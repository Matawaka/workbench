using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelInvocationSourceBinding(
    string Schema, string BindingId, string SourceRepository, string SourceFrontier,
    string SourceArtifactSha256, string RequestEnvelopeSha256, string SourceAuthorityEffect,
    string InvocationProfileId, string ExpectedExecutableSha256, string ExpectedModelSha256,
    int MaxRequestBytes, int MaxStdoutBytes, int MaxStderrBytes, int MaxOutputChars,
    int MaxOutputTokens, int TimeoutSeconds, int TtlSeconds, int MaxCalls,
    bool RequireProcessNetworkIsolation);

public sealed record ModelBindingPreview(string Schema, string ReviewDigestSha256,
    string SourceDigestSha256, string RequestTextSha256, string Status);

// This token carries neither the inner bearer nor the request text. Only its owner can use it.
public sealed class SourceBoundModelPermit
{
    internal SourceBoundModelPermit(string reviewDigest, string leaseDigest)
        => (ReviewDigestSha256, LeaseDigestSha256) = (reviewDigest, leaseDigest);
    public string ReviewDigestSha256 { get; }
    public string LeaseDigestSha256 { get; }
}

public sealed record SourceBoundModelOutput(string Schema, string SourceDigestSha256,
    string RequestEnvelopeSha256, string ReviewDigestSha256, string LeaseDigestSha256,
    string ExecutionReceiptDigestSha256, string PortableResultDigestSha256,
    string OutputText, string OutputTextSha256, string SourceClass,
    bool ContentReviewComplete, bool DisplayPermitCreated, bool ProcessNetworkIsolationProven,
    string Status);

/// <summary>
/// Separate model authority above the existing v0.55 model lease. The process-only
/// v0.55 provenance grant is deliberately not an accepted input to this API.
/// No UI/IPC registration is introduced here.
/// </summary>
public sealed class SourceBoundModelInvocation
{
    public const string SourceSchema = "matawaka.model-invocation-source-binding/v0.1";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly BoundedLocalModelInvocationV055Service inner = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<SourceBoundModelPermit, Prepared> prepared = new();
    private readonly HashSet<string> issuedBindings = new(StringComparer.Ordinal);

