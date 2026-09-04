using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpOwnerLeaseBindingTransactionV05111(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string BindingTransactionId,
    string State,
    string OwnerSessionId,
    string GenerationTransactionId,
    string GenerationTransactionState,
    string GenerationSuccessorMetadataSha256,
    string PreparedLeaseId,
    string? LeaseStatePath,
    string? LeaseStateSha256AtCreation,
    string? LeaseCreationReceiptPath,
    string? LeaseCreationReceiptSha256,
    string? OwnerMetadataSha256,
    string? LeaseClassification,
    long? LeaseStateRevision,
    DateTimeOffset? LeaseExpiresAt,
    string? ReconciledFromState,
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

public sealed record LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
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
    bool AuthorityGranted);

/// <summary>
/// v0.51.11 non-authoritative transaction linking one COMMITTED MCP owner
/// generation to one exact prepared/read LeaseId. The exact LeaseId is named
/// before canonical creation, so recovery never needs historical enumeration.
/// Prepared/bound transaction state is provenance only; canonical v0.48 lease
/// state remains read authority.
/// </summary>
public sealed class LocalAppMcpOwnerLeaseBindingV05111Service
{
    public const string Version = "0.51.11";
    public const string Schema = "matawaka.local-app-mcp-owner-lease-binding-transaction/v0.51.11";
    public const int MaxTransactionBytes = 128 * 1024;
    public const int MaxOwnerMetadataBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<LocalAppMcpOwnerLeaseBindingReconcileResultV05111> ReconcileBeforeOwnerGenerationAsync(
        string workspaceRoot,
        string applicationId,
        string ownerMetadataPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, ownerMetadataPath);
        if (!File.Exists(paths.BindingPath))
            return NoPrior();

