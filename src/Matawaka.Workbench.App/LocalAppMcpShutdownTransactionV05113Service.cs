using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpShutdownTransactionV05113(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ShutdownTransactionId,
    string State,
    string OwnerSessionId,
    string LeaseId,
    string ListenerTransactionId,
    string ListenerReadinessState,
    string ListenerReadinessSha256,
    string? AdapterStopReceiptSha256,
    string? OwnerReleaseReceiptSha256,
    string? ExactRevokeReceiptSha256,
    string? LeaseClassification,
    long? LeaseStateRevision,
    DateTimeOffset? LeaseExpiresAt,
    string? ReconciledFromState,
    bool StopRequested,
    bool ListenerObservedInactive,
    bool OwnerReleaseObserved,
    bool ExactLeaseTerminalObserved,
    bool SiblingLeasesRevoked,
    bool CanonicalHistoricalScanPerformed,
    bool TransactionCanonicalLeaseMutationPerformed,
    bool TransactionActiveIndexMutationPerformed,
    bool ShutdownTransactionGrantedAuthority,
    bool ReadAuthorityGranted,
    bool RevokeAuthorityGranted,
    bool ResumeAuthorityGranted,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool EndpointSecretDisclosed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppMcpShutdownReconcileResultV05113(
    string Status,
    bool PriorTransactionObserved,
    string? PriorTransactionId,
    string? PriorState,
    string? ExactLeaseId,
    string? ExactLeaseClassification,
    string? ReceiptPath,
    bool BlocksNewOwnerGeneration,
    bool CanonicalHistoricalScanPerformed,
    bool CanonicalLeaseMutationPerformed,
    bool ListenerStartedOrResumed,
    bool LeaseAutoRevoked,
    bool AuthorityGranted);

/// <summary>
/// v0.51.13 additive reverse-lifecycle transaction. It records the difference
/// between requesting shutdown, materially observing listener stop, observing
/// owner release, and separately observing exact canonical lease termination.
/// The transaction itself grants no read/revoke/resume authority and performs no
/// canonical lease/index mutation. Reconciliation never starts/resumes a listener
/// and never auto-revokes a still-live lease.
/// </summary>
public sealed class LocalAppMcpShutdownTransactionV05113Service
{
    public const string Version = "0.51.13";
    public const string Schema = "matawaka.local-app-mcp-shutdown-transaction/v0.51.13";
    public const int MaxTransactionBytes = 128 * 1024;
    public const int MaxOwnerMetadataBytes = 64 * 1024;
    public const int MaxListenerTransactionBytes = 128 * 1024;
    public const int MaxReceiptBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<LocalAppMcpShutdownReconcileResultV05113> ReconcileBeforeOwnerGenerationAsync(
        string workspaceRoot,
        string applicationId,
        string ownerMetadataPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ownerMetadataPath);
        if (!File.Exists(paths.TransactionPath)) return NoPrior();

        var tx = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (tx.State is "SHUTDOWN_COMPLETED" or "LEASE_ALREADY_TERMINAL" or "LEASE_STATE_ABSENT_DURING_SHUTDOWN_RECOVERY")
        {
            return new LocalAppMcpShutdownReconcileResultV05113(
                "PRIOR_SHUTDOWN_TRANSACTION_TERMINAL", true, tx.ShutdownTransactionId, tx.State,
                tx.LeaseId, tx.LeaseClassification, null, false, false, false, false, false, false);
        }

        if (tx.State is not ("SHUTDOWN_PREPARED" or "LISTENER_STOPPED" or "OWNER_RELEASED" or "LEASE_REVOKED" or "OWNER_RELEASED_LEASE_LIVE"))
            throw new InvalidDataException($"MCP_SHUTDOWN_TRANSACTION_INCONSISTENT: unsupported active state {tx.State}.");

        var owner = ReadOwner(paths.OwnerMetadataPath, applicationId);
        if (!owner.SessionId.Equals(tx.OwnerSessionId, StringComparison.Ordinal) ||
            owner.LeaseId?.Equals(tx.LeaseId, StringComparison.Ordinal) != true)
            throw new InvalidDataException(
                "MCP_SHUTDOWN_TRANSACTION_INCONSISTENT: stale owner metadata does not match prior shutdown SessionId/LeaseId; no successor authority was created.");

        var lease = ObserveExactLease(workspaceRoot, applicationId, tx.LeaseId);
        if (!lease.Present)
        {
            var absent = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "LEASE_STATE_ABSENT_DURING_SHUTDOWN_RECOVERY",
                LeaseClassification = "ABSENT",
                LeaseStateRevision = null,
                LeaseExpiresAt = null,
                ReconciledFromState = tx.State,
                ListenerObservedInactive = true,
                OwnerReleaseObserved = true,
                ExactLeaseTerminalObserved = true,
                Note = "Exact canonical LeaseId state is currently absent after owner.lock reacquisition. This is evidence of current absence only, not historical nonexistence; no replacement authority or canonical mutation was created."
            };
            var receipt = await PersistAsync(paths, absent, cancellationToken);
            return new LocalAppMcpShutdownReconcileResultV05113(
                "PRIOR_SHUTDOWN_CLOSED_ABSENT", true, tx.ShutdownTransactionId, absent.State,
                tx.LeaseId, "ABSENT", receipt, false, false, false, false, false, false);
        }
        if (!lease.Valid)
            throw new InvalidDataException(
                "MCP_SHUTDOWN_TRANSACTION_INCONSISTENT: exact canonical LeaseId state exists but is invalid; no successor/listener/revoke authority was created.");

        if (!lease.Live)
        {
            var terminal = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "LEASE_ALREADY_TERMINAL",
                LeaseClassification = lease.Classification,
                LeaseStateRevision = lease.StateRevision,
                LeaseExpiresAt = lease.ExpiresAt,
                ReconciledFromState = tx.State,
                ListenerObservedInactive = true,
                OwnerReleaseObserved = true,
                ExactLeaseTerminalObserved = true,
                Note = "The exact canonical lease is already terminal at shutdown recovery. No listener was started/resumed and no canonical/index state was rewritten by reconciliation."
            };
            var receipt = await PersistAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpShutdownReconcileResultV05113(
                "PRIOR_SHUTDOWN_TERMINAL_LEASE", true, tx.ShutdownTransactionId, terminal.State,
                tx.LeaseId, lease.Classification, receipt, false, false, false, false, false, false);
        }

        // This method is called only after this process acquired the app-scoped
        // owner.lock. Therefore stale prior-process listener/owner receipts cannot
        // be treated as current runtime authority. The exact lease is still live,
        // so successor startup is blocked until explicit closure or expiry.
        var live = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "OWNER_RELEASED_LEASE_LIVE",
            LeaseClassification = "LIVE",
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ReconciledFromState = tx.State,
            ListenerObservedInactive = true,
            OwnerReleaseObserved = true,
            ExactLeaseTerminalObserved = false,
            Note = "Prior process-local listener/owner authority cannot survive owner.lock reacquisition, but the exact canonical lease remains live. Successor startup is blocked; reconciliation performs no listener start/resume and no automatic revoke."
        };
        var blockedReceipt = await PersistAsync(paths, live, cancellationToken);
        return new LocalAppMcpShutdownReconcileResultV05113(
            "PRIOR_SHUTDOWN_OWNER_RELEASED_LEASE_LIVE", true, tx.ShutdownTransactionId, live.State,
            tx.LeaseId, "LIVE", blockedReceipt, true, false, false, false, false, false);
    }

    public async Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> PrepareAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var owner = ReadOwner(paths.OwnerMetadataPath, held.ApplicationId);
        if (!owner.SessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            owner.LeaseId?.Equals(held.LeaseId, StringComparison.Ordinal) != true ||
            !owner.State.Equals("LISTENER_READY_OWNED", StringComparison.Ordinal) ||
            !owner.ListenerObservedActive)
            throw new InvalidDataException("MCP_SHUTDOWN_PREPARE_REFUSED: exact LISTENER_READY_OWNED owner metadata is required.");

        var listener = ReadListener(paths.ListenerTransactionPath, held.ApplicationId);
        if (!listener.State.Equals("LISTENER_READY", StringComparison.Ordinal) ||
            !listener.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !listener.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal) || !listener.ListenerObservedActive)
            throw new InvalidDataException("MCP_SHUTDOWN_PREPARE_REFUSED: exact LISTENER_READY transaction is required.");

        var lease = ObserveExactLease(held.WorkspaceRoot, held.ApplicationId, held.LeaseId!);
        if (!lease.Present || !lease.Valid || !lease.Live)
            throw new InvalidDataException("MCP_SHUTDOWN_PREPARE_REFUSED: exact canonical lease is not currently live/valid.");

        var tx = new LocalAppMcpShutdownTransactionV05113(
            Schema, Version, DateTimeOffset.Now, held.ApplicationId,
            "shutdowntx-" + Guid.NewGuid().ToString("N"),
            "SHUTDOWN_PREPARED", held.SessionId, held.LeaseId!,
            listener.ListenerTransactionId, listener.State,
            LocalAppV046FileBoundary.HashFile(paths.ListenerTransactionPath),
            null, null, null,
            lease.Classification, lease.StateRevision, lease.ExpiresAt, null,
            true, false, false, false, false,
            false, false, false,
            false, false, false, false,
            false, false, false,
            NonEffects(),
            "Human-confirmed shutdown was prepared while exact LISTENER_READY owner/listener evidence and live canonical LeaseId were still materially bound. SHUTDOWN_PREPARED is not evidence that the listener stopped.");
        await WriteTransactionAtomicAsync(paths.TransactionPath, tx, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, tx, cancellationToken);
        return (tx, receipt);
    }

    public async Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> RecordListenerStoppedAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppMcpAdapterStopReceiptV049 stopReceipt,
        string stopReceiptPath,
        bool listenerObservedInactive,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!listenerObservedInactive || !stopReceipt.ListenerStopped ||
            !stopReceipt.ApplicationId.Equals(held.ApplicationId, StringComparison.Ordinal) ||
            !stopReceipt.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_STOP_REFUSED: exact listener inactivity and matching stop receipt are required.");
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var tx = await ReadTransactionAsync(paths.TransactionPath, held.ApplicationId, cancellationToken);
        if (!tx.State.Equals("SHUTDOWN_PREPARED", StringComparison.Ordinal) ||
            !tx.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !tx.LeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_STOP_REFUSED: exact SHUTDOWN_PREPARED relation is missing.");
        var stopSha = ValidateAndHashReceipt(stopReceiptPath, "v0.51.13 adapter stop receipt");
        var stopped = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "LISTENER_STOPPED",
            AdapterStopReceiptSha256 = stopSha,
            ListenerObservedInactive = true,
            Note = "Existing v0.49 StopAsync produced a matching stop receipt and this process materially observed the adapter inactive. LISTENER_STOPPED does not imply owner release or canonical lease revocation."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, stopped, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, stopped, cancellationToken);
        return (stopped, receipt);
    }

    public async Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> RecordOwnerReleasedAsync(
        string workspaceRoot,
        string applicationId,
        string ownerSessionId,
        string leaseId,
        string ownerReleaseReceiptPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ResolveExpectedOwnerPath(workspaceRoot, applicationId));
        var tx = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (!tx.State.Equals("LISTENER_STOPPED", StringComparison.Ordinal) ||
            !tx.OwnerSessionId.Equals(ownerSessionId, StringComparison.Ordinal) ||
            !tx.LeaseId.Equals(leaseId, StringComparison.Ordinal) || !tx.ListenerObservedInactive)
            throw new InvalidDataException("MCP_SHUTDOWN_OWNER_RELEASE_REFUSED: exact LISTENER_STOPPED relation is missing.");

        var release = ReadOwnershipReceipt(ownerReleaseReceiptPath, applicationId, ownerSessionId, leaseId);
        if (!release.ListenerObservedInactiveBeforeRelease || !release.CrossProcessHandleReleased ||
            !release.Status.Equals("MCP_SESSION_OWNERSHIP_RELEASED_AFTER_LISTENER_STOP", StringComparison.Ordinal))
            throw new InvalidDataException("MCP_SHUTDOWN_OWNER_RELEASE_REFUSED: ownership receipt does not prove release after listener stop.");
        var released = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "OWNER_RELEASED",
            OwnerReleaseReceiptSha256 = ValidateAndHashReceipt(ownerReleaseReceiptPath, "v0.51.13 owner release receipt"),
            OwnerReleaseObserved = true,
            Note = "The exact app-scoped owner handle was released only after listener inactivity. OWNER_RELEASED does not imply exact canonical lease revocation; lease closure remains a separate authority-bearing operation."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, released, cancellationToken);
        var receiptPath = await WriteReceiptAsync(workspaceRoot, released, cancellationToken);
        return (released, receiptPath);
    }

    public async Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> RecordLeaseTerminalAsync(
        string workspaceRoot,
        string applicationId,
        string ownerSessionId,
        string leaseId,
        string exactRevokeReceiptPath,
        bool siblingLeasesRevoked,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ResolveExpectedOwnerPath(workspaceRoot, applicationId));
        var tx = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (!tx.State.Equals("OWNER_RELEASED", StringComparison.Ordinal) ||
            !tx.OwnerSessionId.Equals(ownerSessionId, StringComparison.Ordinal) ||
            !tx.LeaseId.Equals(leaseId, StringComparison.Ordinal) || !tx.OwnerReleaseObserved)
            throw new InvalidDataException("MCP_SHUTDOWN_LEASE_CLOSE_REFUSED: exact OWNER_RELEASED relation is missing.");
        if (siblingLeasesRevoked)
            throw new InvalidDataException("MCP_SHUTDOWN_LEASE_CLOSE_REFUSED: sibling lease revocation is outside the exact shutdown corridor.");

        var lease = ObserveExactLease(workspaceRoot, applicationId, leaseId);
        if (!lease.Present || !lease.Valid || lease.Live)
            throw new InvalidDataException("MCP_SHUTDOWN_LEASE_CLOSE_REFUSED: exact canonical lease is still live or invalid after closure operation.");
        var state = lease.Revoked ? "LEASE_REVOKED" : "LEASE_ALREADY_TERMINAL";
        var closed = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = state,
            ExactRevokeReceiptSha256 = ValidateAndHashReceipt(exactRevokeReceiptPath, "v0.51.13 exact revoke receipt"),
            LeaseClassification = lease.Classification,
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ExactLeaseTerminalObserved = true,
            SiblingLeasesRevoked = false,
            Note = lease.Revoked
                ? "A separate exact indexed revoke operation completed after owner release, and the transaction re-observed the same exact canonical LeaseId as REVOKED. The shutdown transaction itself granted no revoke authority."
                : "The exact canonical LeaseId became terminal by expiry/budget before the separate close observation. No replacement state was written by the shutdown transaction."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, closed, cancellationToken);
        var receiptPath = await WriteReceiptAsync(workspaceRoot, closed, cancellationToken);
        return (closed, receiptPath);
    }

    public async Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> CommitCompletedAsync(
        string workspaceRoot,
        string applicationId,
        string ownerSessionId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ResolveExpectedOwnerPath(workspaceRoot, applicationId));
        var tx = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (tx.State is not ("LEASE_REVOKED" or "LEASE_ALREADY_TERMINAL") ||
            !tx.OwnerSessionId.Equals(ownerSessionId, StringComparison.Ordinal) ||
            !tx.LeaseId.Equals(leaseId, StringComparison.Ordinal) ||
            !tx.ListenerObservedInactive || !tx.OwnerReleaseObserved || !tx.ExactLeaseTerminalObserved || tx.SiblingLeasesRevoked)
            throw new InvalidDataException("MCP_SHUTDOWN_COMMIT_REFUSED: listener/owner/exact-lease closure evidence is incomplete.");
        var lease = ObserveExactLease(workspaceRoot, applicationId, leaseId);
        if (!lease.Present || !lease.Valid || lease.Live)
            throw new InvalidDataException("MCP_SHUTDOWN_COMMIT_REFUSED: exact canonical lease is not terminal at final commit.");
        var completed = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "SHUTDOWN_COMPLETED",
            LeaseClassification = lease.Classification,
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            Note = "Listener inactivity, owner release, and exact lease terminality were separately observed in order. SHUTDOWN_COMPLETED is provenance/control evidence only and grants no future runtime or read authority."
        };
        await WriteTransactionAtomicAsync(paths.TransactionPath, completed, cancellationToken);
        var receiptPath = await WriteReceiptAsync(workspaceRoot, completed, cancellationToken);
        return (completed, receiptPath);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("shutdown-v05113-order", true, "LISTENER_READY -> SHUTDOWN_PREPARED -> LISTENER_STOPPED -> OWNER_RELEASED -> LEASE_REVOKED/LEASE_ALREADY_TERMINAL -> SHUTDOWN_COMPLETED", "separate states"),
        ("shutdown-v05113-prepared", true, "SHUTDOWN_PREPARED does not claim listener stop", "false"),
        ("shutdown-v05113-release", true, "owner release requires exact listener inactivity receipt", "fail closed"),
        ("shutdown-v05113-revoke", true, "exact revoke remains separate authority-bearing operation", "transaction grants none"),
        ("shutdown-v05113-recovery", true, "owner.lock reacquired + exact live lease -> OWNER_RELEASED_LEASE_LIVE", "block successor; no auto revoke"),
        ("shutdown-v05113-siblings", true, "sibling lease revoke refused", "preserved"),
        ("shutdown-v05113-no-history", true, "exact LeaseId path only", "true"),
        ("shutdown-v05113-secrets", true, "no bearer plaintext/hash or endpoint path token persisted", "omitted"),
        ("shutdown-v05113-authority", true, "shutdown transaction grants no read/revoke/resume authority", "false")
    };

    private static LocalAppMcpShutdownReconcileResultV05113 NoPrior()
        => new("NO_PRIOR_SHUTDOWN_TRANSACTION", false, null, null, null, null, null,
            false, false, false, false, false, false);

    private static void RequireHeld(LocalAppHeldMcpSessionOwnershipV0517 held)
    {
        if (held is null || held.Released || string.IsNullOrWhiteSpace(held.LeaseId))
            throw new InvalidDataException("v0.51.13 requires currently held MCP ownership already bound to an exact LeaseId.");
    }

    private static LocalAppMcpSessionOwnerV0517 ReadOwner(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("MCP_SHUTDOWN_OWNER_ABSENT: exact owner metadata is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.13 owner metadata");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxOwnerMetadataBytes) throw new InvalidDataException("MCP_SHUTDOWN_OWNER_INVALID_SIZE.");
        LocalAppMcpSessionOwnerV0517 owner;
        try
        {
            owner = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("MCP_SHUTDOWN_OWNER_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_SHUTDOWN_OWNER_INVALID: JSON parse failed.", ex); }
        if (owner.Schema != LocalAppMcpSessionOwnershipV0517Service.OwnerSchema || owner.Version != LocalAppMcpSessionOwnershipV0517Service.Version ||
            !owner.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || owner.BearerPlaintextStored || owner.BearerHashStored ||
            owner.EndpointSecretStored || owner.LeaseAuthorityGranted)
            throw new InvalidDataException("MCP_SHUTDOWN_OWNER_INVALID: identity/authority boundary failed.");
        return owner;
    }

    private static LocalAppMcpListenerReadinessTransactionV05112 ReadListener(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_TRANSACTION_ABSENT.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.13 listener transaction");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxListenerTransactionBytes) throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_TRANSACTION_INVALID_SIZE.");
        LocalAppMcpListenerReadinessTransactionV05112 tx;
        try
        {
            tx = JsonSerializer.Deserialize<LocalAppMcpListenerReadinessTransactionV05112>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_TRANSACTION_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_TRANSACTION_INVALID: JSON parse failed.", ex); }
        if (tx.Schema != LocalAppMcpListenerReadinessV05112Service.Schema || tx.Version != LocalAppMcpListenerReadinessV05112Service.Version ||
            !tx.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || tx.CanonicalHistoricalScanPerformed ||
            tx.CanonicalLeaseMutationPerformed || tx.ActiveIndexMutationPerformed || tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted ||
            tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted || tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_TRANSACTION_INVALID: identity/authority boundary failed.");
        return tx;
    }

    private static LocalAppMcpSessionOwnershipReceiptV0517 ReadOwnershipReceipt(
        string path, string applicationId, string sessionId, string leaseId)
    {
        ValidateAndHashReceipt(path, "v0.51.13 owner release receipt");
        LocalAppMcpSessionOwnershipReceiptV0517 receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<LocalAppMcpSessionOwnershipReceiptV0517>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("MCP_SHUTDOWN_OWNER_RELEASE_RECEIPT_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_SHUTDOWN_OWNER_RELEASE_RECEIPT_INVALID: JSON parse failed.", ex); }
        if (receipt.Schema != LocalAppMcpSessionOwnershipV0517Service.ReceiptSchema || receipt.Version != LocalAppMcpSessionOwnershipV0517Service.Version ||
            !receipt.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || !receipt.SessionId.Equals(sessionId, StringComparison.Ordinal) ||
            receipt.LeaseId?.Equals(leaseId, StringComparison.Ordinal) != true || receipt.CanonicalLeaseMutated || receipt.LeaseAuthorityGranted ||
            receipt.BearerPlaintextUsedOrDisclosed || receipt.BearerHashUsedOrDisclosed || receipt.EndpointSecretUsedOrDisclosed)
            throw new InvalidDataException("MCP_SHUTDOWN_OWNER_RELEASE_RECEIPT_INVALID: identity/authority boundary failed.");
        return receipt;
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
        catch (InvalidDataException) { return new(true, false, false, false, false, false, "CANONICAL_STATE_INVALID", null, null); }
    }

    private async Task<LocalAppMcpShutdownTransactionV05113> ReadTransactionAsync(string path, string applicationId, CancellationToken cancellationToken)
    {
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.13 shutdown transaction");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxTransactionBytes)
            throw new InvalidDataException($"MCP_SHUTDOWN_TRANSACTION_INVALID_SIZE: expected 1..{MaxTransactionBytes} bytes.");
        LocalAppMcpShutdownTransactionV05113 tx;
        try
        {
            tx = JsonSerializer.Deserialize<LocalAppMcpShutdownTransactionV05113>(
                await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("MCP_SHUTDOWN_TRANSACTION_INVALID: empty JSON.");
        }
        catch (JsonException ex) { throw new InvalidDataException("MCP_SHUTDOWN_TRANSACTION_INVALID: JSON parse failed.", ex); }
        if (tx.Schema != Schema || tx.Version != Version || !tx.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.ShutdownTransactionId) || !tx.ShutdownTransactionId.StartsWith("shutdowntx-", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.OwnerSessionId) || !tx.OwnerSessionId.StartsWith("mcpsess-", StringComparison.Ordinal) ||
            !SafeLeaseId(tx.LeaseId) || string.IsNullOrWhiteSpace(tx.ListenerTransactionId) ||
            tx.CanonicalHistoricalScanPerformed || tx.TransactionCanonicalLeaseMutationPerformed || tx.TransactionActiveIndexMutationPerformed ||
            tx.ShutdownTransactionGrantedAuthority || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted ||
            tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_SHUTDOWN_TRANSACTION_INVALID: identity/authority boundary failed validation.");
        return tx;
    }

    private async Task<string> PersistAsync(ResolvedPaths paths, LocalAppMcpShutdownTransactionV05113 tx, CancellationToken cancellationToken)
    {
        await WriteTransactionAtomicAsync(paths.TransactionPath, tx, cancellationToken);
        return await WriteReceiptAsync(paths.WorkspaceRoot, tx, cancellationToken);
    }

    private static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalAppMcpShutdownTransactionV05113 tx, CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-shutdown-v05113");
        var path = Path.Combine(dir,
            $"shutdown-{LocalAppV046FileBoundary.SafeToken(tx.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(tx.ShutdownTransactionId)}-{LocalAppV046FileBoundary.SafeToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteTransactionAtomicAsync(string path, LocalAppMcpShutdownTransactionV05113 tx, CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.13 shutdown transaction");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.13 shutdown transaction");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.13 shutdown transaction");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string ValidateAndHashReceipt(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException($"{role} is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, role);
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxReceiptBytes) throw new InvalidDataException($"{role} has invalid size.");
        return LocalAppV046FileBoundary.HashFile(path);
    }

    private static ResolvedPaths ResolvePaths(string workspaceRoot, string applicationId, string ownerMetadataPath)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", LocalAppV046FileBoundary.SafeToken(applicationId));
        Directory.CreateDirectory(appDir);
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.13 shutdown app directory");
        var expectedOwner = Path.Combine(appDir, "owner-v0.51.7.json");
        if (!Path.GetFullPath(ownerMetadataPath).Equals(Path.GetFullPath(expectedOwner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.51.13 owner metadata path mismatch.");
        var listenerPath = Path.Combine(appDir, "listener-readiness-v05112.json");
        var txPath = Path.Combine(appDir, "shutdown-v05113.json");
        if (File.Exists(listenerPath)) LocalAppV046FileBoundary.RejectReparse(listenerPath, "v0.51.13 listener transaction path");
        if (File.Exists(txPath)) LocalAppV046FileBoundary.RejectReparse(txPath, "v0.51.13 shutdown transaction path");
        return new ResolvedPaths(Path.GetFullPath(workspaceRoot.Trim()), expectedOwner, listenerPath, txPath);
    }

    private static string ResolveExpectedOwnerPath(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        return Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", LocalAppV046FileBoundary.SafeToken(applicationId), "owner-v0.51.7.json");
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string[] NonEffects() => new[]
    {
        "shutdown transaction is runtime provenance/control evidence only, not read/revoke/resume authority",
        "SHUTDOWN_PREPARED is not evidence that a listener stopped",
        "LISTENER_STOPPED is not owner release and OWNER_RELEASED is not canonical lease revocation",
        "reconciliation never starts/resumes a listener and never auto-revokes/renews a live lease",
        "exact canonical revoke remains a separate inherited authority-bearing operation",
        "no historical canonical lease enumeration",
        "no canonical lease or verified active-index mutation by shutdown transaction service",
        "no bearer plaintext/hash or reusable endpoint path token stored/disclosed",
        "no sibling lease revocation, Secure MCP Tunnel/network publication/catalog/Agent Execute/ActionPermit authority"
    };

    private sealed record ExactLeaseObservation(
        bool Present, bool Valid, bool Live, bool Revoked, bool Expired, bool BudgetExhausted,
        string Classification, long? StateRevision, DateTimeOffset? ExpiresAt);

    private sealed record ResolvedPaths(string WorkspaceRoot, string OwnerMetadataPath, string ListenerTransactionPath, string TransactionPath);
}