    public static ModelInvocationSourceBinding ParseSource(string json)
    {
        using var document = JsonDocument.Parse(json);
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "SOURCE_OBJECT_REQUIRED");
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        var expected = typeof(ModelInvocationSourceBinding).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Require(names.Length == expected.Count && names.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            expected.SetEquals(names), "SOURCE_KEYS_INVALID");
        return document.RootElement.Deserialize<ModelInvocationSourceBinding>() ?? throw new InvalidDataException("SOURCE_OBJECT_REQUIRED");
    }

    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text))).ToLowerInvariant();
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string classification)
    {
        if (!condition) throw new InvalidDataException(classification);
    }
    private static bool Hex(string? value, int length) => value is not null &&
        Regex.IsMatch(value, $"^[0-9a-f]{{{length}}}$", RegexOptions.CultureInvariant) && value.Any(c => c != '0');

    public static void ValidateBinding(ModelInvocationSourceBinding s, LocalModelInvocationRequestV055 r)
    {
        Require(s is not null && r is not null, "BINDING_REQUIRED");
        Require(s!.Schema == SourceSchema && r!.Schema == BoundedLocalModelInvocationV055Service.RequestSchema,
            "MODEL_AUTHORITY_SCHEMA_REQUIRED");
        Require(s.SourceAuthorityEffect == "NONE_BY_SOURCE_RECORD" && s.MaxCalls == 1, "SOURCE_NOT_AUTHORITY");
        Require(s.BindingId is not null && Regex.IsMatch(s.BindingId, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$") &&
            s.BindingId == r.RequestId, "REQUEST_ID_MISMATCH");
        Require(s.SourceRepository is not null && Regex.IsMatch(s.SourceRepository, "^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$") &&
            Hex(s.SourceFrontier, 40) && Hex(s.SourceArtifactSha256, 64) && Hex(s.RequestEnvelopeSha256, 64), "SOURCE_PROVENANCE_INVALID");
        Require(Hex(s.ExpectedExecutableSha256, 64) && Hex(s.ExpectedModelSha256, 64) &&
            s.ExpectedExecutableSha256 == r.ExpectedExecutableSha256 && s.ExpectedModelSha256 == r.ExpectedModelSha256,
            "ARTIFACT_BINDING_MISMATCH");
        Require(s.InvocationProfileId == r.InvocationProfileId, "PROFILE_SUBSTITUTION");
        Require(s.InvocationProfileId == BoundedLocalModelInvocationV055Service.FixtureProfileId, "INVOCATION_PROFILE_UNSUPPORTED");
        Require(!s.RequireProcessNetworkIsolation, "PROCESS_NETWORK_ISOLATION_UNPROVEN");
        Require(s.MaxRequestBytes == r.MaxRequestBytes && s.MaxStdoutBytes == r.MaxStdoutBytes &&
            s.MaxStderrBytes == r.MaxStderrBytes && s.MaxOutputChars == r.MaxOutputChars &&
            s.MaxOutputTokens == r.MaxOutputTokens && s.TimeoutSeconds == r.TimeoutSeconds && s.TtlSeconds == r.TtlSeconds,
            "SOURCE_BOUND_MISMATCH");
        Require(s.MaxRequestBytes is > 0 and <= BoundedLocalModelInvocationV055Service.HardMaxRequestBytes &&
            s.MaxStdoutBytes is > 0 and <= BoundedLocalModelInvocationV055Service.HardMaxStdoutBytes &&
            s.MaxStderrBytes is > 0 and <= BoundedLocalModelInvocationV055Service.HardMaxStderrBytes &&
            s.MaxOutputChars is > 0 and <= BoundedLocalModelInvocationV055Service.HardMaxOutputChars &&
            s.MaxOutputTokens is > 0 and <= BoundedLocalModelInvocationV055Service.HardMaxOutputTokens &&
            s.TimeoutSeconds is > 0 and <= BoundedLocalModelInvocationV055Service.MaxTimeoutSeconds &&
            s.TtlSeconds is > 0 and <= BoundedLocalModelInvocationV055Service.MaxTtlSeconds,
            "BOUND_INVALID");
        Require(r.RequestUtf8 is not null && Utf8.GetByteCount(r.RequestUtf8) > 0 &&
            Utf8.GetByteCount(r.RequestUtf8) <= s.MaxRequestBytes, "REQUEST_TEXT_BOUND");
    }

    private static ModelBindingPreview Review(ModelInvocationSourceBinding s, LocalModelInvocationRequestV055 r)
    {
        ValidateBinding(s, r);
        // Includes all request settings; raw text is hashed and never written by this wrapper.
        return new("matawaka.model-binding-preview/v0.1", Digest(new { Source = s, Request = r }),
            Digest(s), Hash(r.RequestUtf8), "PREVIEW_NOT_AUTHORITY");
    }

    public ModelBindingPreview Preview(string workspace, ModelInvocationSourceBinding s,
        LocalModelInvocationRequestV055 r, CancellationToken cancellationToken)
    {
        var review = Review(s, r);
        _ = inner.Preview(workspace, r, cancellationToken);
        return review;
    }

    public async Task<SourceBoundModelPermit> GrantAsync(string workspace, ModelInvocationSourceBinding s,
        LocalModelInvocationRequestV055 r, string reviewedDigest, bool explicitModelConfirmation,
        CancellationToken cancellationToken)
    {
        Require(explicitModelConfirmation, "SEPARATE_MODEL_CONFIRMATION_REQUIRED");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var review = Review(s, r);
            Require(review.ReviewDigestSha256 == reviewedDigest, "REVIEW_DRIFT");
            Require(!issuedBindings.Contains(s.BindingId), "BINDING_ALREADY_ISSUED");
            var preview = inner.Preview(workspace, r, cancellationToken);
            var made = await inner.GrantAsync(workspace, preview, r.RequestUtf8, cancellationToken);
            var permit = new SourceBoundModelPermit(review.ReviewDigestSha256, Digest(made.Receipt));
            prepared.Add(permit, new(s, r, made.Grant, review, Path.GetFullPath(workspace),
                made.Receipt.LeaseStateSha256));
            issuedBindings.Add(s.BindingId);
            return permit;
        }
        finally { gate.Release(); }
    }

    public async Task<SourceBoundModelOutput> InvokeAsync(SourceBoundModelPermit permit,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Require(prepared.Remove(permit, out var p), "MODEL_PERMIT_UNAVAILABLE");
            // Removal precedes every failure path; rejection cannot resurrect this token.
            cancellationToken.ThrowIfCancellationRequested();
            Require(DateTimeOffset.Now < p!.Grant.ExpiresAt, "MODEL_PERMIT_EXPIRED");
            Require(HashFile(p.Grant.LeaseStatePath) == p.StateSha256, "MODEL_STATE_DRIFT");
            _ = inner.Preview(p.Workspace, p.Request, cancellationToken);
            var executed = await inner.InvokeAsync(p.Workspace, p.Grant, cancellationToken);
            Require(executed.Result is not null, "MODEL_OUTPUT_ABSENT");
            return BindOutput(p.Source, p.Request, p.Review, permit,
                executed.Receipt, executed.Result!, p.Grant.LeaseId);
        }
        finally { gate.Release(); }
    }

    // Pure validation shared with qualification. No receipt input can grant/invoke authority.
    public static SourceBoundModelOutput BindOutput(ModelInvocationSourceBinding s,
        LocalModelInvocationRequestV055 r, ModelBindingPreview review, SourceBoundModelPermit permit,
        LocalModelInvocationExecutionReceiptV055 receipt, LocalModelInvocationPortableResultV055 result,
        string expectedLeaseId)
    {
        var rebuilt = Review(s, r);
        Require(rebuilt == review && permit.ReviewDigestSha256 == review.ReviewDigestSha256, "OUTPUT_REVIEW_BINDING");
        Require(receipt.Schema == BoundedLocalModelInvocationV055Service.ExecutionReceiptSchema &&
            receipt.Version == BoundedLocalModelInvocationV055Service.Version &&
            result.Schema == BoundedLocalModelInvocationV055Service.PortableResultSchema &&
            result.Version == BoundedLocalModelInvocationV055Service.Version, "OUTPUT_SCHEMA");
        Require(receipt.LeaseId == expectedLeaseId && receipt.RequestId == s.BindingId && result.RequestId == s.BindingId &&
            receipt.InvocationProfileId == s.InvocationProfileId && result.InvocationProfileId == s.InvocationProfileId &&
            receipt.RequestDigestSha256 == Hash(r.RequestUtf8) && result.RequestDigestSha256 == Hash(r.RequestUtf8) &&
            receipt.RequestBytes == Utf8.GetByteCount(r.RequestUtf8) && result.RequestBytes == receipt.RequestBytes,
            "OUTPUT_REQUEST_BINDING");
        Require(receipt.RuntimeTreeManifestSha256 == r.RuntimeTreeManifestSha256 &&
            receipt.ModelAcquisitionReceiptSha256 == r.ModelAcquisitionReceiptSha256 &&
            receipt.ModelArtifactId == r.ModelArtifactId && result.ModelArtifactId == r.ModelArtifactId &&
            receipt.ExecutableSha256BeforeStart == s.ExpectedExecutableSha256 &&
            receipt.ObservedProcessImageSha256 == s.ExpectedExecutableSha256 && result.ExecutableSha256 == s.ExpectedExecutableSha256 &&
            receipt.ModelSha256BeforeStart == s.ExpectedModelSha256 && result.ModelSha256 == s.ExpectedModelSha256,
            "OUTPUT_ARTIFACT_BINDING");
        Require(receipt.Status == "UNTRUSTED_LOCAL_MODEL_OUTPUT" && receipt.State == "MODEL_INVOCATION_COMPLETED" &&
            result.Status == "UNTRUSTED_LOCAL_MODEL_OUTPUT" && receipt.FailureClassification is null &&
            receipt.ModelInvocationAuthorityConsumed && receipt.OneRequestAttempted && receipt.ExactProcessImageVerified,
            "OUTPUT_TERMINAL_STATE");
        Require(!receipt.WorkbenchNetworkTransportPerformed && !receipt.ServerOrPortRequestedByInvocationProfile &&
            !receipt.ProcessNetworkIsolationProven && !receipt.AutomaticRetryPerformed && !receipt.AutomaticResumePerformed &&
            !receipt.BenchmarkPerformed && !receipt.GameAccessPerformed && !receipt.DisplayPerformed &&
            !receipt.ResponseAuthorityCreated && !receipt.ActionPermitCreated && !receipt.SuccessorPermitCreated &&
            !result.ContentReviewComplete && !result.FactualTruthProven && !result.ResponseAuthorityCreated &&
            !result.DisplayPermitCreated && !result.GameAuthorityCreated && !result.ActionPermitCreated && !result.SuccessorPermitCreated,
            "OUTPUT_AUTHORITY_WIDENING");
        Require(receipt.StdoutBytesObserved > 0 && receipt.StdoutBytesObserved <= s.MaxStdoutBytes &&
            receipt.StderrBytesObserved >= 0 && receipt.StderrBytesObserved <= s.MaxStderrBytes &&
            Hex(result.StdoutSha256, 64) && receipt.StdoutSha256 == result.StdoutSha256 &&
            receipt.OutputArtifactSha256 == result.StdoutSha256, "OUTPUT_STREAM_BINDING");
        Require(result.OutputText is not null && result.OutputText.Length > 0 && result.OutputText.Length <= s.MaxOutputChars &&
            result.OutputText == result.OutputText.Trim() &&
            !result.OutputText.Any(c => char.IsControl(c) && c is not '\n' and not '\t') &&
            result.OutputChars == result.OutputText.Length && receipt.OutputChars == result.OutputChars &&
            result.OutputTextSha256 == Hash(result.OutputText), "OUTPUT_TEXT_BINDING");
        return new("matawaka.source-bound-model-output/v0.1", review.SourceDigestSha256,
            s.RequestEnvelopeSha256, review.ReviewDigestSha256, permit.LeaseDigestSha256,
            Digest(receipt), Digest(result), result.OutputText!, result.OutputTextSha256,
            "FIXTURE_OUTPUT_NOT_LIVE_MODEL", false, false, false, "UNTRUSTED_OUTPUT_PENDING_CALLER_REVIEW");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private sealed record Prepared(ModelInvocationSourceBinding Source, LocalModelInvocationRequestV055 Request,
        LocalModelInvocationGrantV055 Grant, ModelBindingPreview Review, string Workspace, string StateSha256);
}
