using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

/// <summary>Code-only profile review. Neither this object nor its digest is a permit.</summary>
public sealed class QwenCodeOnlyPlan
{
    internal QwenCodeOnlyPlan(string digest, string sourceDigest, string envelopeDigest,
        string executableDigest, string modelDigest, int stdout, int stderr, int chars,
        int tokens, int milliseconds)
        => (ReviewDigest, SourceDigest, EnvelopeDigest, ExecutableDigest, ModelDigest,
            MaxStdoutBytes, MaxStderrBytes, MaxOutputChars, MaxOutputTokens, EffectiveBudgetMilliseconds)
        = (digest, sourceDigest, envelopeDigest, executableDigest, modelDigest,
            stdout, stderr, chars, tokens, milliseconds);
    public string ReviewDigest { get; }
    public string SourceDigest { get; }
    public string EnvelopeDigest { get; }
    public string ExecutableDigest { get; }
    public string ModelDigest { get; }
    public int MaxStdoutBytes { get; }
    public int MaxStderrBytes { get; }
    public int MaxOutputChars { get; }
    public int MaxOutputTokens { get; }
    public int EffectiveBudgetMilliseconds { get; }
    public string Status => "CODE_ONLY_PROFILE_REVIEW_NOT_LIVE_ADMISSION";
    public bool ProcessNetworkIsolationProven => false;
    public bool ModelRequestAuthorized => false;
    public bool DisplayPermitCreated => false;
}

/// <summary>Exact selected resource ceiling; not an OS enforcement receipt.</summary>
public sealed record QwenIsolationRequirements(string Schema, int RequestedGpuLayers,
    bool CpuFallbackAllowed, int MaximumProcesses, bool ServerAllowed, bool NetworkAllowed,
    bool ToolsAllowed, bool GameAccessAllowed, bool DisplayAllowed);

public sealed class QwenAdapterRefusal : Exception
{
    public QwenAdapterRefusal(string classification) : base(classification) { }
}