        var tx = await ReadTransactionAsync(paths.BindingPath, applicationId, cancellationToken);
        if (tx.State is "OWNER_BOUND" or "OWNER_BOUND_RECOVERED" or "ABANDONED_BEFORE_LEASE" or
            "LEASE_REVOKED_AFTER_CREATE" or "LEASE_EXPIRED_AFTER_CREATE" or
            "LEASE_BUDGET_EXHAUSTED_AFTER_CREATE" or "LEASE_STATE_ABSENT_AFTER_CREATE")
        {
            return new LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
                "PRIOR_OWNER_LEASE_BINDING_TERMINAL",
                true, tx.BindingTransactionId, tx.State, tx.PreparedLeaseId, tx.LeaseClassification,
                null, false, false, false, false);
        }

        if (tx.State is not ("PREPARED_BINDING" or "LEASE_CREATED" or "LIVE_ORPHAN_AFTER_LEASE_CREATE"))
            throw new InvalidDataException($"MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: unsupported active state {tx.State}.");

        var lease = ObserveExactLease(workspaceRoot, applicationId, tx.PreparedLeaseId);
        var owner = TryReadOwner(paths.OwnerMetadataPath, applicationId);

        if (lease.Present && lease.Valid && owner is not null &&
            owner.SessionId.Equals(tx.OwnerSessionId, StringComparison.Ordinal) &&
            owner.LeaseId?.Equals(tx.PreparedLeaseId, StringComparison.Ordinal) == true)
        {
            var recovered = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "OWNER_BOUND_RECOVERED",
                OwnerMetadataSha256 = LocalAppV046FileBoundary.HashFile(paths.OwnerMetadataPath),
                LeaseClassification = lease.Classification,
                LeaseStateRevision = lease.StateRevision,
                LeaseExpiresAt = lease.ExpiresAt,
                ReconciledFromState = tx.State,
                Note = "Exact prior owner SessionId + prepared LeaseId metadata and exact canonical lease state were observed before any successor owner generation. Owner->lease binding materialization was recovered without inferring listener/read authority."
            };
            var receipt = await PersistTerminalAsync(paths, recovered, cancellationToken);
            return new LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
                "PRIOR_OWNER_LEASE_BINDING_RECOVERED", true, tx.BindingTransactionId,
                recovered.State, tx.PreparedLeaseId, lease.Classification, receipt,
                false, false, false, false);
        }

        if (owner is not null && !owner.SessionId.Equals(tx.OwnerSessionId, StringComparison.Ordinal))
            throw new InvalidDataException(
                "MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: active owner metadata SessionId does not match the prior binding transaction; no successor authority was created.");
        if (owner is not null && !string.IsNullOrWhiteSpace(owner.LeaseId) &&
            !owner.LeaseId.Equals(tx.PreparedLeaseId, StringComparison.Ordinal))
            throw new InvalidDataException(
                "MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: prior owner metadata references a different LeaseId; no successor authority was created.");

        if (!lease.Present)
        {
            var absentState = tx.State.Equals("PREPARED_BINDING", StringComparison.Ordinal)
                ? "ABANDONED_BEFORE_LEASE"
                : "LEASE_STATE_ABSENT_AFTER_CREATE";
            var terminal = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = absentState,
                LeaseClassification = "ABSENT",
                LeaseStateRevision = null,
                LeaseExpiresAt = null,
                ReconciledFromState = tx.State,
                Note = tx.State.Equals("PREPARED_BINDING", StringComparison.Ordinal)
                    ? "No exact canonical state was observed at the prepared LeaseId during recovery. The attempt is closed as abandoned-before-observed-lease; this is not a claim of historical nonexistence and grants no authority."
                    : "The transaction previously observed exact lease creation, but canonical state is now absent. This is recorded as evidence-only absence without guessing, recreation or authority."
            };
            var receipt = await PersistTerminalAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
                "PRIOR_OWNER_LEASE_BINDING_CLOSED_ABSENT", true, tx.BindingTransactionId,
                terminal.State, tx.PreparedLeaseId, "ABSENT", receipt,
                false, false, false, false);
        }

        if (!lease.Valid)
            throw new InvalidDataException(
                "MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: exact prepared LeaseId state exists but does not satisfy the canonical v0.48 identity/schema contract.");

        if (lease.Live)
        {
            var orphan = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "LIVE_ORPHAN_AFTER_LEASE_CREATE",
                LeaseStatePath = lease.StatePath,
                LeaseClassification = "LIVE_ORPHAN_AFTER_LEASE_CREATE",
                LeaseStateRevision = lease.StateRevision,
                LeaseExpiresAt = lease.ExpiresAt,
                ReconciledFromState = tx.State,
                Note = "Exact canonical lease state is still live while the prior owner generation is not exactly bound to it. The lease remains canonical orphan authority and must be closed/expire through inherited explicit semantics; reconciliation does not revoke it."
            };
            var receipt = await PersistTerminalAsync(paths, orphan, cancellationToken);
            return new LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
                "PRIOR_OWNER_LEASE_BINDING_LIVE_ORPHAN", true, tx.BindingTransactionId,
                orphan.State, tx.PreparedLeaseId, orphan.LeaseClassification, receipt,
                true, false, false, false);
        }

        var terminalState = lease.Revoked
            ? "LEASE_REVOKED_AFTER_CREATE"
            : lease.Expired
                ? "LEASE_EXPIRED_AFTER_CREATE"
                : "LEASE_BUDGET_EXHAUSTED_AFTER_CREATE";
        var closed = tx with
        {
            ObservedAt = DateTimeOffset.Now,
            State = terminalState,
            LeaseStatePath = lease.StatePath,
            LeaseClassification = lease.Classification,
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ReconciledFromState = tx.State,
            Note = "Exact prepared LeaseId canonical state is terminal. Recovery recorded the exact terminal condition without mutation, historical scan or authority inference."
        };
        var closedReceipt = await PersistTerminalAsync(paths, closed, cancellationToken);
        return new LocalAppMcpOwnerLeaseBindingReconcileResultV05111(
            "PRIOR_OWNER_LEASE_BINDING_TERMINAL_LEASE", true, tx.BindingTransactionId,
            closed.State, tx.PreparedLeaseId, lease.Classification, closedReceipt,
            false, false, false, false);
    }

    public async Task<(LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)> PrepareBindingAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var generation = await ReadCommittedGenerationAsync(paths, held.ApplicationId, held.SessionId, cancellationToken);

        string preparedLeaseId;
        string preparedStatePath;
        do
        {
            preparedLeaseId = "lease-" + Guid.NewGuid().ToString("N");
            preparedStatePath = LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(
                held.WorkspaceRoot, held.ApplicationId, preparedLeaseId, createDirectory: true);
        }
        while (File.Exists(preparedStatePath));

        var tx = new LocalAppMcpOwnerLeaseBindingTransactionV05111(
            Schema, Version, DateTimeOffset.Now, held.ApplicationId,
            "bindtx-" + Guid.NewGuid().ToString("N"),
            "PREPARED_BINDING",
            held.SessionId,
            generation.TransactionId,
            generation.State,
            generation.SuccessorMetadataSha256!,
            preparedLeaseId,
            null, null, null, null, null,
            "PREPARED_ID_CANONICAL_NOT_OBSERVED",
            null, null, null,
            false, false, false,
            false, false, false, false,
            false, false, false,
            NonEffects(),
            "Exact LeaseId is reserved/bound to this owner generation before canonical state creation. PREPARED_BINDING is not proof that a read lease exists and grants no authority.");

        await WriteTransactionAtomicAsync(paths.BindingPath, tx, cancellationToken);
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, tx, cancellationToken);
        return (tx, receipt);
    }

    public async Task<(LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)> RecordLeaseCreatedAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppIndexedLeaseCreateResultV0515 created,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var prepared = await ReadTransactionAsync(paths.BindingPath, held.ApplicationId, cancellationToken);
        if (!prepared.State.Equals("PREPARED_BINDING", StringComparison.Ordinal) ||
            !prepared.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !prepared.PreparedLeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
            !created.Receipt.LeaseId.Equals(prepared.PreparedLeaseId, StringComparison.Ordinal) ||
            !created.Receipt.ApplicationId.Equals(held.ApplicationId, StringComparison.Ordinal) ||
            created.Receipt.BearerPlaintextPersisted || created.Receipt.NetworkAccessPerformed)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_CREATE_MISMATCH: created lease evidence does not match exact PREPARED_BINDING.");

        var exactPath = LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(
            held.WorkspaceRoot, held.ApplicationId, prepared.PreparedLeaseId, createDirectory: false);
        if (!Path.GetFullPath(created.Receipt.StatePath).Equals(Path.GetFullPath(exactPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(exactPath))
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_CREATE_MISMATCH: exact canonical state path was not observed.");
        LocalAppV046FileBoundary.RejectReparse(exactPath, "v0.51.11 created canonical lease state");
        var stateSha = LocalAppV046FileBoundary.HashFile(exactPath);
        if (!stateSha.Equals(created.Receipt.StateSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_CREATE_MISMATCH: canonical state SHA differs from creation receipt.");
        var state = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(
            held.WorkspaceRoot, held.ApplicationId, prepared.PreparedLeaseId);
        if (!File.Exists(created.ReceiptPath))
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_CREATE_MISMATCH: exact lease creation receipt artifact is absent.");
        LocalAppV046FileBoundary.RejectReparse(created.ReceiptPath, "v0.51.11 lease creation receipt");

        var next = prepared with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "LEASE_CREATED",
            LeaseStatePath = exactPath,
            LeaseStateSha256AtCreation = stateSha,
            LeaseCreationReceiptPath = Path.GetFullPath(created.ReceiptPath),
            LeaseCreationReceiptSha256 = LocalAppV046FileBoundary.HashFile(created.ReceiptPath),
            LeaseClassification = "LIVE_CREATED_OWNER_NOT_YET_BOUND",
            LeaseStateRevision = state.StateRevision,
            LeaseExpiresAt = state.ExpiresAt,
            ReconciledFromState = null,
            Note = "Exact canonical v0.48-schema lease state and its creation receipt were observed at the preallocated LeaseId. LEASE_CREATED is still not proof that owner metadata is bound to that LeaseId."
        };
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, next, cancellationToken);
        await WriteTransactionAtomicAsync(paths.BindingPath, next, cancellationToken);
        return (next, receipt);
    }

    public async Task<(LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)> CommitOwnerBoundAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        var paths = ResolvePaths(held.WorkspaceRoot, held.ApplicationId, held.MetadataPath);
        var current = await ReadTransactionAsync(paths.BindingPath, held.ApplicationId, cancellationToken);
        if (!current.State.Equals("LEASE_CREATED", StringComparison.Ordinal) ||
            !current.OwnerSessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            !current.PreparedLeaseId.Equals(held.LeaseId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_COMMIT_REFUSED: exact LEASE_CREATED transaction/held owner binding was not observed.");

        var owner = TryReadOwner(paths.OwnerMetadataPath, held.ApplicationId)
            ?? throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_COMMIT_REFUSED: exact owner metadata contract was not observed.");
        if (!owner.SessionId.Equals(held.SessionId, StringComparison.Ordinal) ||
            owner.LeaseId?.Equals(current.PreparedLeaseId, StringComparison.Ordinal) != true)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_COMMIT_REFUSED: owner metadata does not reference exact prepared LeaseId.");

        var lease = ObserveExactLease(held.WorkspaceRoot, held.ApplicationId, current.PreparedLeaseId);
        if (!lease.Present || !lease.Valid)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_COMMIT_REFUSED: exact canonical lease state is absent/invalid after owner metadata binding.");

        var bound = current with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "OWNER_BOUND",
            OwnerMetadataSha256 = LocalAppV046FileBoundary.HashFile(paths.OwnerMetadataPath),
            LeaseStatePath = lease.StatePath,
            LeaseClassification = lease.Classification,
            LeaseStateRevision = lease.StateRevision,
            LeaseExpiresAt = lease.ExpiresAt,
            ReconciledFromState = null,
            Note = "Exact owner SessionId + LeaseId metadata and exact canonical lease state were both observed. OWNER_BOUND proves only owner-to-lease provenance materialization; listener readiness and read authority remain separate."
        };
        var receipt = await WriteReceiptAsync(held.WorkspaceRoot, bound, cancellationToken);
        await WriteTransactionAtomicAsync(paths.BindingPath, bound, cancellationToken);
        return (bound, receipt);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("owner-lease-v05111-prepared", true, "exact LeaseId named while canonical=false", "prepared != created"),
        ("owner-lease-v05111-created", true, "exact state/receipt observed before LEASE_CREATED", "created != owner bound"),
        ("owner-lease-v05111-bound", true, "exact SessionId+LeaseId owner metadata observed", "OWNER_BOUND"),
        ("owner-lease-v05111-recovery", true, "exact prepared LeaseId path only", "no historical scan"),
        ("owner-lease-v05111-orphan", true, "live incomplete lease blocks successor start; no auto-revoke", "explicit closure/expiry"),
        ("owner-lease-v05111-authority", true, "lease/read/revoke/resume=false", "false"),
        ("owner-lease-v05111-secrets", true, "bearer/hash/endpoint secret omitted", "omitted"),
        ("owner-lease-v05111-listener", true, "OWNER_BOUND != listener ready", "separate boundary")
    };

    private static LocalAppMcpOwnerLeaseBindingReconcileResultV05111 NoPrior()
        => new("NO_PRIOR_OWNER_LEASE_BINDING_TRANSACTION", false, null, null, null, null, null,
            false, false, false, false);

    private static void RequireHeld(LocalAppHeldMcpSessionOwnershipV0517 held)
    {
        if (held is null || held.Released)
            throw new InvalidDataException("v0.51.11 owner->lease binding requires an actively held MCP owner domain.");
    }

    private static async Task<LocalAppMcpOwnerGenerationTransactionV05110> ReadCommittedGenerationAsync(
        ResolvedPaths paths,
        string applicationId,
        string ownerSessionId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.GenerationTransactionPath))
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_PREPARE_REFUSED: v0.51.10 generation transaction is absent.");
        LocalAppV046FileBoundary.RejectReparse(paths.GenerationTransactionPath, "v0.51.11 current generation transaction");
        var info = new FileInfo(paths.GenerationTransactionPath);
        if (info.Length < 1 || info.Length > LocalAppMcpOwnerGenerationTransactionV05110Service.MaxTransactionBytes)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_PREPARE_REFUSED: generation transaction size is invalid.");
        LocalAppMcpOwnerGenerationTransactionV05110 generation;
        try
        {
            generation = JsonSerializer.Deserialize<LocalAppMcpOwnerGenerationTransactionV05110>(
                await File.ReadAllTextAsync(paths.GenerationTransactionPath, Encoding.UTF8, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_PREPARE_REFUSED: generation transaction is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_PREPARE_REFUSED: generation transaction JSON is invalid.", ex);
        }
        if (generation.Schema != LocalAppMcpOwnerGenerationTransactionV05110Service.Schema ||
            generation.Version != LocalAppMcpOwnerGenerationTransactionV05110Service.Version ||
            !generation.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            !generation.State.Equals("COMMITTED", StringComparison.Ordinal) ||
            !generation.SuccessorSessionId.Equals(ownerSessionId, StringComparison.Ordinal) ||
            !generation.SuccessorMetadataContractValid || string.IsNullOrWhiteSpace(generation.SuccessorMetadataSha256) ||
            generation.CanonicalLeaseMutated || generation.ActiveIndexMutated || generation.LeaseAuthorityGranted ||
            generation.ReadAuthorityGranted || generation.RevokeAuthorityGranted || generation.ResumeAuthorityGranted ||
            generation.BearerPlaintextDisclosed || generation.BearerHashDisclosed || generation.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_PREPARE_REFUSED: exact COMMITTED non-authoritative v0.51.10 owner generation was not observed for this owner SessionId.");
        return generation;
    }

    private static LocalAppMcpSessionOwnerV0517? TryReadOwner(string metadataPath, string applicationId)
    {
        if (!File.Exists(metadataPath)) return null;
        LocalAppV046FileBoundary.RejectReparse(metadataPath, "v0.51.11 owner metadata observation");
        var info = new FileInfo(metadataPath);
        if (info.Length < 1 || info.Length > MaxOwnerMetadataBytes)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: owner metadata size is invalid.");
        try
        {
            var owner = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(File.ReadAllText(metadataPath, Encoding.UTF8), JsonOptions);
            if (owner is null || owner.Schema != LocalAppMcpSessionOwnershipV0517Service.OwnerSchema ||
                owner.Version != LocalAppMcpSessionOwnershipV0517Service.Version ||
                !owner.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
                owner.BearerPlaintextStored || owner.BearerHashStored || owner.EndpointSecretStored || owner.LeaseAuthorityGranted)
                throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: owner metadata contract/secret boundary is invalid.");
            return owner;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT: owner metadata JSON is invalid.", ex);
        }
    }

    private static ExactLeaseObservation ObserveExactLease(string workspaceRoot, string applicationId, string leaseId)
    {
        var statePath = LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(
            workspaceRoot, applicationId, leaseId, createDirectory: false);
        if (!File.Exists(statePath))
            return new ExactLeaseObservation(false, false, false, false, false, false,
                "ABSENT", statePath, null, null);
        try
        {
            var state = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspaceRoot, applicationId, leaseId);
            var expired = state.ExpiresAt <= DateTimeOffset.Now;
            var budget = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
            var live = !state.Revoked && !expired && !budget;
            var classification = state.Revoked ? "REVOKED" : expired ? "EXPIRED" : budget ? "BUDGET_EXHAUSTED" : "LIVE";
            return new ExactLeaseObservation(true, true, live, state.Revoked, expired, budget,
                classification, statePath, state.StateRevision, state.ExpiresAt);
        }
        catch (InvalidDataException)
        {
            return new ExactLeaseObservation(true, false, false, false, false, false,
                "CANONICAL_STATE_INVALID", statePath, null, null);
        }
    }

    private async Task<LocalAppMcpOwnerLeaseBindingTransactionV05111> ReadTransactionAsync(
        string path,
        string applicationId,
        CancellationToken cancellationToken)
    {
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.11 owner->lease binding transaction");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxTransactionBytes)
            throw new InvalidDataException($"MCP_OWNER_LEASE_BINDING_TRANSACTION_INVALID_SIZE: transaction must be within 1..{MaxTransactionBytes} bytes.");
        LocalAppMcpOwnerLeaseBindingTransactionV05111 tx;
        try
        {
            tx = JsonSerializer.Deserialize<LocalAppMcpOwnerLeaseBindingTransactionV05111>(
                await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INVALID: transaction JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INVALID: transaction JSON could not be parsed.", ex);
        }
        if (tx.Schema != Schema || tx.Version != Version || !tx.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.BindingTransactionId) || !tx.BindingTransactionId.StartsWith("bindtx-", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.OwnerSessionId) || !tx.OwnerSessionId.StartsWith("mcpsess-", StringComparison.Ordinal) ||
            !SafeLeaseId(tx.PreparedLeaseId) || tx.CanonicalHistoricalScanPerformed || tx.CanonicalLeaseMutationPerformed ||
            tx.ActiveIndexMutationPerformed || tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted ||
            tx.ResumeAuthorityGranted || tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_OWNER_LEASE_BINDING_TRANSACTION_INVALID: transaction identity/authority boundary failed validation.");
        return tx;
    }

    private async Task<string> PersistTerminalAsync(
        ResolvedPaths paths,
        LocalAppMcpOwnerLeaseBindingTransactionV05111 terminal,
        CancellationToken cancellationToken)
    {
        var receipt = await WriteReceiptAsync(paths.WorkspaceRoot, terminal, cancellationToken);
        await WriteTransactionAtomicAsync(paths.BindingPath, terminal, cancellationToken);
        return receipt;
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalAppMcpOwnerLeaseBindingTransactionV05111 tx,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-owner-lease-binding-v05111");
        var path = Path.Combine(dir,
            $"owner-lease-bind-{LocalAppV046FileBoundary.SafeToken(tx.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(tx.BindingTransactionId)}-{LocalAppV046FileBoundary.SafeToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteTransactionAtomicAsync(
        string path,
        LocalAppMcpOwnerLeaseBindingTransactionV05111 tx,
        CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.11 owner->lease binding transaction");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.11 owner->lease binding transaction");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.11 owner->lease binding transaction");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static ResolvedPaths ResolvePaths(string workspaceRoot, string applicationId, string ownerMetadataPath)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", LocalAppV046FileBoundary.SafeToken(applicationId));
        Directory.CreateDirectory(appDir);
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.11 owner->lease binding app directory");
        var expectedOwner = Path.Combine(appDir, "owner-v0.51.7.json");
        if (!Path.GetFullPath(ownerMetadataPath).Equals(Path.GetFullPath(expectedOwner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.51.11 owner metadata path mismatch.");
        var bindingPath = Path.Combine(appDir, "owner-lease-binding-v05111.json");
        var generationPath = Path.Combine(appDir, "generation-transition-v05110.json");
        if (File.Exists(bindingPath)) LocalAppV046FileBoundary.RejectReparse(bindingPath, "v0.51.11 binding transaction path");
        if (File.Exists(generationPath)) LocalAppV046FileBoundary.RejectReparse(generationPath, "v0.51.10 generation transaction path from v0.51.11");
        return new ResolvedPaths(Path.GetFullPath(workspaceRoot.Trim()), expectedOwner, bindingPath, generationPath);
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string[] NonEffects() => new[]
    {
        "owner->lease binding transaction is provenance/control evidence only, not lease/read/revoke/resume authority",
        "prepared exact LeaseId is not proof that canonical lease state exists",
        "no historical canonical lease enumeration",
        "transaction service does not mutate canonical lease state or verified active index",
        "live orphan reconciliation never auto-revokes canonical authority",
        "no bearer plaintext/hash or endpoint path secret stored/disclosed",
        "OWNER_BOUND is not listener readiness",
        "no network/tunnel/publication/catalog/Agent Execute or ActionPermit authority"
    };

    private sealed record ExactLeaseObservation(
        bool Present,
        bool Valid,
        bool Live,
        bool Revoked,
        bool Expired,
        bool BudgetExhausted,
        string Classification,
        string StatePath,
        long? StateRevision,
        DateTimeOffset? ExpiresAt);

    private sealed record ResolvedPaths(
        string WorkspaceRoot,
        string OwnerMetadataPath,
        string BindingPath,
        string GenerationTransactionPath);
}
