using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record RuntimeExecutionSourceBindingV055(
    string Schema,
    string BindingId,
    string SourceRepository,
    string SourceFrontier,
    string SourceArtifactSha256,
    string RequestEnvelopeSha256,
    string SourceAuthorityEffect,
    string ProcessEffectCeiling,
    bool OneShot,
    int MaxCalls,
    int TtlSeconds,
    bool FreshHumanConfirmationRequired,
    bool NetworkAuthorized,
    bool ModelRequestAuthorized,
    bool GameAccessAuthorized,
    bool DisplayAuthorized);

public sealed record ProvenanceBoundRuntimeExecutionPreviewV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    RuntimeExecutionSourceBindingV055 SourceBinding,
    string SourceBindingDigestSha256,
    RuntimeExecutionPreviewV053 RuntimePreview,
    string BindingDigestSha256,
    bool SourceRecordGrantedAuthority,
    bool ProcessExecutionPerformed,
    bool ReadyForExplicitConfirmation,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record ProvenanceBoundRuntimeExecutionLeaseStateV055(
    string Schema,
    string Version,
    string LeaseId,
    string BindingId,
    string SourceBindingDigestSha256,
    string BindingDigestSha256,
    string RuntimeRequestDigestSha256,
    string InnerAuthorityReceiptDigestSha256,
    string BearerSha256,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    int RemainingCalls,
    string State,
    bool Completed,
    bool Failed,
    string? FailureClassification,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record ProvenanceBoundRuntimeExecutionGrantV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string BindingId,
    string SourceBindingDigestSha256,
    string BindingDigestSha256,
    string RuntimeRequestDigestSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    string AuthorityReceiptPath,
    string AuthorityReceiptSha256,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool InnerLeaseBearerExposed,
    bool BearerPersistedInPlaintextByWorkbench,
    bool ProcessExecutionPerformed,
    string Note);

public sealed record ProvenanceBoundRuntimeExecutionAuthorityReceiptV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    RuntimeExecutionSourceBindingV055 SourceBinding,
    string SourceBindingDigestSha256,
    string BindingDigestSha256,
    string RuntimeRequestDigestSha256,
    string InnerAuthorityReceiptDigestSha256,
    string BearerSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool SourceRecordGrantedAuthority,
    bool InnerLeaseBearerExposed,
    bool BearerPlaintextPersisted,
    bool ProcessExecutionPerformed,
    bool ModelRequestAuthorized,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record ProvenanceBoundRuntimeExecutionReceiptV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string BindingId,
    string SourceBindingDigestSha256,
    string BindingDigestSha256,
    string RuntimeRequestDigestSha256,
    string InnerAuthorityReceiptDigestSha256,
    RuntimeExecutionReceiptV053 RuntimeExecutionReceipt,
    string RuntimeExecutionReceiptDigestSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    bool ProvenanceAuthorityConsumed,
    bool InnerLeaseBearerExposed,
    bool AutomaticRetryPerformed,
    bool AutomaticResumePerformed,
    bool NetworkAuthorized,
    bool ModelRequestAuthorized,
    bool GameAccessAuthorized,
    bool DisplayAuthorized,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class ProvenanceBoundRuntimeExecutionExceptionV055 : IOException
{
    public string Classification { get; }

    public ProvenanceBoundRuntimeExecutionExceptionV055(string classification, string message) : base(message)
        => Classification = classification;

    public ProvenanceBoundRuntimeExecutionExceptionV055(string classification, string message, Exception inner) : base(message, inner)
        => Classification = classification;
}

public sealed class ProvenanceBoundRuntimeExecutionV055Service : IDisposable
{
    public const string Version = "0.55.0";
    public const string SourceBindingSchema = "matawaka.runtime-execution-source-binding/v0.55";
    public const string PreviewSchema = "matawaka.provenance-bound-runtime-execution-preview/v0.55";
    public const string LeaseStateSchema = "matawaka.provenance-bound-runtime-execution-lease-state/v0.55";
    public const string GrantSchema = "matawaka.provenance-bound-runtime-execution-grant/v0.55";
    public const string AuthorityReceiptSchema = "matawaka.provenance-bound-runtime-execution-authority-receipt/v0.55";
    public const string ExecutionReceiptSchema = "matawaka.provenance-bound-runtime-execution-receipt/v0.55";
    public const string SourceAuthorityEffect = "NONE_BY_SOURCE_RECORD";
    public const string ProcessEffectCeiling = "EXACT_RUNTIME_ONLY";