public static class QwenIsolatedOneShotAdapter
{
    public const string ProfileId = "LLAMA_CPP_B10621_QWEN3_8B_Q4_K_M_V1";
    public const string RuntimeCommit = "c1d0e7a004015f23bc0233470b747b596f29b264";
    public const string ModelSha256 = "d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 8,
    };

    // These are data parsers, not entry points to a runtime or an authority service.
    public static ModelInvocationSourceBinding ParseSource(string json) => ParseClosed<ModelInvocationSourceBinding>(json);
    public static LocalModelInvocationRequestV055 ParseRequest(string json) => ParseClosed<LocalModelInvocationRequestV055>(json);
    public static QwenIsolationRequirements ParseRequirements(string json) => ParseClosed<QwenIsolationRequirements>(json);

    private static T ParseClosed<T>(string json)
    {
        Need(json is not null && json.Length <= 262144, "ENVELOPE_SIZE");
        try
        {
            using var document = JsonDocument.Parse(json!, new JsonDocumentOptions { MaxDepth = 8 });
            Need(document.RootElement.ValueKind == JsonValueKind.Object, "ENVELOPE_OBJECT");
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            var expected = typeof(T).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Need(names.Length == expected.Count && names.Distinct(StringComparer.Ordinal).Count() == names.Length &&
                expected.SetEquals(names), "ENVELOPE_KEYS");
            return document.RootElement.Deserialize<T>(JsonOptions) ?? throw new QwenAdapterRefusal("ENVELOPE_NULL");
        }
        catch (JsonException) { throw new QwenAdapterRefusal("ENVELOPE_MALFORMED"); }
    }

    public static QwenCodeOnlyPlan Review(ModelInvocationSourceBinding source,
        LocalModelInvocationRequestV055 request, QwenIsolationRequirements isolation,
        int foregroundRemainingMilliseconds)
    {
        Need(source is not null && request is not null && isolation is not null, "INPUT_REQUIRED");
        var s = source!; var r = request!; var p = isolation!;
        Need(s.Schema == "matawaka.model-invocation-source-binding/v0.1" &&
            r.Schema == "matawaka.local-model-invocation-request/v0.55" &&
            p.Schema == "matawaka.qwen-isolation-requirements/v0.1", "SCHEMA");
        Need(s.SourceAuthorityEffect == "NONE_BY_SOURCE_RECORD" && s.MaxCalls == 1, "SOURCE_NOT_AUTHORITY");
        Need(s.BindingId is not null && Regex.IsMatch(s.BindingId, "\\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}\\z") &&
            s.BindingId == r.RequestId, "REQUEST_BINDING");
        Need(s.SourceRepository is not null && Regex.IsMatch(s.SourceRepository, "\\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\\z") &&
            Hex(s.SourceFrontier, 40) && Hex(s.SourceArtifactSha256, 64) && Hex(s.RequestEnvelopeSha256, 64), "SOURCE_PROVENANCE");
        Need(s.InvocationProfileId == ProfileId && r.InvocationProfileId == ProfileId, "PROFILE_SUBSTITUTION");
        Need(Hex(s.ExpectedExecutableSha256, 64) && s.ExpectedExecutableSha256 == r.ExpectedExecutableSha256 &&
            s.ExpectedModelSha256 == ModelSha256 && r.ExpectedModelSha256 == ModelSha256, "ARTIFACT_BINDING");
        Need(Hex(r.RuntimeTreeManifestSha256, 64) && Hex(r.ModelAcquisitionReceiptSha256, 64), "ARTIFACT_PROVENANCE");
        Need(Small(r.RuntimeTreeManifestPath) && Small(r.ModelAcquisitionReceiptPath) && Small(r.ModelArtifactId) &&
            r.ExecutableRelativePath == "llama-cli.exe", "ARTIFACT_LOCATOR");
        Need(s.RequireProcessNetworkIsolation && p.RequestedGpuLayers == 99 && !p.CpuFallbackAllowed &&
            p.MaximumProcesses == 1 && !p.ServerAllowed && !p.NetworkAllowed && !p.ToolsAllowed &&
            !p.GameAccessAllowed && !p.DisplayAllowed, "ISOLATION_REQUIREMENTS");
        Need(s.MaxRequestBytes == r.MaxRequestBytes && s.MaxStdoutBytes == r.MaxStdoutBytes &&
            s.MaxStderrBytes == r.MaxStderrBytes && s.MaxOutputChars == r.MaxOutputChars &&
            s.MaxOutputTokens == r.MaxOutputTokens && s.TimeoutSeconds == r.TimeoutSeconds &&
            s.TtlSeconds == r.TtlSeconds, "BOUND_BINDING");
        Need(s.MaxRequestBytes is > 0 and <= 65536 && s.MaxStdoutBytes is > 0 and <= 65536 &&
            s.MaxStderrBytes is > 0 and <= 65536 && s.MaxOutputChars is > 0 and <= 600 &&
            s.MaxOutputTokens is > 0 and <= 160 && s.TimeoutSeconds is > 0 and <= 60 &&
            s.TtlSeconds is > 0 and <= 60, "BOUND_WIDENING");
        Need(r.RequestUtf8 is not null && r.RequestUtf8.Length > 0 && r.RequestUtf8.Length <= s.MaxRequestBytes &&
            !r.RequestUtf8.Contains('\0'), "REQUEST_TEXT");
        Need(Bytes(r.RequestUtf8!).Length <= s.MaxRequestBytes, "REQUEST_UTF8_BOUND");
        // Supplied remaining lifetime is an assertion, NOT authenticated current intent.
        Need(foregroundRemainingMilliseconds is > 0 and <= 30000, "FOREGROUND_BUDGET");
        int budget = Math.Min(foregroundRemainingMilliseconds, Math.Min(s.TimeoutSeconds, s.TtlSeconds) * 1000);
        return new(Digest(new { Schema = "matawaka.qwen-code-only-review/v0.1", Source = s, Request = r,
                Requirements = p, ForegroundRemainingMilliseconds = foregroundRemainingMilliseconds }),
            Digest(s), s.RequestEnvelopeSha256, s.ExpectedExecutableSha256, s.ExpectedModelSha256,
            s.MaxStdoutBytes, s.MaxStderrBytes, s.MaxOutputChars, s.MaxOutputTokens, budget);
    }

    public static void RequireLiveAdmission(QwenCodeOnlyPlan plan)
    {
        Need(plan is not null, "PLAN_REQUIRED");
        // No injected bool/receipt/provider can turn the code-only review into OS proof.
        // A qualified native host implementation must arrive as a separately reviewed successor.
        throw new QwenAdapterRefusal("HOST_ISOLATION_PROVIDER_NOT_QUALIFIED");
    }

    internal static void Need(bool condition, string code) { if (!condition) throw new QwenAdapterRefusal(code); }
    internal static byte[] Bytes(string text)
    {
        try { return Utf8.GetBytes(text); }
        catch (EncoderFallbackException) { throw new QwenAdapterRefusal("UTF8_INVALID"); }
    }
    internal static string Text(byte[] bytes)
    {
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new QwenAdapterRefusal("UTF8_INVALID"); }
    }
    internal static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(Bytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    internal static string TextDigest(string text) => Convert.ToHexString(SHA256.HashData(Bytes(text))).ToLowerInvariant();
    private static bool Hex(string? value, int length) => value is not null && value.Length == length &&
        value.Any(c => c != '0') && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Small(string? value)
    {
        if (value is null || value.Length is 0 or > 1024 || value.Any(char.IsControl)) return false;
        _ = Bytes(value); // Reject invalid UTF-16 instead of permitting serializer replacement collisions.
        return true;
    }
}
