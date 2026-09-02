using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppReadLeaseScopeV048(string Role, string PathPrefix);

public sealed record LocalAppReadLeaseRequestV048(
    string Schema,
    string RequestId,
    string ApplicationId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    int MaxBytesPerRead,
    long MaxTotalBytes,
    int MaxCalls,
    int TtlSeconds);

public sealed record LocalAppReadLeasePreviewV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string ApplicationId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    int MaxBytesPerRead,
    long MaxTotalBytes,
    int MaxCalls,
    int TtlSeconds,
    DateTimeOffset ProposedExpiresAt,
    bool ContainsFileContents,
    bool ReadyForExplicitLeaseAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppReadLeaseStateV048(
    string Schema,
    string Version,
    string LeaseId,
    string CreatedRequestId,
    string ApplicationId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaxBytesPerRead,
    long MaxTotalBytes,
    long RemainingBytes,
    int MaxCalls,
    int RemainingCalls,
    string BearerSha256,
    bool Revoked,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastConsumedAt,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppReadLeaseGrantV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string ApplicationId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaxBytesPerRead,
    long MaxTotalBytes,
    int MaxCalls,
    bool BearerStoredInPlaintextByWorkbench,
    bool NetworkTransportImplemented,
    string Note);

public sealed record LocalAppReadLeaseCreationReceiptV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string ApplicationId,
    string BearerSha256,
    string StatePath,
    string StateSha256,
    DateTimeOffset ExpiresAt,
    int MaxBytesPerRead,
    long MaxTotalBytes,
    int MaxCalls,
    bool BearerPlaintextPersisted,
    bool ClipboardWritePerformed,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppLeaseReadRequestV048(
    string Schema,
    string RequestId,
    string LeaseId,
    string Bearer,
    string ApplicationId,
    string Role,
    string RelativePath,
    long Offset,
    int MaxBytes,
    string? ExpectedFileSha256);

public sealed record LocalAppReadLeaseResponseV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string LeaseId,
    string ApplicationId,
    string Role,
    string RelativePath,
    long FileBytes,
    string FileSha256,
    long Offset,
    int ReturnedBytes,
    bool EndOfFile,
    string ContentBase64,
    string? Utf8Text,
    int RemainingCalls,
    long RemainingBytes,
    DateTimeOffset ExpiresAt,
    bool NetworkAccessPerformed,
    bool FileMutationPerformed,
    bool ProcessLaunchPerformed,
    string Note);