    private static readonly Regex BindingIdRegex = new("^[a-z0-9][a-z0-9:._-]{7,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RepositoryRegex = new("^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly JsonSerializerOptions DigestJsonOptions = new() { WriteIndented = false };

    private readonly BoundedRuntimeExecutionV053Service _inner = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PreparedInnerLease> _prepared = new(StringComparer.Ordinal);
    private bool _disposed;

    public ProvenanceBoundRuntimeExecutionPreviewV055 Preview(
        string workspaceRoot,
        RuntimeExecutionSourceBindingV055 sourceBinding,
        RuntimeExecutionRequestV053 runtimeRequest,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateSourceBinding(sourceBinding);
        if (runtimeRequest is null) throw Refused("RUNTIME_REQUEST_REFUSED", "An exact v0.53 runtime request is required.");
        if (!string.Equals(runtimeRequest.RequestId, sourceBinding.BindingId, StringComparison.Ordinal))
            throw Refused("REQUEST_BINDING_ID_MISMATCH", "The v0.53 RequestId must equal the exact external BindingId.");
        if (runtimeRequest.TtlSeconds != sourceBinding.TtlSeconds)
            throw Refused("REQUEST_TTL_MISMATCH", "The v0.53 TTL must equal the exact source binding TTL.");

        var runtimePreview = _inner.Preview(workspaceRoot, runtimeRequest, cancellationToken);
        var sourceDigest = HashRecord(sourceBinding);
        var bindingDigest = ComputeBindingDigest(sourceBinding, sourceDigest, runtimePreview.RequestDigestSha256);
        return new ProvenanceBoundRuntimeExecutionPreviewV055(
            PreviewSchema, Version, DateTimeOffset.Now, sourceBinding, sourceDigest,
            runtimePreview, bindingDigest, false, false, true, BaseNonEffects(),
            "Source evidence and exact v0.53 request are digest-bound. Preview performs no grant or process effect.");
    }

    public async Task<(ProvenanceBoundRuntimeExecutionGrantV055 Grant, ProvenanceBoundRuntimeExecutionAuthorityReceiptV055 Receipt, string ReceiptPath)> GrantAsync(
        string workspaceRoot,
        RuntimeExecutionSourceBindingV055 sourceBinding,
        RuntimeExecutionRequestV053 runtimeRequest,
        string expectedBindingDigestSha256,
        bool explicitConfirmation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        RequireSha256(expectedBindingDigestSha256, "expected binding digest");
        if (!explicitConfirmation)
            throw Refused("EXPLICIT_CONFIRMATION_REQUIRED", "A source record or preview cannot create execution authority; explicit current confirmation is required.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_prepared.Values.Any(x => string.Equals(x.BindingId, sourceBinding.BindingId, StringComparison.Ordinal)))
                throw Refused("BINDING_ALREADY_PREPARED", "This exact BindingId already has a process-local prepared lease.");

            var preview = Preview(workspaceRoot, sourceBinding, runtimeRequest, cancellationToken);
            if (!CryptographicEquals(preview.BindingDigestSha256, expectedBindingDigestSha256))
                throw Refused("PREVIEW_BINDING_DIGEST_MISMATCH", "The freshly recomputed source/request binding differs from the explicitly reviewed preview.");

            var inner = await _inner.GrantAsync(workspaceRoot, preview.RuntimePreview, cancellationToken).ConfigureAwait(false);
            var innerReceiptDigest = HashRecord(inner.Receipt);
            var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var now = DateTimeOffset.Now;
            var state = new ProvenanceBoundRuntimeExecutionLeaseStateV055(
                LeaseStateSchema, Version, inner.Grant.LeaseId, sourceBinding.BindingId,
                preview.SourceBindingDigestSha256, preview.BindingDigestSha256,
                preview.RuntimePreview.RequestDigestSha256, innerReceiptDigest, HashText(bearer),
                now, inner.Grant.ExpiresAt, 1, 1, "PROVENANCE_BOUND_EXECUTION_PREPARED",
                false, false, null, 1, BaseNonEffects(),
                "Outer authority is provenance-bound and process-local; the inner v0.53 bearer is neither returned nor persisted by v0.55.");
            var root = ResolveRepositoryRoot(workspaceRoot);
            var statePath = StatePath(root, inner.Grant.LeaseId);
            await WriteJsonAtomicAsync(statePath, state, cancellationToken).ConfigureAwait(false);

            var receipt = new ProvenanceBoundRuntimeExecutionAuthorityReceiptV055(
                AuthorityReceiptSchema, Version, now, inner.Grant.LeaseId, sourceBinding,
                preview.SourceBindingDigestSha256, preview.BindingDigestSha256,
                preview.RuntimePreview.RequestDigestSha256, innerReceiptDigest, state.BearerSha256,
                statePath, HashFile(statePath), inner.Grant.ExpiresAt, 1,
                false, false, false, false, false, BaseNonEffects(),
                "PROVENANCE_BOUND_EXECUTION_AUTHORITY_PREPARED",
                "Explicit confirmation created one outer lease. Source evidence remains non-authoritative and no process has started.");
            var receiptPath = ReceiptPath(root, "authority", inner.Grant.LeaseId);
            await WriteJsonAtomicAsync(receiptPath, receipt, cancellationToken).ConfigureAwait(false);

            var grant = new ProvenanceBoundRuntimeExecutionGrantV055(
                GrantSchema, Version, now, inner.Grant.LeaseId, bearer, sourceBinding.BindingId,
                preview.SourceBindingDigestSha256, preview.BindingDigestSha256,
                preview.RuntimePreview.RequestDigestSha256, statePath, HashFile(statePath),
                receiptPath, HashFile(receiptPath), inner.Grant.ExpiresAt, 1,
                false, false, false,
                "Only the outer bearer is returned. Restart or loss of this service instance cannot resume the hidden inner v0.53 authority.");
            _prepared.Add(grant.LeaseId, new PreparedInnerLease(sourceBinding.BindingId, inner.Grant, innerReceiptDigest));
            return (grant, receipt, receiptPath);
        }
        finally
        {
            // If outer materialization failed, the unreturned v0.53 bearer becomes unreachable.
            _gate.Release();
        }
    }

    public async Task<(ProvenanceBoundRuntimeExecutionReceiptV055 Receipt, string ReceiptPath)> ExecuteAsync(
        string workspaceRoot,
        ProvenanceBoundRuntimeExecutionGrantV055 grant,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ProvenanceBoundRuntimeExecutionLeaseStateV055? consumed = null;
        try
        {
            var root = ResolveRepositoryRoot(workspaceRoot);
            var statePath = ValidateStatePath(root, grant.LeaseStatePath, grant.LeaseId);
            if (!CryptographicEquals(HashFile(statePath), grant.LeaseStateSha256))
                throw Refused("PROVENANCE_STATE_HASH_MISMATCH", "Persisted provenance state differs from the exact outer grant.");
            var state = ReadState(statePath);
            ValidateGrantAgainstState(grant, state);
            var authorityPath = ValidateAuthorityReceiptPath(root, grant.AuthorityReceiptPath, grant.LeaseId);
            if (!CryptographicEquals(HashFile(authorityPath), grant.AuthorityReceiptSha256))
                throw Refused("PROVENANCE_AUTHORITY_RECEIPT_HASH_MISMATCH", "Persisted authority receipt differs from the exact outer grant.");
            var authorityReceipt = ReadAuthorityReceipt(authorityPath);
            ValidateAuthorityReceipt(authorityReceipt, grant, state);
            if (!_prepared.TryGetValue(grant.LeaseId, out var prepared) ||
                !string.Equals(prepared.BindingId, grant.BindingId, StringComparison.Ordinal) ||
                !CryptographicEquals(prepared.InnerAuthorityReceiptDigestSha256, state.InnerAuthorityReceiptDigestSha256))
                throw Refused("PROCESS_LOCAL_INNER_AUTHORITY_UNAVAILABLE", "The hidden v0.53 bearer is not owned by this exact service instance; restart does not resume authority.");
            if (state.Completed || state.Failed || state.RemainingCalls != 1 || state.State != "PROVENANCE_BOUND_EXECUTION_PREPARED")
                throw Refused("PROVENANCE_AUTHORITY_UNAVAILABLE", "The exact provenance-bound lease is no longer available.");
            if (DateTimeOffset.Now >= state.ExpiresAt)
                throw Refused("PROVENANCE_LEASE_EXPIRED", "The provenance-bound lease expired before consumption.");
            if (!CryptographicEquals(HashText(grant.Bearer), state.BearerSha256))
                throw Refused("PROVENANCE_BEARER_REFUSED", "The outer bearer does not match exact persisted authority.");

            consumed = state with
            {
                RemainingCalls = 0,
                State = "PROVENANCE_AUTHORITY_CONSUMED",
                StateRevision = state.StateRevision + 1,
                Note = "Outer authority was durably consumed before delegating to the hidden v0.53 one-shot grant."
            };
            await WriteJsonAtomicAsync(statePath, consumed, cancellationToken).ConfigureAwait(false);

            var inner = await _inner.ExecuteAsync(workspaceRoot, prepared.Grant, cancellationToken).ConfigureAwait(false);
            var completed = consumed with
            {
                State = inner.Receipt.Status,
                Completed = true,
                StateRevision = consumed.StateRevision + 1,
                Note = "Exact v0.53 execution receipt returned through the provenance-bound outer lease; no higher-layer authority was created."
            };
            await WriteJsonAtomicAsync(statePath, completed, CancellationToken.None).ConfigureAwait(false);
            _prepared.Remove(grant.LeaseId);

            var receipt = new ProvenanceBoundRuntimeExecutionReceiptV055(
                ExecutionReceiptSchema, Version, DateTimeOffset.Now, grant.LeaseId, grant.BindingId,
                grant.SourceBindingDigestSha256, grant.BindingDigestSha256, grant.RuntimeRequestDigestSha256,
                state.InnerAuthorityReceiptDigestSha256, inner.Receipt, HashRecord(inner.Receipt),
                statePath, HashFile(statePath), true, false, false, false,
                false, false, false, false, BaseNonEffects(), inner.Receipt.Status,
                "Receipt binds source evidence to the exact consumed v0.53 lease. It does not authorize a model request, game access or display.");
            var receiptPath = ReceiptPath(root, "execution", inner.Receipt.TransactionId);
            await WriteJsonAtomicAsync(receiptPath, receipt, CancellationToken.None).ConfigureAwait(false);
            return (receipt, receiptPath);
        }
        catch (OperationCanceledException)
        {
            if (consumed is not null) await MarkFailedAsync(grant.LeaseStatePath, consumed, "PROVENANCE_EXECUTION_CANCELLED").ConfigureAwait(false);
            _prepared.Remove(grant.LeaseId);
            throw;
        }
        catch (ProvenanceBoundRuntimeExecutionExceptionV055)
        {
            if (consumed is not null) await MarkFailedAsync(grant.LeaseStatePath, consumed, "PROVENANCE_EXECUTION_REFUSED").ConfigureAwait(false);
            if (consumed is not null) _prepared.Remove(grant.LeaseId);
            throw;
        }
        catch (RuntimeExecutionExceptionV053 ex)
        {
            if (consumed is not null) await MarkFailedAsync(grant.LeaseStatePath, consumed, ex.Classification).ConfigureAwait(false);
            _prepared.Remove(grant.LeaseId);
            throw new ProvenanceBoundRuntimeExecutionExceptionV055(ex.Classification, ex.Message, ex);
        }
        catch (Exception ex)
        {
            if (consumed is not null) await MarkFailedAsync(grant.LeaseStatePath, consumed, "PROVENANCE_EXECUTION_FAILED").ConfigureAwait(false);
            if (consumed is not null) _prepared.Remove(grant.LeaseId);
            throw new ProvenanceBoundRuntimeExecutionExceptionV055(
                "PROVENANCE_EXECUTION_FAILED",
                "Provenance-bound execution failed closed; no retry or resume authority was created.",
                ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<(RuntimeExecutionStopReceiptV053 Receipt, string ReceiptPath)> StopActiveOwnedRuntimeAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
        => _inner.StopActiveOwnedRuntimeAsync(workspaceRoot, cancellationToken);

    public bool HasActiveOwnedRuntime => _inner.HasActiveOwnedRuntime;

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("source-schema", SourceBindingSchema == "matawaka.runtime-execution-source-binding/v0.55", SourceBindingSchema, "v0.55"),
        ("source-not-authority", SourceAuthorityEffect == "NONE_BY_SOURCE_RECORD", SourceAuthorityEffect, "NONE_BY_SOURCE_RECORD"),
        ("process-ceiling", ProcessEffectCeiling == "EXACT_RUNTIME_ONLY", ProcessEffectCeiling, "EXACT_RUNTIME_ONLY"),
        ("inner-bearer-hidden", true, "v0.53 grant retained in process-local dictionary", "not returned or persisted by v0.55"),
        ("one-shot", true, "MaxCalls and source MaxCalls fixed to 1", "1"),
        ("restart", true, "inner grant bearer is process-local only", "Restart != Resume Authority"),
        ("delegation", true, "v0.55 consumes outer state before BoundedRuntimeExecutionV053Service.ExecuteAsync", "two distinct gates"),
        ("higher-effects", true, "network/model/game/display fixed false", "false")
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prepared.Clear();
        _inner.Dispose();
        _gate.Dispose();
    }

    private static void ValidateSourceBinding(RuntimeExecutionSourceBindingV055 value)
    {
        if (value is null || value.Schema != SourceBindingSchema)
            throw Refused("SOURCE_BINDING_SCHEMA_REFUSED", $"Expected exact schema {SourceBindingSchema}.");
        if (!BindingIdRegex.IsMatch(value.BindingId)) throw Refused("BINDING_ID_REFUSED", "BindingId format is invalid.");
        if (!RepositoryRegex.IsMatch(value.SourceRepository)) throw Refused("SOURCE_REPOSITORY_REFUSED", "SourceRepository must be exact owner/repository identity.");
        RequireHex(value.SourceFrontier, 40, "SourceFrontier");
        RequireSha256(value.SourceArtifactSha256, "SourceArtifactSha256");
        RequireSha256(value.RequestEnvelopeSha256, "RequestEnvelopeSha256");
        if (value.SourceAuthorityEffect != SourceAuthorityEffect)
            throw Refused("SOURCE_AUTHORITY_EFFECT_REFUSED", "Source evidence must explicitly create no execution authority.");
        if (value.ProcessEffectCeiling != ProcessEffectCeiling || !value.OneShot || value.MaxCalls != 1)
            throw Refused("PROCESS_EFFECT_CEILING_REFUSED", "Only one exact-runtime process call is admissible.");
        if (value.TtlSeconds < BoundedRuntimeExecutionV053Service.MinTtlSeconds || value.TtlSeconds > BoundedRuntimeExecutionV053Service.MaxTtlSeconds)
            throw Refused("SOURCE_TTL_REFUSED", "Source binding TTL is outside the v0.53 execution corridor.");
        if (!value.FreshHumanConfirmationRequired)
            throw Refused("CONFIRMATION_REQUIREMENT_REFUSED", "Source binding must preserve a separate fresh confirmation requirement.");
        if (value.NetworkAuthorized || value.ModelRequestAuthorized || value.GameAccessAuthorized || value.DisplayAuthorized)
            throw Refused("HIGHER_EFFECT_AUTHORITY_REFUSED", "This lease layer cannot authorize network, model, game or display effects.");
    }

    private static void ValidateGrantAgainstState(ProvenanceBoundRuntimeExecutionGrantV055 grant, ProvenanceBoundRuntimeExecutionLeaseStateV055 state)
    {
        if (grant is null || grant.Schema != GrantSchema || grant.Version != Version ||
            state.Schema != LeaseStateSchema || state.Version != Version ||
            grant.LeaseId != state.LeaseId || grant.BindingId != state.BindingId ||
            !CryptographicEquals(grant.SourceBindingDigestSha256, state.SourceBindingDigestSha256) ||
            !CryptographicEquals(grant.BindingDigestSha256, state.BindingDigestSha256) ||
            !CryptographicEquals(grant.RuntimeRequestDigestSha256, state.RuntimeRequestDigestSha256) ||
            !CryptographicEquals(grant.LeaseStateSha256, HashRecordForPersistedState(state)) ||
            grant.MaxCalls != 1 || grant.InnerLeaseBearerExposed || grant.BearerPersistedInPlaintextByWorkbench || grant.ProcessExecutionPerformed)
            throw Refused("PROVENANCE_GRANT_REFUSED", "Outer grant and persisted provenance state do not match.");
    }

    private static void ValidateAuthorityReceipt(
        ProvenanceBoundRuntimeExecutionAuthorityReceiptV055 receipt,
        ProvenanceBoundRuntimeExecutionGrantV055 grant,
        ProvenanceBoundRuntimeExecutionLeaseStateV055 state)
    {
        if (receipt.Schema != AuthorityReceiptSchema || receipt.Version != Version ||
            receipt.LeaseId != grant.LeaseId || receipt.SourceBinding.BindingId != grant.BindingId ||
            !CryptographicEquals(HashRecord(receipt.SourceBinding), receipt.SourceBindingDigestSha256) ||
            !CryptographicEquals(receipt.SourceBindingDigestSha256, state.SourceBindingDigestSha256) ||
            !CryptographicEquals(receipt.BindingDigestSha256, state.BindingDigestSha256) ||
            !CryptographicEquals(receipt.RuntimeRequestDigestSha256, state.RuntimeRequestDigestSha256) ||
            !CryptographicEquals(receipt.InnerAuthorityReceiptDigestSha256, state.InnerAuthorityReceiptDigestSha256) ||
            !CryptographicEquals(receipt.BearerSha256, state.BearerSha256) ||
            !CryptographicEquals(receipt.LeaseStateSha256, grant.LeaseStateSha256) ||
            receipt.SourceRecordGrantedAuthority || receipt.InnerLeaseBearerExposed || receipt.BearerPlaintextPersisted ||
            receipt.ProcessExecutionPerformed || receipt.ModelRequestAuthorized || receipt.MaxCalls != 1 ||
            receipt.Status != "PROVENANCE_BOUND_EXECUTION_AUTHORITY_PREPARED")
            throw Refused("PROVENANCE_AUTHORITY_RECEIPT_REFUSED", "Authority receipt does not match the exact source, request and outer lease state.");
    }

    private static string ComputeBindingDigest(RuntimeExecutionSourceBindingV055 source, string sourceDigest, string requestDigest)
        => HashText(string.Join("\n", new[]
        {
            source.Schema, source.BindingId, source.SourceRepository, source.SourceFrontier.ToLowerInvariant(),
            source.SourceArtifactSha256.ToLowerInvariant(), source.RequestEnvelopeSha256.ToLowerInvariant(),
            sourceDigest.ToLowerInvariant(), requestDigest.ToLowerInvariant(), source.SourceAuthorityEffect,
            source.ProcessEffectCeiling, source.TtlSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "one-shot=1", "network=0", "model=0", "game=0", "display=0"
        }));

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw Refused("WORKSPACE_REFUSED", "Workspace root is required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench")));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw Refused("WORKSPACE_REFUSED", $"Workbench Git repository missing: {root}");
        return root;
    }

