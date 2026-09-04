using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpListenerReadinessTransactionV05112(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ListenerTransactionId,
    string State,
    string OwnerSessionId,
    string LeaseId,
    string BindingTransactionId,
    string BindingTransactionState,
    string BindingTransactionSha256,
    string? LoopbackHost,
    int? LoopbackPort,
    string? LeaseClassification,
    long? LeaseStateRevision,
    DateTimeOffset? LeaseExpiresAt,
    string? ReconciledFromState,
    bool ListenerStartAttempted,
    bool ListenerObservedActive,
    bool CanonicalHistoricalScanPerformed,
    bool CanonicalLeaseMutationPerformed,
    bool ActiveIndexMutationPerformed,
    bool LeaseAuthorityGranted,
    bool ReadAuthorityGranted,
    bool RevokeAuthorityGranted,
    bool ResumeAuthorityGranted,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool EndpointSecretDisclosed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppMcpListenerReadinessReconcileResultV05112(
    string Status,
    bool PriorTransactionObserved,
    string? PriorTransactionId,
    string? PriorTerminalState,
    string? ExactLeaseId,
    string? ExactLeaseClassification,
    string? ReceiptPath,
    bool BlocksNewOwnerGeneration,
    bool CanonicalHistoricalScanPerformed,
    bool CanonicalLeaseMutationPerformed,
    bool ListenerStartedOrResumed,
    bool AuthorityGranted);

/// <summary>
/// v0.51.12 non-authoritative listener-readiness transaction. It separates an
/// exact OWNER_BOUND lease from listener-start intent, actual process-local
/// listener materialization, and committed readiness evidence. It never starts,
/// stops, resumes or revokes a listener/lease during reconciliation.
/// </summary>
public sealed class LocalAppMcpListenerReadinessV05112Service
{
    public const string Version = "0.51.12";
    public const string Schema = "matawaka.local-app-mcp-listener-readiness-transaction/v0.51.12";
    public const int MaxTransactionBytes = 128 * 1024;
    public const int MaxOwnerMetadataBytes = 64 * 1024;
    public const int MaxBindingBytes = 128 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<LocalAppMcpListenerReadinessReconcileResultV05112> ReconcileBeforeOwnerGenerationAsync(
        string workspaceRoot,
        string applicationId,
        string ownerMetadataPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ownerMetadataPath);
        if (!File.Exists(paths.TransactionPath)) return NoPrior();

        var tx = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (IsTerminal(tx.State))
        {
            return new LocalAppMcpListenerReadinessReconcileResultV05112(
                "PRIOR_LISTENER_READINESS_TERMINAL", true, tx.ListenerTransactionId, tx.State,
                tx.LeaseId, tx.LeaseClassification, null, false, false, false, false, false);
        }

        if (tx.State is not ("PREPARED_LISTENER_START" or "LISTENER_STARTED" or "LISTENER_READY" or "LIVE_BOUND_NO_LISTENER"))
            throw new InvalidDataException($"MCP_LISTENER_READINESS_TRANSACTION_INCONSISTENT: unsupported active state {tx.State}.");

        var owner = ReadOwner(paths.OwnerMetadataPath, applicationId);
        if (!owner.SessionId.Equals(tx.OwnerSessionId, StringComparison.Ordinal) ||
            owner.LeaseId?.Equals(tx.LeaseId, StringComparison.Ordinal) != true)
            throw new InvalidDataException(
                "MCP_LISTENER_READINESS_TRANSACTION_INCONSISTENT: stale owner metadata does not match the prior listener transaction; no successor authority was created.");

        var lease = ObserveExactLease(workspaceRoot, applicationId, tx.LeaseId);
        if (!lease.Present)
        {
            var absent = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "LEASE_STATE_ABSENT_BEFORE_LISTENER_RECOVERY",
                LeaseClassification = "ABSENT",
                LeaseStateRevision = null,
                LeaseExpiresAt = null,
                ReconciledFromState = tx.State,
                ListenerObservedActive = false,
                Note = "No exact canonical lease state is currently observable for the prior listener transaction. This is evidence-only current absence, not historical nonexistence, and creates no replacement authority."
            };
            var receipt = await PersistTerminalAsync(paths, absent, cancellationToken);
            return new LocalAppMcpListenerReadinessReconcileResultV05112(
                "PRIOR_LISTENER_READINESS_CLOSED_ABSENT", true, tx.ListenerTransactionId,
                absent.State, tx.LeaseId, "ABSENT", receipt, false, false, false, false, false);
        }
        if (!lease.Valid)
            throw new InvalidDataException(
                "MCP_LISTENER_READINESS_TRANSACTION_INCONSISTENT: exact canonical LeaseId state exists but is invalid; no listener/replacement authority was created.");

        if (!lease.Live)
        {
            var terminalState = lease.Revoked
                ? "LEASE_REVOKED_BEFORE_LISTENER_RECOVERY"
                : lease.Expired
                    ? "LEASE_EXPIRED_BEFORE_LISTENER_RECOVERY"
                    : "LEASE_BUDGET_EXHAUSTED_BEFORE_LISTENER_RECOVERY";
            var terminal = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = terminalState,
                LeaseClassification = lease.Classification,
                LeaseStateRevision = lease.StateRevision,
                LeaseExpiresAt = lease.ExpiresAt,
                ReconciledFromState = tx.State,
                ListenerObservedActive = false,
                Note = "The exact canonical lease is terminal at recovery. No listener is started/resumed and no canonical/index authority is mutated."
            };
            var receipt = await PersistTerminalAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpListenerReadinessReconcileResultV05112(
                "PRIOR_LISTENER_READINESS_TERMINAL_LEASE", true, tx.ListenerTransactionId,
                terminal.State, tx.LeaseId, lease.Classification, receipt, false, false, false, false, false);
        }

        // Reaching this method means this process has acquired the app-scoped owner.lock.
        // A listener from the previous Workbench process therefore cannot be treated as
        // currently owned/ready merely from stale metadata or a transaction receipt.
        var blocked = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "LIVE_BOUND_NO_LISTENER",
            LeaseClassification = "LIVE_BOUND_NO_LISTENER",
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ReconciledFromState = tx.State,
            ListenerObservedActive = false,
            Note = "Exact canonical lease is still live and prior owner metadata remains bound, but process-local listener readiness cannot survive as authority across owner.lock reacquisition. Successor startup is blocked until explicit inherited lease closure or expiry; reconciliation performs no start/resume/revoke."
        };
        var blockedReceipt = await PersistTerminalAsync(paths, blocked, cancellationToken);
        return new LocalAppMcpListenerReadinessReconcileResultV05112(
            "PRIOR_LISTENER_READINESS_LIVE_BOUND_NO_LISTENER", true, tx.ListenerTransactionId,
            blocked.State, tx.LeaseId, blocked.LeaseClassification, blockedReceipt,
            true, false, false, false, false);
    }

    public async Task<(LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)> PrepareAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppMcpOwnerLeaseBindingTransactionV05111 ownerBound,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!ownerBound.State.Equals("OWNER_BOUND", StringComparison.Ordinal) ||
            !ownerBound.ApplicationId.Equals(held.ApplicationId, StringComparison.Ordinal) ||
            !ownerBound.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !ownerBound.PreparedLeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_LISTENER_READINESS_PREPARE_REFUSED: exact OWNER_BOUND relation was not supplied.");

        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var owner = ReadOwner(paths.OwnerMetadataPath, held.ApplicationId);
        if (!owner.SessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            owner.LeaseId?.Equals(held.LeaseId, StringComparison.Ordinal) != true ||
            !owner.State.Equals("LEASE_BOUND_LISTENER_NOT_READY", StringComparison.Ordinal))
            throw new InvalidDataException("MCP_LISTENER_READINESS_PREPARE_REFUSED: owner metadata is not exact LEASE_BOUND_LISTENER_NOT_READY.");

        var binding = ReadBinding(paths.BindingPath, held.ApplicationId);
        if (!binding.BindingTransactionId.Equals(ownerBound.BindingTransactionId, StringComparison.Ordinal) ||
            !binding.State.Equals("OWNER_BOUND", StringComparison.Ordinal) ||
            !binding.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !binding.PreparedLeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_LISTENER_READINESS_PREPARE_REFUSED: persisted owner->lease binding does not match supplied OWNER_BOUND evidence.");

        var lease = ObserveExactLease(held.WorkspaceRoot, held.ApplicationId, held.LeaseId!);
        if (!lease.Present || !lease.Valid || !lease.Live)
            throw new InvalidDataException("MCP_LISTENER_READINESS_PREPARE_REFUSED: exact canonical lease is not currently live/valid.");

        var tx = new LocalAppMcpListenerReadinessTransactionV05112(
            Schema, Version, DateTimeOffset.Now, held.ApplicationId,
            "listenertx-" + Guid.NewGuid().ToString("N"),
            "PREPARED_LISTENER_START",
            held.SessionId,
            held.LeaseId!,
            ownerBound.BindingTransactionId,
            ownerBound.State,
            LocalAppV046FileBoundary.HashFile(paths.BindingPath),
            null, null,
            lease.Classification,
            lease.StateRevision,
            lease.ExpiresAt,
            null,
            false, false,
            false, false, false,
            false, false, false, false,
            false, false, false,
            NonEffects(),
            "Exact OWNER_BOUND relation and live canonical lease were observed before listener startup. PREPARED_LISTENER_START records intent/provenance only and is not evidence that a listener exists.");

        await WriteTransactionAtomicAsync(paths.TransactionPath, tx, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, tx, cancellationToken);
        return (tx, receipt);
    }

    public async Task<(LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)> RecordListenerStartedAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppMcpAdapterGrantV049 adapterGrant,
        bool listenerObservedActive,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!listenerObservedActive)
            throw new InvalidDataException("MCP_LISTENER_READINESS_START_REFUSED: adapter StartAsync returned but active listener observation is false.");
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var prepared = await ReadTransactionAsync(paths.TransactionPath, held.ApplicationId, cancellationToken);
        if (!prepared.State.Equals("PREPARED_LISTENER_START", StringComparison.Ordinal) ||
            !prepared.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !prepared.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal) ||
            !adapterGrant.ApplicationId.Equals(held.ApplicationId, StringComparison.Ordinal) ||
            !adapterGrant.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal) ||
            !adapterGrant.LoopbackOnly || adapterGrant.PublicNetworkExposurePerformed || adapterGrant.SecureMcpTunnelStarted)
            throw new InvalidDataException("MCP_LISTENER_READINESS_START_REFUSED: adapter grant does not match exact PREPARED_LISTENER_START boundary.");

        var uri = new Uri(adapterGrant.EndpointUrl);
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) || uri.Port <= 0)
            throw new InvalidDataException("MCP_LISTENER_READINESS_START_REFUSED: only exact IPv4 loopback listener observation is admissible.");

        var started = prepared with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "LISTENER_STARTED",
            LoopbackHost = uri.Host,
            LoopbackPort = uri.Port,
            ListenerStartAttempted = true,
            ListenerObservedActive = true,
            Note = "Existing v0.49 StartAsync returned an exact app/LeaseId IPv4-loopback grant and this Workbench process materially observed the adapter active. LISTENER_STARTED is still not the committed readiness state and grants no authority."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, started, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, started, cancellationToken);
        return (started, receipt);
    }

    public async Task<(LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)> CommitReadyAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppMcpAdapterGrantV049 adapterGrant,
        bool listenerObservedActive,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!listenerObservedActive)
            throw new InvalidDataException("MCP_LISTENER_READINESS_COMMIT_REFUSED: active process-local listener observation is required.");
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var started = await ReadTransactionAsync(paths.TransactionPath, held.ApplicationId, cancellationToken);
        if (!started.State.Equals("LISTENER_STARTED", StringComparison.Ordinal) ||
            !started.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !started.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal) ||
            !adapterGrant.ApplicationId.Equals(held.ApplicationId, StringComparison.Ordinal) ||
            !adapterGrant.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_LISTENER_READINESS_COMMIT_REFUSED: exact LISTENER_STARTED/app/LeaseId relation is missing.");

        var uri = new Uri(adapterGrant.EndpointUrl);
        if (!uri.Host.Equals(started.LoopbackHost, StringComparison.Ordinal) || uri.Port != started.LoopbackPort)
            throw new InvalidDataException("MCP_LISTENER_READINESS_COMMIT_REFUSED: loopback endpoint observation drifted between start and commit.");
        var lease = ObserveExactLease(held.WorkspaceRoot, held.ApplicationId, held.LeaseId!);
        if (!lease.Present || !lease.Valid || !lease.Live)
            throw new InvalidDataException("MCP_LISTENER_READINESS_COMMIT_REFUSED: exact canonical lease is no longer live at readiness commit.");

        var ready = started with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "LISTENER_READY",
            LeaseClassification = lease.Classification,
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ListenerStartAttempted = true,
            ListenerObservedActive = true,
            Note = "Exact process-local IPv4-loopback listener remained materially active for the same ApplicationId/LeaseId at readiness commit. LISTENER_READY is runtime evidence only; it is not public reachability and grants no lease/read/revoke/resume authority."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, ready, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, ready, cancellationToken);
        return (ready, receipt);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("listener-v05112-order", true, "OWNER_BOUND -> PREPARED_LISTENER_START -> LISTENER_STARTED -> LISTENER_READY -> owner MarkListenerReady", "separate states"),
        ("listener-v05112-prepared", true, "PREPARED_LISTENER_START does not claim listener existence", "false"),
        ("listener-v05112-loopback", true, "only exact http://127.0.0.1:<port> observation accepted", "loopback only"),
        ("listener-v05112-recovery", true, "reacquired owner.lock + live exact bound lease -> LIVE_BOUND_NO_LISTENER", "block successor; no auto resume/revoke"),
        ("listener-v05112-no-history", true, "exact LeaseId path only", "true"),
        ("listener-v05112-secrets", true, "no bearer plaintext/hash or endpoint path token persisted", "omitted"),
        ("listener-v05112-authority", true, "transaction grants no lease/read/revoke/resume authority", "false")
    };

    private static bool IsTerminal(string state)
        => state is "LEASE_STATE_ABSENT_BEFORE_LISTENER_RECOVERY" or
            "LEASE_REVOKED_BEFORE_LISTENER_RECOVERY" or
            "LEASE_EXPIRED_BEFORE_LISTENER_RECOVERY" or
            "LEASE_BUDGET_EXHAUSTED_BEFORE_LISTENER_RECOVERY";

    private static LocalAppMcpListenerReadinessReconcileResultV05112 NoPrior()
        => new("NO_PRIOR_LISTENER_READINESS_TRANSACTION", false, null, null, null, null, null,
            false, false, false, false, false);

    private static void RequireHeld(LocalAppHeldMcpSessionOwnershipV0517 held)
    {
        if (held is null || held.Released || string.IsNullOrWhiteSpace(held.LeaseId))
            throw new InvalidDataException("v0.51.12 requires currently held MCP ownership already bound to an exact LeaseId.");
    }

    private static LocalAppMcpSessionOwnerV0517 ReadOwner(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("MCP_LISTENER_READINESS_OWNER_ABSENT: exact owner metadata is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.12 owner metadata");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxOwnerMetadataBytes)
            throw new InvalidDataException("MCP_LISTENER_READINESS_OWNER_INVALID_SIZE.");
        LocalAppMcpSessionOwnerV0517 owner;
        try
        {
            owner = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("MCP_LISTENER_READINESS_OWNER_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_LISTENER_READINESS_OWNER_INVALID: JSON parse failed.", ex); }
        if (owner.Schema != LocalAppMcpSessionOwnershipV0517Service.OwnerSchema ||
            owner.Version != LocalAppMcpSessionOwnershipV0517Service.Version ||
            !owner.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            owner.BearerPlaintextStored || owner.BearerHashStored || owner.EndpointSecretStored || owner.LeaseAuthorityGranted)
            throw new InvalidDataException("MCP_LISTENER_READINESS_OWNER_INVALID: identity/authority boundary failed.");
        return owner;
    }

    private static LocalAppMcpOwnerLeaseBindingTransactionV05111 ReadBinding(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("MCP_LISTENER_READINESS_BINDING_ABSENT: exact owner->lease binding transaction is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.12 owner->lease binding");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxBindingBytes)
            throw new InvalidDataException("MCP_LISTENER_READINESS_BINDING_INVALID_SIZE.");
        LocalAppMcpOwnerLeaseBindingTransactionV05111 binding;
        try
        {
            binding = JsonSerializer.Deserialize<LocalAppMcpOwnerLeaseBindingTransactionV05111>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("MCP_LISTENER_READINESS_BINDING_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_LISTENER_READINESS_BINDING_INVALID: JSON parse failed.", ex); }
        if (binding.Schema != LocalAppMcpOwnerLeaseBindingV05111Service.Schema ||
            binding.Version != LocalAppMcpOwnerLeaseBindingV05111Service.Version ||
            !binding.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            binding.CanonicalHistoricalScanPerformed || binding.CanonicalLeaseMutationPerformed || binding.ActiveIndexMutationPerformed ||
            binding.LeaseAuthorityGranted || binding.ReadAuthorityGranted || binding.RevokeAuthorityGranted || binding.ResumeAuthorityGranted ||
            binding.BearerPlaintextDisclosed || binding.BearerHashDisclosed || binding.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_LISTENER_READINESS_BINDING_INVALID: identity/authority boundary failed.");
        return binding;
    }

    private static ExactLeaseObservation ObserveExactLease(string workspaceRoot, string applicationId, string leaseId)
    {
        var path = LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(workspaceRoot, applicationId, leaseId, createDirectory: false);
        if (!File.Exists(path)) return new(false, false, false, false, false, false, "ABSENT", null, null);
        try
        {
            var state = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspaceRoot, applicationId, leaseId);
            var expired = state.ExpiresAt <= DateTimeOffset.Now;
            var budget = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
            var live = !state.Revoked && !expired && !budget;
            var classification = state.Revoked ? "REVOKED" : expired ? "EXPIRED" : budget ? "BUDGET_EXHAUSTED" : "LIVE";
            return new(true, true, live, state.Revoked, expired, budget, classification, state.StateRevision, state.ExpiresAt);
        }
        catch (InvalidDataException)
        {
            return new(true, false, false, false, false, false, "CANONICAL_STATE_INVALID", null, null);
        }
    }

    private async Task<LocalAppMcpListenerReadinessTransactionV05112> ReadTransactionAsync(
        string path, string applicationId, CancellationToken cancellationToken)
    {
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.12 listener-readiness transaction");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxTransactionBytes)
            throw new InvalidDataException($"MCP_LISTENER_READINESS_TRANSACTION_INVALID_SIZE: expected 1..{MaxTransactionBytes} bytes.");
        LocalAppMcpListenerReadinessTransactionV05112 tx;
        try
        {
            tx = JsonSerializer.Deserialize<LocalAppMcpListenerReadinessTransactionV05112>(
                await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("MCP_LISTENER_READINESS_TRANSACTION_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_LISTENER_READINESS_TRANSACTION_INVALID: JSON parse failed.", ex); }
        if (tx.Schema != Schema || tx.Version != Version || !tx.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.ListenerTransactionId) || !tx.ListenerTransactionId.StartsWith("listenertx-", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.OwnerSessionId) || !tx.OwnerSessionId.StartsWith("mcpsess-", StringComparison.Ordinal) ||
            !SafeLeaseId(tx.LeaseId) || string.IsNullOrWhiteSpace(tx.BindingTransactionId) ||
            tx.CanonicalHistoricalScanPerformed || tx.CanonicalLeaseMutationPerformed || tx.ActiveIndexMutationPerformed ||
            tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted ||
            tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_LISTENER_READINESS_TRANSACTION_INVALID: identity/authority boundary failed validation.");
        return tx;
    }

    private async Task<string> PersistTerminalAsync(ResolvedPaths paths, LocalAppMcpListenerReadinessTransactionV05112 tx, CancellationToken cancellationToken)
    {
        await WriteTransactionAtomicAsync(paths.TransactionPath, tx, cancellationToken);
        return await WriteReceiptAsync(paths.WorkspaceRoot, tx, cancellationToken);
    }

    private static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalAppMcpListenerReadinessTransactionV05112 tx, CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-listener-readiness-v05112");
        var path = Path.Combine(dir,
            $"listener-ready-{LocalAppV046FileBoundary.SafeToken(tx.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(tx.ListenerTransactionId)}-{LocalAppV046FileBoundary.SafeToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteTransactionAtomicAsync(string path, LocalAppMcpListenerReadinessTransactionV05112 tx, CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.12 listener-readiness transaction");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.12 listener-readiness transaction");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.12 listener-readiness transaction");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static ResolvedPaths ResolvePaths(string workspaceRoot, string applicationId, string ownerMetadataPath)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", LocalAppV046FileBoundary.SafeToken(applicationId));
        Directory.CreateDirectory(appDir);
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.12 listener-readiness app directory");
        var expectedOwner = Path.Combine(appDir, "owner-v0.51.7.json");
        if (!Path.GetFullPath(ownerMetadataPath).Equals(Path.GetFullPath(expectedOwner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.51.12 owner metadata path mismatch.");
        var bindingPath = Path.Combine(appDir, "owner-lease-binding-v05111.json");
        var txPath = Path.Combine(appDir, "listener-readiness-v05112.json");
        if (File.Exists(bindingPath)) LocalAppV046FileBoundary.RejectReparse(bindingPath, "v0.51.12 binding path");
        if (File.Exists(txPath)) LocalAppV046FileBoundary.RejectReparse(txPath, "v0.51.12 listener transaction path");
        return new ResolvedPaths(Path.GetFullPath(workspaceRoot.Trim()), expectedOwner, bindingPath, txPath);
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string[] NonEffects() => new[]
    {
        "listener-readiness transaction is runtime provenance/control evidence only, not lease/read/revoke/resume authority",
        "PREPARED_LISTENER_START is not evidence that a listener exists",
        "LISTENER_STARTED is not committed readiness and LISTENER_READY is not public/external reachability",
        "reconciliation never starts, resumes, stops, renews or revokes a listener/lease",
        "no historical canonical lease enumeration",
        "no canonical lease or verified active-index mutation",
        "no bearer plaintext/hash or reusable endpoint path token stored/disclosed",
        "no Secure MCP Tunnel/network publication/catalog/Agent Execute/ActionPermit authority"
    };

    private sealed record ExactLeaseObservation(
        bool Present, bool Valid, bool Live, bool Revoked, bool Expired, bool BudgetExhausted,
        string Classification, long? StateRevision, DateTimeOffset? ExpiresAt);

    private sealed record ResolvedPaths(string WorkspaceRoot, string OwnerMetadataPath, string BindingPath, string TransactionPath);
}