public sealed record LocalAppReadLeaseConsumptionReceiptV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string LeaseId,
    string ApplicationId,
    string Role,
    string RelativePath,
    string FileSha256,
    long Offset,
    int ReturnedBytes,
    int RemainingCalls,
    long RemainingBytes,
    DateTimeOffset ExpiresAt,
    long StateRevision,
    bool BearerVerified,
    bool ScopeVerified,
    bool FreshHashVerified,
    bool NetworkAccessPerformed,
    bool FileMutationPerformed,
    bool ProcessLaunchPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppReadLeaseRevokeReceiptV048(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    int ObservedActiveLeases,
    int RevokedLeases,
    IReadOnlyList<string> LeaseIds,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppReadLeaseV048Service
{
    public const string Version = "0.48.0";
    public const string RequestSchema = "matawaka.local-app-read-lease-request/v0.48";
    public const string PreviewSchema = "matawaka.local-app-read-lease-preview/v0.48";
    public const string StateSchema = "matawaka.local-app-read-lease-state/v0.48";
    public const string GrantSchema = "matawaka.local-app-read-lease-grant/v0.48";
    public const string CreationReceiptSchema = "matawaka.local-app-read-lease-creation-receipt/v0.48";
    public const string ReadRequestSchema = "matawaka.local-app-lease-read-request/v0.48";
    public const string ReadResponseSchema = "matawaka.local-app-read-lease-response/v0.48";
    public const string ConsumptionReceiptSchema = "matawaka.local-app-read-lease-consumption-receipt/v0.48";
    public const string RevokeReceiptSchema = "matawaka.local-app-read-lease-revoke-receipt/v0.48";
    public const int MaxScopes = 16;
    public const int MaxBytesPerRead = LocalAppReadToolV046Service.MaxReadBytes;
    public const long MaxTotalBytes = 8L * 1024L * 1024L;
    public const int MaxCalls = 32;
    public const int MaxTtlSeconds = 15 * 60;

    private static readonly SemaphoreSlim StateGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private readonly LocalAppChatReadRelayV047Service _relay = new();

    public LocalAppReadLeasePreviewV048 PreviewFromJson(
        string workspaceRoot,
        string selectedApplicationId,
        string requestJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) throw new InvalidDataException("Read lease request JSON is empty.");
        ValidateExactRequestShape(requestJson);
        LocalAppReadLeaseRequestV048 request;
        try
        {
            request = JsonSerializer.Deserialize<LocalAppReadLeaseRequestV048>(requestJson, JsonOptions)
                ?? throw new InvalidDataException("Read lease request JSON could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Read lease request JSON is invalid.", ex);
        }
        return Preview(workspaceRoot, selectedApplicationId, request, cancellationToken);
    }

    public LocalAppReadLeasePreviewV048 Preview(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeaseRequestV048 request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Schema != RequestSchema)
            throw new InvalidDataException("Exact v0.48 read lease request schema is required.");
        if (!SafeRequestId(request.RequestId)) throw new InvalidDataException("RequestId is unsafe.");
        if (!string.Equals(request.ApplicationId, selectedApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Lease request ApplicationId does not match the explicitly selected registered application.");
        if (request.Scopes is null || request.Scopes.Count == 0 || request.Scopes.Count > MaxScopes)
            throw new InvalidDataException($"Scopes must contain 1..{MaxScopes} entries.");
        if (request.MaxBytesPerRead <= 0 || request.MaxBytesPerRead > MaxBytesPerRead)
            throw new InvalidDataException($"MaxBytesPerRead must be between 1 and {MaxBytesPerRead}.");
        if (request.MaxTotalBytes <= 0 || request.MaxTotalBytes > MaxTotalBytes)
            throw new InvalidDataException($"MaxTotalBytes must be between 1 and {MaxTotalBytes}.");
        if (request.MaxTotalBytes < request.MaxBytesPerRead)
            throw new InvalidDataException("MaxTotalBytes cannot be smaller than MaxBytesPerRead.");
        if (request.MaxCalls <= 0 || request.MaxCalls > MaxCalls)
            throw new InvalidDataException($"MaxCalls must be between 1 and {MaxCalls}.");
        if (request.TtlSeconds <= 0 || request.TtlSeconds > MaxTtlSeconds)
            throw new InvalidDataException($"TtlSeconds must be between 1 and {MaxTtlSeconds}.");

        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, request.ApplicationId);
        var scopes = request.Scopes.Select(scope => NormalizeAndValidateScope(workspaceRoot, request.ApplicationId, scope, cancellationToken)).ToArray();
        var duplicates = scopes.GroupBy(x => $"{x.Role}\0{x.PathPrefix}", StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException("Duplicate read lease scope is not allowed.");
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.Now;
        return new LocalAppReadLeasePreviewV048(
            PreviewSchema,
            Version,
            now,
            request.RequestId,
            request.ApplicationId,
            scopes,
            request.MaxBytesPerRead,
            request.MaxTotalBytes,
            request.MaxCalls,
            request.TtlSeconds,
            now.AddSeconds(request.TtlSeconds),
            false,
            true,
            DefaultNonEffects(),
            "Lease Request != Lease Authority. Preview validates only selected app, normalized scopes and ceilings; it reads/discloses no application contents and creates no bearer or lease authority.");
    }

    public async Task<(LocalAppReadLeaseGrantV048 Grant, LocalAppReadLeaseCreationReceiptV048 Receipt, string ReceiptPath)> CreateAsync(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeasePreviewV048 confirmedPreview,
        bool clipboardWritePerformed,
        CancellationToken cancellationToken)
    {
        if (confirmedPreview is null || !confirmedPreview.ReadyForExplicitLeaseAuthority)
            throw new InvalidDataException("A READY v0.48 lease preview is required.");
        if (!string.Equals(selectedApplicationId, confirmedPreview.ApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Selected application changed after lease preview.");

        var request = new LocalAppReadLeaseRequestV048(
            RequestSchema,
            confirmedPreview.RequestId,
            confirmedPreview.ApplicationId,
            confirmedPreview.Scopes,
            confirmedPreview.MaxBytesPerRead,
            confirmedPreview.MaxTotalBytes,
            confirmedPreview.MaxCalls,
            confirmedPreview.TtlSeconds);
        var fresh = Preview(workspaceRoot, selectedApplicationId, request, cancellationToken);
        RequireSamePreview(confirmedPreview, fresh);

        var now = DateTimeOffset.Now;
        var leaseId = "lease-" + Guid.NewGuid().ToString("N");
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var bearerSha = HashText(bearer);
        var state = new LocalAppReadLeaseStateV048(
            StateSchema,
            Version,
            leaseId,
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
            DefaultNonEffects(),
            "Only SHA-256 of the 256-bit bearer is persisted. The plaintext bearer is returned once in the grant object and is not written by this service to lease state or receipts.");

        await StateGate.WaitAsync(cancellationToken);
        string statePath;
        try
        {
            statePath = StatePath(workspaceRoot, state.ApplicationId, state.LeaseId);
            if (File.Exists(statePath)) throw new InvalidDataException("Generated lease id unexpectedly collides with existing state.");
            await WriteStateAtomicAsync(statePath, state, cancellationToken);
        }
        finally
        {
            StateGate.Release();
        }

        var grant = new LocalAppReadLeaseGrantV048(
            GrantSchema,
            Version,
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
            "Bearer is shown to the operator once for a future bounded adapter/session. Possession cannot exceed lease scopes, byte/call ceilings or expiry; v0.48 has no network/listener/tunnel/MCP transport.");
        var receipt = new LocalAppReadLeaseCreationReceiptV048(
            CreationReceiptSchema,
            Version,
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
            DefaultNonEffects(),
            "READ_LEASE_CREATED_LOCAL_HASH_ONLY_NO_NETWORK",
            "Explicit human confirmation created only a short-lived bounded local read lease. Bearer plaintext is not persisted by the lease service; network transport is absent.");
        var receiptPath = await WriteReceiptAsync(workspaceRoot, "creation", state.ApplicationId, state.LeaseId, receipt, cancellationToken);
        return (grant, receipt, receiptPath);
    }

    public async Task<(LocalAppReadLeaseResponseV048 Response, LocalAppReadLeaseConsumptionReceiptV048 Receipt, string ReceiptPath)> AuthorizeAndReadAsync(
        string workspaceRoot,
        LocalAppLeaseReadRequestV048 request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Schema != ReadRequestSchema)
            throw new InvalidDataException("Exact v0.48 lease read request schema is required.");
        if (!SafeRequestId(request.RequestId) || !SafeLeaseId(request.LeaseId))
            throw new InvalidDataException("Unsafe read RequestId or LeaseId.");
        if (string.IsNullOrWhiteSpace(request.Bearer)) throw new InvalidDataException("Lease bearer is required.");
        if (request.MaxBytes <= 0 || request.MaxBytes > MaxBytesPerRead)
            throw new InvalidDataException($"MaxBytes must be between 1 and {MaxBytesPerRead}.");

        await StateGate.WaitAsync(cancellationToken);
        try
        {
            var statePath = StatePath(workspaceRoot, request.ApplicationId, request.LeaseId);
            var state = ReadState(statePath, request.ApplicationId, request.LeaseId);
            ValidateLiveState(state);
            VerifyBearer(state, request.Bearer);
            if (request.MaxBytes > state.MaxBytesPerRead)
                throw new InvalidDataException("Requested MaxBytes exceeds lease MaxBytesPerRead.");
            if (state.RemainingCalls <= 0) throw new InvalidDataException("Read lease call budget is exhausted.");
            if (state.RemainingBytes <= 0 || request.MaxBytes > state.RemainingBytes)
                throw new InvalidDataException("Requested MaxBytes exceeds remaining lease byte budget.");

            var role = request.Role.Trim().ToLowerInvariant();
            var relative = LocalAppV046FileBoundary.NormalizeRelative(request.RelativePath);
            if (!ScopeAllows(state.Scopes, role, relative))
                throw new InvalidDataException("Requested role/path is outside the lease scope.");

            var relayRequest = new LocalAppChatReadRequestV047(
                LocalAppChatReadRelayV047Service.RequestSchema,
                request.RequestId,
                request.ApplicationId,
                role,
                relative,
                request.Offset,
                request.MaxBytes,
                request.ExpectedFileSha256);
            var preview = _relay.Preview(workspaceRoot, state.ApplicationId, relayRequest, cancellationToken);
            var read = _relay.PrepareConfirmedRead(workspaceRoot, state.ApplicationId, preview, cancellationToken);

            var remainingCalls = state.RemainingCalls - 1;
            var remainingBytes = state.RemainingBytes - read.ReturnedBytes;
            if (remainingBytes < 0) throw new InvalidDataException("Lease accounting would underflow remaining bytes.");
            var next = state with
            {
                RemainingCalls = remainingCalls,
                RemainingBytes = remainingBytes,
                LastConsumedAt = DateTimeOffset.Now,
                StateRevision = state.StateRevision + 1
            };
            await WriteStateAtomicAsync(statePath, next, cancellationToken);

            var response = new LocalAppReadLeaseResponseV048(
                ReadResponseSchema,
                Version,
                DateTimeOffset.Now,
                request.RequestId,
                state.LeaseId,
                state.ApplicationId,
                role,
                relative,
                read.FileBytes,
                read.FileSha256,
                read.Offset,
                read.ReturnedBytes,
                read.EndOfFile,
                read.ContentBase64,
                read.Utf8Text,
                remainingCalls,
                remainingBytes,
                next.ExpiresAt,
                false,
                false,
                false,
                "Authorized only by an already-created bounded read lease. This service performs no network transport, file mutation or process launch.");
            var receipt = new LocalAppReadLeaseConsumptionReceiptV048(
                ConsumptionReceiptSchema,
                Version,
                DateTimeOffset.Now,
                request.RequestId,
                state.LeaseId,
                state.ApplicationId,
                role,
                relative,
                read.FileSha256,
                read.Offset,
                read.ReturnedBytes,
                remainingCalls,
                remainingBytes,
                next.ExpiresAt,
                next.StateRevision,
                true,
                true,
                true,
                false,
                false,
                false,
                DefaultNonEffects(),
                "READ_LEASE_CONSUMED_BOUNDED_NO_NETWORK",
                "One bounded read was consumed atomically from the local lease state. The bearer is not written to the receipt.");
            var receiptPath = await WriteReceiptAsync(workspaceRoot, "consumption", state.ApplicationId, state.LeaseId, receipt, cancellationToken);
            return (response, receipt, receiptPath);
        }
        finally
        {
            StateGate.Release();
        }
    }

    public IReadOnlyList<LocalAppReadLeaseStateV048> ListActive(string workspaceRoot, string applicationId)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var dir = StateDirectory(workspaceRoot, applicationId);
        if (!Directory.Exists(dir)) return Array.Empty<LocalAppReadLeaseStateV048>();
        var now = DateTimeOffset.Now;
        var result = new List<LocalAppReadLeaseStateV048>();
        foreach (var file in Directory.EnumerateFiles(dir, "lease-*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(file, Encoding.UTF8), JsonOptions);
                if (state is not null && state.Schema == StateSchema && state.ApplicationId == applicationId && !state.Revoked && state.ExpiresAt > now && state.RemainingCalls > 0 && state.RemainingBytes > 0)
                    result.Add(state);
            }
            catch (JsonException)
            {
                throw new InvalidDataException($"Unreadable read lease state: {file}");
            }
        }
        return result.OrderBy(x => x.ExpiresAt).ThenBy(x => x.LeaseId, StringComparer.Ordinal).ToArray();
    }

    public async Task<(LocalAppReadLeaseRevokeReceiptV048 Receipt, string ReceiptPath)> RevokeAllActiveAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await StateGate.WaitAsync(cancellationToken);
        try
        {
            var active = ListActive(workspaceRoot, applicationId);
            var revoked = new List<string>();
            foreach (var state in active)
            {
                var path = StatePath(workspaceRoot, applicationId, state.LeaseId);
                var fresh = ReadState(path, applicationId, state.LeaseId);
                if (fresh.Revoked || fresh.ExpiresAt <= DateTimeOffset.Now) continue;
                var next = fresh with { Revoked = true, RevokedAt = DateTimeOffset.Now, StateRevision = fresh.StateRevision + 1 };
                await WriteStateAtomicAsync(path, next, cancellationToken);
                revoked.Add(state.LeaseId);
            }
            var receipt = new LocalAppReadLeaseRevokeReceiptV048(
                RevokeReceiptSchema,
                Version,
                DateTimeOffset.Now,
                applicationId,
                active.Count,
                revoked.Count,
                revoked,
                false,
                DefaultNonEffects(),
                "READ_LEASES_REVOKED_LOCAL_ONLY",
                "Revocation changes only ignored local lease state. It creates no network, mutation, execution or future read authority.");
            var receiptPath = await WriteReceiptAsync(workspaceRoot, "revoke", applicationId, "all", receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        finally
        {
            StateGate.Release();
        }
    }

    public static string SerializeGrant(LocalAppReadLeaseGrantV048 grant) => JsonSerializer.Serialize(grant, JsonOptions);

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("lease-v048-max-per-read", MaxBytesPerRead == 1048576, MaxBytesPerRead.ToString(), "1048576"),
        ("lease-v048-max-total", MaxTotalBytes == 8388608, MaxTotalBytes.ToString(), "8388608"),
        ("lease-v048-max-calls", MaxCalls == 32, MaxCalls.ToString(), "32"),
        ("lease-v048-max-ttl", MaxTtlSeconds == 900, MaxTtlSeconds.ToString(), "900"),
        ("lease-v048-bearer-persisted", true, "sha256 only", "sha256 only"),
        ("lease-v048-network", true, "not implemented", "not implemented"),
        ("lease-v048-write-authority", true, "false", "false")
    };

    private static LocalAppReadLeaseScopeV048 NormalizeAndValidateScope(
        string workspaceRoot,
        string applicationId,
        LocalAppReadLeaseScopeV048 scope,
        CancellationToken cancellationToken)
    {
        if (scope is null) throw new InvalidDataException("Null lease scope is not allowed.");
        var role = scope.Role.Trim().ToLowerInvariant();
        var root = role switch
        {
            "installed" => LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId),
            "source" => LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, applicationId, requireBinding: true),
            _ => throw new InvalidDataException("Lease scope role must be exactly installed or source.")
        };
        var raw = scope.PathPrefix.Replace('\\', '/').Trim();
        var isPrefix = raw.EndsWith('/', StringComparison.Ordinal);
        var core = raw.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(core) || core == ".") throw new InvalidDataException("Lease scope cannot be application-root wildcard.");
        var normalizedCore = LocalAppV046FileBoundary.NormalizeRelative(core);
        LocalAppV046FileBoundary.EnsureNoReparseBoundary(root, normalizedCore);
        var path = Path.GetFullPath(Path.Combine(root, normalizedCore.Replace('/', Path.DirectorySeparatorChar)));
        LocalAppV046FileBoundary.EnsureInsideRoot(root, path, "lease scope");
        if (isPrefix)
        {
            if (!Directory.Exists(path)) throw new InvalidDataException($"Lease directory-prefix scope does not exist: {normalizedCore}/");
            LocalAppV046FileBoundary.RejectReparse(path, "lease directory-prefix scope");
        }
        else
        {
            if (!File.Exists(path)) throw new InvalidDataException($"Lease exact-file scope does not exist: {normalizedCore}");
            LocalAppV046FileBoundary.RejectReparse(path, "lease exact-file scope");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new LocalAppReadLeaseScopeV048(role, normalizedCore + (isPrefix ? "/" : string.Empty));
    }

    private static bool ScopeAllows(IReadOnlyList<LocalAppReadLeaseScopeV048> scopes, string role, string relative)
    {
        foreach (var scope in scopes)
        {
            if (!scope.Role.Equals(role, StringComparison.OrdinalIgnoreCase)) continue;
            if (scope.PathPrefix.EndsWith('/', StringComparison.Ordinal))
            {
                if (relative.StartsWith(scope.PathPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (relative.Equals(scope.PathPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void ValidateLiveState(LocalAppReadLeaseStateV048 state)
    {
        if (state.Schema != StateSchema || state.Version != Version) throw new InvalidDataException("Unexpected read lease state schema/version.");
        if (state.Revoked) throw new InvalidDataException("Read lease is revoked.");
        if (state.ExpiresAt <= DateTimeOffset.Now) throw new InvalidDataException("Read lease is expired.");
        if (state.RemainingCalls <= 0) throw new InvalidDataException("Read lease call budget is exhausted.");
        if (state.RemainingBytes <= 0) throw new InvalidDataException("Read lease byte budget is exhausted.");
    }

    private static void VerifyBearer(LocalAppReadLeaseStateV048 state, string bearer)
    {
        var observed = Convert.FromHexString(HashText(bearer));
        var expected = Convert.FromHexString(state.BearerSha256);
        if (!CryptographicOperations.FixedTimeEquals(observed, expected)) throw new InvalidDataException("Read lease bearer mismatch.");
    }

    private static LocalAppReadLeaseStateV048 ReadState(string path, string applicationId, string leaseId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Read lease state is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, "read lease state");
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Read lease state could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Read lease state JSON is invalid.", ex);
        }
        if (state.ApplicationId != applicationId || state.LeaseId != leaseId)
            throw new InvalidDataException("Read lease state identity mismatch.");
        return state;
    }

    private static async Task WriteStateAtomicAsync(string path, LocalAppReadLeaseStateV048 state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary read lease state");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "read lease state");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string StateDirectory(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace, "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var stateRoot = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-leases"));
        Directory.CreateDirectory(stateRoot);
        var app = Path.GetFullPath(Path.Combine(stateRoot, LocalAppV046FileBoundary.SafeToken(applicationId)));
        Directory.CreateDirectory(app);
        return app;
    }

    private static string StatePath(string workspaceRoot, string applicationId, string leaseId)
    {
        if (!SafeLeaseId(leaseId)) throw new InvalidDataException("Unsafe LeaseId.");
        return Path.Combine(StateDirectory(workspaceRoot, applicationId), LocalAppV046FileBoundary.SafeToken(leaseId) + ".json");
    }

    private static async Task<string> WriteReceiptAsync<T>(
        string workspaceRoot,
        string kind,
        string applicationId,
        string leaseId,
        T receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-read-leases");
        var path = Path.Combine(dir, $"read-lease-{LocalAppV046FileBoundary.SafeToken(kind)}-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{LocalAppV046FileBoundary.SafeToken(leaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static void RequireSamePreview(LocalAppReadLeasePreviewV048 a, LocalAppReadLeasePreviewV048 b)
    {
        if (a.RequestId != b.RequestId || a.ApplicationId != b.ApplicationId || a.MaxBytesPerRead != b.MaxBytesPerRead ||
            a.MaxTotalBytes != b.MaxTotalBytes || a.MaxCalls != b.MaxCalls || a.TtlSeconds != b.TtlSeconds ||
            a.Scopes.Count != b.Scopes.Count || !a.Scopes.Zip(b.Scopes).All(pair => pair.First.Role == pair.Second.Role && pair.First.PathPrefix == pair.Second.PathPrefix))
            throw new InvalidDataException("Read lease preview is stale or no longer equivalent; create a new preview.");
    }

    private static void ValidateExactRequestShape(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Read lease request must be one JSON object.");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Schema", "RequestId", "ApplicationId", "Scopes", "MaxBytesPerRead", "MaxTotalBytes", "MaxCalls", "TtlSeconds"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw new InvalidDataException($"Unknown read lease request field: {property.Name}");
            if (!seen.Add(property.Name)) throw new InvalidDataException($"Duplicate read lease request field: {property.Name}");
        }
        foreach (var required in allowed)
            if (!seen.Contains(required)) throw new InvalidDataException($"Missing read lease request field: {required}");
        if (doc.RootElement.GetProperty("Scopes").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Scopes must be a JSON array.");
        foreach (var scope in doc.RootElement.GetProperty("Scopes").EnumerateArray())
        {
            if (scope.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Each scope must be an object.");
            var scopeSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in scope.EnumerateObject())
            {
                if (property.Name is not ("Role" or "PathPrefix")) throw new InvalidDataException($"Unknown lease scope field: {property.Name}");
                if (!scopeSeen.Add(property.Name)) throw new InvalidDataException($"Duplicate lease scope field: {property.Name}");
            }
            if (!scopeSeen.SetEquals(new[] { "Role", "PathPrefix" })) throw new InvalidDataException("Lease scope requires exactly Role and PathPrefix.");
        }
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool SafeRequestId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or ':');
    private static bool SafeLeaseId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string[] DefaultNonEffects() => new[]
    {
        "no automatic upload or network access",
        "no HTTP listener/tunnel/MCP exposure",
        "no arbitrary filesystem root",
        "no application/source mutation",
        "no process launch or execution authority",
        "no Git/catalog/Agent Execute authority",
        "lease state is local ignored Workbench state only"
    };
}