    private static string StatePath(string root, string leaseId)
    {
        var dir = Path.Combine(root, "artifacts", "runtime-execution", "provenance-leases");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"provenance-bound-runtime-execution-lease-v0.55-{leaseId}.json");
    }

    private static string ReceiptPath(string root, string kind, string id)
    {
        var dir = Path.Combine(root, "artifacts", "runtime-execution", "provenance-receipts");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"provenance-bound-runtime-execution-{kind}-v0.55-{id}.json");
    }

    private static string ValidateStatePath(string root, string supplied, string leaseId)
    {
        var expected = Path.GetFullPath(Path.Combine(root, "artifacts", "runtime-execution", "provenance-leases",
            $"provenance-bound-runtime-execution-lease-v0.55-{leaseId}.json"));
        var actual = Path.GetFullPath(supplied);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw Refused("PROVENANCE_STATE_PATH_REFUSED", "Lease state path is outside the exact Workbench-owned v0.55 location.");
        return actual;
    }

    private static string ValidateAuthorityReceiptPath(string root, string supplied, string leaseId)
    {
        var expected = Path.GetFullPath(Path.Combine(root, "artifacts", "runtime-execution", "provenance-receipts",
            $"provenance-bound-runtime-execution-authority-v0.55-{leaseId}.json"));
        var actual = Path.GetFullPath(supplied);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(actual))
            throw Refused("PROVENANCE_AUTHORITY_RECEIPT_PATH_REFUSED", "Authority receipt path is not the exact Workbench-owned v0.55 receipt.");
        return actual;
    }

    private static ProvenanceBoundRuntimeExecutionLeaseStateV055 ReadState(string path)
    {
        if (!File.Exists(path)) throw Refused("PROVENANCE_STATE_MISSING", "Persisted provenance-bound lease state is missing.");
        try
        {
            return JsonSerializer.Deserialize<ProvenanceBoundRuntimeExecutionLeaseStateV055>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Provenance lease state deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw new ProvenanceBoundRuntimeExecutionExceptionV055("PROVENANCE_STATE_INVALID", "Persisted provenance-bound state is invalid.", ex);
        }
    }

    private static ProvenanceBoundRuntimeExecutionAuthorityReceiptV055 ReadAuthorityReceipt(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ProvenanceBoundRuntimeExecutionAuthorityReceiptV055>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Authority receipt deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw new ProvenanceBoundRuntimeExecutionExceptionV055("PROVENANCE_AUTHORITY_RECEIPT_INVALID", "Persisted authority receipt is invalid.", ex);
        }
    }

    private static async Task MarkFailedAsync(string statePath, ProvenanceBoundRuntimeExecutionLeaseStateV055 state, string classification)
    {
        try
        {
            var failed = state with
            {
                RemainingCalls = 0,
                State = "PROVENANCE_EXECUTION_TERMINAL_FAIL_CLOSED",
                Completed = true,
                Failed = true,
                FailureClassification = classification,
                StateRevision = state.StateRevision + 1,
                Note = "Failure after outer authority consumption creates no retry or resume authority."
            };
            await WriteJsonAtomicAsync(statePath, failed, CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string HashRecord<T>(T value) => HashText(JsonSerializer.Serialize(value, DigestJsonOptions));

    private static string HashRecordForPersistedState(ProvenanceBoundRuntimeExecutionLeaseStateV055 value)
        => HashText(JsonSerializer.Serialize(value, JsonOptions));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool CryptographicEquals(string left, string right)
    {
        RequireSha256(left, "left digest");
        RequireSha256(right, "right digest");
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
    }

    private static void RequireSha256(string value, string role) => RequireHex(value, 64, role);

    private static void RequireHex(string value, int length, string role)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != length || value.Any(ch => !Uri.IsHexDigit(ch)) || value.All(ch => ch == '0'))
            throw Refused("DIGEST_REFUSED", $"{role} must be exact non-zero {length}-character hexadecimal evidence.");
    }

    private static IReadOnlyList<string> BaseNonEffects() => new[]
    {
        "External Intent != Execution Authority",
        "Source Receipt != Capability Lease",
        "Capability Lease != Model Request Authority",
        "Process Started != Runtime Ready",
        "Runtime Ready != Model Request Authority",
        "Restart != Resume Authority",
        "inner v0.53 bearer is not returned or persisted by v0.55",
        "no automatic retry or resume",
        "no network/model/game/display authority",
        "no KONTUR-specific behavior in the generic lease service"
    };

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProvenanceBoundRuntimeExecutionV055Service));
    }

    private static ProvenanceBoundRuntimeExecutionExceptionV055 Refused(string classification, string message)
        => new(classification, message);

    private sealed record PreparedInnerLease(string BindingId, RuntimeExecutionGrantV053 Grant, string InnerAuthorityReceiptDigestSha256);
}
