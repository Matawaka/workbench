using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.51.11 additive exact-prepared-LeaseId creation corridor. It preserves the
/// canonical v0.48 state/grant/creation-receipt schemas and v0.51.5 derived-index
/// dirty/fence semantics, but lets the owner->lease transaction name the exact
/// LeaseId before canonical state exists. Prepared id != created authority.
/// </summary>
public sealed class LocalAppPreparedIndexedLeaseV05111Service
{
    public const string Version = "0.51.11";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly LocalAppReadLeaseV048Service _leases = new();
    private readonly LocalAppActiveLeaseIndexV0515Service _index = new();
    private readonly LocalAppActiveIndexFenceV0516Service _fence = new();

    public async Task<LocalAppIndexedLeaseCreateResultV0515> CreatePreparedIndexedAsync(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeasePreviewV048 confirmedPreview,
        string preparedLeaseId,
        bool clipboardWritePerformed,
        CancellationToken cancellationToken)
    {
        if (!SafeLeaseId(preparedLeaseId))
            throw new InvalidDataException("Unsafe exact prepared LeaseId for v0.51.11 creation.");
        if (confirmedPreview is null || !confirmedPreview.ReadyForExplicitLeaseAuthority)
            throw new InvalidDataException("A READY v0.48 lease preview is required for v0.51.11 prepared creation.");
        if (!selectedApplicationId.Equals(confirmedPreview.ApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Selected application changed after lease preview.");

        var request = new LocalAppReadLeaseRequestV048(
            LocalAppReadLeaseV048Service.RequestSchema,
            confirmedPreview.RequestId,
            confirmedPreview.ApplicationId,
            confirmedPreview.Scopes,
            confirmedPreview.MaxBytesPerRead,
            confirmedPreview.MaxTotalBytes,
            confirmedPreview.MaxCalls,
            confirmedPreview.TtlSeconds);
        var fresh = _leases.Preview(workspaceRoot, selectedApplicationId, request, cancellationToken);
        RequireSamePreview(confirmedPreview, fresh);

        await using var fence = await _fence.AcquireAsync(
            workspaceRoot, selectedApplicationId, "create-prepared-indexed-read-lease-v0.51.11", cancellationToken);
        var mutation = await _index.BeginMutationAsync(
            workspaceRoot, selectedApplicationId, "create-prepared-live-lease-v0.51.11", preparedLeaseId, cancellationToken);

        var now = DateTimeOffset.Now;
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var bearerSha = HashText(bearer);
        var state = new LocalAppReadLeaseStateV048(
            LocalAppReadLeaseV048Service.StateSchema,
            LocalAppReadLeaseV048Service.Version,
            preparedLeaseId,
            fresh.RequestId,
            fresh.ApplicationId,
            fresh.Scopes,
            now,
            now.AddSeconds(fresh.TtlSeconds),
            fresh.MaxBytesPerRead,
            fresh.MaxTotalBytes,
            fresh.MaxTotalBytes,
            fresh.MaxCalls,
            fresh.MaxCalls,
            bearerSha,
            false,
            null,
            null,
            0,
            CanonicalNonEffects(),
            "v0.51.11 used an exact LeaseId already named by a non-authoritative owner->lease PREPARED_BINDING transaction. Canonical authority begins only when this v0.48-schema state file is materialized; bearer plaintext is not persisted.");

        var statePath = ResolveExactStatePath(workspaceRoot, selectedApplicationId, preparedLeaseId, createDirectory: true);
        if (File.Exists(statePath))
            throw new InvalidDataException("PREPARED_LEASE_ID_COLLISION: exact prepared LeaseId already has canonical state; no replacement state was written.");
        await WriteStateAtomicAsync(statePath, state, cancellationToken);

        var grant = new LocalAppReadLeaseGrantV048(
            LocalAppReadLeaseV048Service.GrantSchema,
            LocalAppReadLeaseV048Service.Version,
            DateTimeOffset.Now,
            state.LeaseId,
            bearer,
            state.ApplicationId,
            state.Scopes,
            state.IssuedAt,
            state.ExpiresAt,
            state.MaxBytesPerRead,
            state.MaxTotalBytes,
            state.MaxCalls,
            false,
            false,
            "Bearer is returned once for the inherited bounded local adapter/session. The exact LeaseId was prepared before state creation, but preparation alone granted no authority.");

        var receipt = new LocalAppReadLeaseCreationReceiptV048(
            LocalAppReadLeaseV048Service.CreationReceiptSchema,
            LocalAppReadLeaseV048Service.Version,
            DateTimeOffset.Now,
            state.LeaseId,
            state.ApplicationId,
            state.BearerSha256,
            statePath,
            LocalAppV046FileBoundary.HashFile(statePath),
            state.ExpiresAt,
            state.MaxBytesPerRead,
            state.MaxTotalBytes,
            state.MaxCalls,
            false,
            clipboardWritePerformed,
            false,
            CanonicalNonEffects(),
            "READ_LEASE_CREATED_EXACT_PREPARED_ID_LOCAL_HASH_ONLY_NO_NETWORK",
            "Explicit human-confirmed read-session start materialized one canonical v0.48-schema lease at the exact previously prepared LeaseId. The prepared transaction itself did not create authority.");
        var receiptPath = await WriteCreationReceiptAsync(workspaceRoot, selectedApplicationId, preparedLeaseId, receipt, cancellationToken);

        var index = await _index.CommitMutationAsync(workspaceRoot, mutation, preparedLeaseId, cancellationToken);
        return new LocalAppIndexedLeaseCreateResultV0515(
            grant, receipt, receiptPath, index.IndexRevision, index.Entries.Count);
    }

    public static string ResolveExactStatePath(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        bool createDirectory = false)
    {
        if (!SafeLeaseId(leaseId)) throw new InvalidDataException("Unsafe LeaseId for exact canonical state path.");
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var root = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-leases"));
        if (createDirectory) Directory.CreateDirectory(root);
        if (Directory.Exists(root)) LocalAppV046FileBoundary.RejectReparse(root, "v0.51.11 read lease state root");
        var appDir = Path.GetFullPath(Path.Combine(root, LocalAppV046FileBoundary.SafeToken(applicationId)));
        if (createDirectory) Directory.CreateDirectory(appDir);
        if (Directory.Exists(appDir)) LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.11 read lease state app directory");
        var path = Path.Combine(appDir, LocalAppV046FileBoundary.SafeToken(leaseId) + ".json");
        if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "v0.51.11 exact canonical lease state");
        return path;
    }

    public static LocalAppReadLeaseStateV048 ReadExactCanonicalState(
        string workspaceRoot,
        string applicationId,
        string leaseId)
    {
        var path = ResolveExactStatePath(workspaceRoot, applicationId, leaseId, createDirectory: false);
        if (!File.Exists(path)) throw new FileNotFoundException("Exact canonical read lease state is absent.", path);
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.11 exact canonical lease state read");
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                    ?? throw new InvalidDataException("Exact canonical read lease state is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Exact canonical read lease state JSON is invalid.", ex);
        }
        if (state.Schema != LocalAppReadLeaseV048Service.StateSchema ||
            state.Version != LocalAppReadLeaseV048Service.Version ||
            !state.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            !state.LeaseId.Equals(leaseId, StringComparison.Ordinal))
            throw new InvalidDataException("Exact canonical read lease state identity/schema mismatch.");
        return state;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("prepared-lease-v05111-id", true, "exact LeaseId named before state write", "prepared != created"),
        ("prepared-lease-v05111-schema", true, "v0.48 state/grant/creation receipt schemas", "preserved"),
        ("prepared-lease-v05111-index", true, "v0.51.5 dirty marker + commit under v0.51.6 fence", "preserved"),
        ("prepared-lease-v05111-bearer", true, "plaintext one-time grant; SHA only persisted", "preserved"),
        ("prepared-lease-v05111-collision", true, "existing exact state refuses replacement", "fail closed"),
        ("prepared-lease-v05111-network", true, "false", "false")
    };

    private static async Task WriteStateAtomicAsync(
        string path,
        LocalAppReadLeaseStateV048 state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.11 prepared lease state");
            if (File.Exists(path)) throw new InvalidDataException("PREPARED_LEASE_ID_COLLISION: canonical state appeared before commit.");
            File.Move(temp, path, false);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.11 prepared canonical lease state");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static async Task<string> WriteCreationReceiptAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        LocalAppReadLeaseCreationReceiptV048 receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-read-leases");
        var path = Path.Combine(dir,
            $"read-lease-creation-prepared-v05111-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{LocalAppV046FileBoundary.SafeToken(leaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static void RequireSamePreview(LocalAppReadLeasePreviewV048 a, LocalAppReadLeasePreviewV048 b)
    {
        if (a.RequestId != b.RequestId || a.ApplicationId != b.ApplicationId ||
            a.MaxBytesPerRead != b.MaxBytesPerRead || a.MaxTotalBytes != b.MaxTotalBytes ||
            a.MaxCalls != b.MaxCalls || a.TtlSeconds != b.TtlSeconds || a.Scopes.Count != b.Scopes.Count ||
            !a.Scopes.Zip(b.Scopes).All(pair => pair.First.Role == pair.Second.Role && pair.First.PathPrefix == pair.Second.PathPrefix))
            throw new InvalidDataException("Read lease preview is stale or no longer equivalent; create a new preview.");
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 &&
           value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string[] CanonicalNonEffects() => new[]
    {
        "prepared LeaseId is evidence/name reservation only until canonical v0.48 state exists",
        "bearer plaintext is not persisted to state or receipt",
        "no network/tunnel/publication/catalog mutation",
        "no application/source mutation",
        "no process/Agent Execute/ActionPermit authority"
    };
}
