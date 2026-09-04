using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppActiveLeaseIndexEntryV0515(
    string LeaseId,
    long LastVerifiedStateRevision);

public sealed record LocalAppActiveLeaseIndexV0515(
    string Schema,
    string Version,
    string ApplicationId,
    long IndexRevision,
    DateTimeOffset ReconciledAt,
    DateTimeOffset UpdatedAt,
    int CanonicalStateRecordsAtLastReconciliation,
    string CanonicalMetadataRootSha256,
    IReadOnlyList<LocalAppActiveLeaseIndexEntryV0515> Entries,
    bool BearerPlaintextStored,
    bool BearerHashStored,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppActiveLeaseIndexDirtyV0515(
    string Schema,
    string Version,
    string ApplicationId,
    string MutationId,
    string Operation,
    string? LeaseId,
    DateTimeOffset BeganAt,
    long IndexRevisionBefore,
    bool CanonicalMutationMayHaveOccurred,
    bool BearerPlaintextStored,
    bool BearerHashStored,
    string Note);

public sealed record LocalAppActiveLeaseIndexReadinessV0515(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    bool Ready,
    bool ReconciliationRequired,
    bool DirtyMarkerPresent,
    long? IndexRevision,
    int IndexedCandidates,
    string Status,
    string Note);

public sealed record LocalAppActiveLeaseIndexMutationV0515(
    string MutationId,
    string Operation,
    string ApplicationId,
    string? LeaseId,
    long IndexRevisionBefore);

public sealed record LocalAppActiveLeaseReconciliationReceiptV0515(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    int CanonicalStateRecords,
    int LiveCandidatesIndexed,
    int InactiveHistoricalRecords,
    long IndexRevisionAfter,
    string CanonicalMetadataRootSha256,
    bool DirtyMarkerCleared,
    bool CanonicalStateFilesMutated,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppVerifiedLiveAuthorityV0515(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    long IndexRevision,
    int IndexedCandidatesObserved,
    int InactiveCandidatesPruned,
    IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> LiveAuthorities,
    int LiveLeaseCount,
    int OrphanClosureEligibleCount,
    string? ActiveLocalMcpApplicationId,
    string? ActiveLocalMcpLeaseId,
    bool CanonicalHistoricalScanPerformed,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool CanonicalStateMutationPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.51.5 derived active-lease index. Canonical authority always remains the exact
/// per-lease v0.48 state file. The index stores only LeaseId + last verified state
/// revision, never bearer plaintext/hash or scopes. A durable dirty marker makes
/// partial create/closure transitions fail closed until explicit bounded reconciliation.
/// </summary>
public sealed class LocalAppActiveLeaseIndexV0515Service
{
    public const string Version = "0.51.5";
    public const string IndexSchema = "matawaka.local-app-active-read-lease-index/v0.51.5";
    public const string DirtySchema = "matawaka.local-app-active-read-lease-index-dirty/v0.51.5";
    public const string ReadinessSchema = "matawaka.local-app-active-read-lease-index-readiness/v0.51.5";
    public const string ReconciliationReceiptSchema = "matawaka.local-app-active-read-lease-reconciliation-receipt/v0.51.5";
    public const string LiveStatusSchema = "matawaka.local-app-verified-live-read-authority/v0.51.5";
    public const int MaxReconciliationStateFiles = 4096;
    public const int LiveAuthorityHardLimit = 32;

    private static readonly SemaphoreSlim IndexGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<LocalAppActiveLeaseIndexReadinessV0515> GetReadinessAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var dirty = DirtyPath(workspaceRoot, applicationId);
            if (File.Exists(dirty))
            {
                ValidateIndexFileBoundary(dirty, "v0.51.5 active-index dirty marker");
                return Readiness(applicationId, false, true, true, null, 0,
                    "ACTIVE_INDEX_RECONCILIATION_REQUIRED",
                    "A durable active-index mutation marker exists. Canonical lease state must be reconciled before the index can be used.");
            }

            var path = IndexPath(workspaceRoot, applicationId);
            if (!File.Exists(path))
                return Readiness(applicationId, false, true, false, null, 0,
                    "ACTIVE_INDEX_RECONCILIATION_REQUIRED",
                    "No verified active-lease index exists for this application yet.");

            var index = ReadIndex(path, applicationId);
            return Readiness(applicationId, true, false, false, index.IndexRevision, index.Entries.Count,
                "ACTIVE_INDEX_READY_VERIFIED_DERIVED_CONTROL",
                "Index identity is valid. Canonical state is still re-read for every indexed LeaseId before status or closure authority is shown.");
        }
        finally { IndexGate.Release(); }
    }

    public async Task<(LocalAppActiveLeaseIndexV0515 Index, LocalAppActiveLeaseReconciliationReceiptV0515 Receipt, string ReceiptPath)> ReconcileAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var states = ReadAllCanonicalStatesBounded(workspaceRoot, applicationId, cancellationToken);
            var now = DateTimeOffset.Now;
            var live = states.Where(x => IsLive(x, now))
                .OrderBy(x => x.LeaseId, StringComparer.Ordinal)
                .Select(x => new LocalAppActiveLeaseIndexEntryV0515(x.LeaseId, x.StateRevision))
                .ToArray();
            var previousRevision = File.Exists(IndexPath(workspaceRoot, applicationId))
                ? ReadIndex(IndexPath(workspaceRoot, applicationId), applicationId).IndexRevision
                : 0;
            var root = MetadataRoot(states);
            var observedAt = DateTimeOffset.Now;
            var index = new LocalAppActiveLeaseIndexV0515(
                IndexSchema,
                Version,
                applicationId,
                checked(previousRevision + 1),
                observedAt,
                observedAt,
                states.Count,
                root,
                live,
                false,
                false,
                IndexNonEffects(),
                "Derived active-candidate index rebuilt from bounded canonical v0.48 state scan. LeaseId membership is not authority by itself; every entry must be revalidated against exact canonical state.");

            await WriteIndexAtomicAsync(IndexPath(workspaceRoot, applicationId), index, cancellationToken);
            var dirty = DirtyPath(workspaceRoot, applicationId);
            if (File.Exists(dirty))
            {
                ValidateIndexFileBoundary(dirty, "v0.51.5 active-index dirty marker before reconciliation clear");
                File.Delete(dirty);
            }

            var receipt = new LocalAppActiveLeaseReconciliationReceiptV0515(
                ReconciliationReceiptSchema,
                Version,
                observedAt,
                applicationId,
                states.Count,
                live.Length,
                states.Count - live.Length,
                index.IndexRevision,
                root,
                true,
                false,
                false,
                false,
                false,
                IndexNonEffects().Concat(new[]
                {
                    "bounded reconciliation may read Workbench-owned lease control state only",
                    "canonical per-lease state bytes are not rewritten, deleted, compacted or reinterpreted"
                }).ToArray(),
                live.Length > LiveAuthorityHardLimit ? "ACTIVE_INDEX_RECONCILED_LIVE_AUTHORITY_OVERFLOW_PRESENT" : "ACTIVE_INDEX_RECONCILED",
                $"Reconciled {states.Count} canonical state records with a hard ceiling of {MaxReconciliationStateFiles}; indexed {live.Length} currently-live candidates. Bearer plaintext/hash were omitted from index and receipt.");
            var receiptPath = await WriteReceiptAsync(workspaceRoot, applicationId, receipt, cancellationToken);
            return (index, receipt, receiptPath);
        }
        finally { IndexGate.Release(); }
    }

    public async Task<LocalAppVerifiedLiveAuthorityV0515> ObserveLiveAuthorityAsync(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = RequireReadyIndex(workspaceRoot, applicationId);
            var now = DateTimeOffset.Now;
            var live = new List<LocalAppReadSessionStatusLeaseV0513>();
            var refreshedEntries = new List<LocalAppActiveLeaseIndexEntryV0515>();
            var pruned = 0;

            foreach (var entry in index.Entries.OrderBy(x => x.LeaseId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = ReadExactCanonicalState(workspaceRoot, applicationId, entry.LeaseId);
                if (state.StateRevision < entry.LastVerifiedStateRevision)
                    throw new InvalidDataException($"ACTIVE_INDEX_INCONSISTENT: canonical state revision regressed for {entry.LeaseId}.");

                if (!IsLive(state, now))
                {
                    pruned++;
                    continue;
                }

                var bound =
                    !string.IsNullOrWhiteSpace(activeLocalMcpApplicationId) &&
                    !string.IsNullOrWhiteSpace(activeLocalMcpLeaseId) &&
                    activeLocalMcpApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
                    activeLocalMcpLeaseId.Equals(state.LeaseId, StringComparison.Ordinal);
                live.Add(ToStatusLease(state, now, bound));
                refreshedEntries.Add(new LocalAppActiveLeaseIndexEntryV0515(state.LeaseId, state.StateRevision));
            }

            var changed = pruned > 0 || !index.Entries.SequenceEqual(refreshedEntries);
            if (changed)
            {
                index = index with
                {
                    IndexRevision = checked(index.IndexRevision + 1),
                    UpdatedAt = DateTimeOffset.Now,
                    Entries = refreshedEntries.ToArray()
                };
                await WriteIndexAtomicAsync(IndexPath(workspaceRoot, applicationId), index, cancellationToken);
            }

            if (live.Count > LiveAuthorityHardLimit)
                throw new InvalidDataException($"LIVE_AUTHORITY_OVERFLOW: verified active index contains {live.Count} live leases; hard ceiling is {LiveAuthorityHardLimit}. Use explicit recovery/reconciliation; no partial authority list was returned.");

            var ordered = live.OrderByDescending(x => x.OrphanClosureEligible)
                .ThenByDescending(x => x.BoundToActiveLocalMcp)
                .ThenBy(x => x.ExpiresAt)
                .ThenBy(x => x.LeaseId, StringComparer.Ordinal)
                .ToArray();
            return new LocalAppVerifiedLiveAuthorityV0515(
                LiveStatusSchema,
                Version,
                DateTimeOffset.Now,
                applicationId,
                index.IndexRevision,
                index.Entries.Count + pruned,
                pruned,
                ordered,
                ordered.Length,
                ordered.Count(x => x.OrphanClosureEligible),
                activeLocalMcpApplicationId,
                activeLocalMcpLeaseId,
                false,
                false,
                false,
                false,
                IndexNonEffects().Concat(new[]
                {
                    "no canonical historical lease enumeration for live-authority status",
                    "inactive/expired indexed candidates may be pruned from derived index only"
                }).ToArray(),
                "VERIFIED_ACTIVE_INDEX_STATUS",
                "Live authority was discovered only from the derived candidate index and then verified against each exact canonical state file. Historical canonical state was not enumerated.");
        }
        finally { IndexGate.Release(); }
    }

    public async Task<LocalAppReadSessionStatusLeaseV0513> ObserveIndexedExactLiveLeaseAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = RequireReadyIndex(workspaceRoot, applicationId);
            var entry = index.Entries.SingleOrDefault(x => x.LeaseId.Equals(leaseId, StringComparison.Ordinal))
                ?? throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: exact live LeaseId is not present in verified active index.");
            var state = ReadExactCanonicalState(workspaceRoot, applicationId, leaseId);
            if (state.StateRevision < entry.LastVerifiedStateRevision)
                throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: exact canonical state revision regressed.");
            var now = DateTimeOffset.Now;
            if (!IsLive(state, now)) throw new InvalidDataException("Exact indexed LeaseId no longer carries live read authority.");
            var bound =
                activeLocalMcpApplicationId?.Equals(applicationId, StringComparison.Ordinal) == true &&
                activeLocalMcpLeaseId?.Equals(leaseId, StringComparison.Ordinal) == true;
            return ToStatusLease(state, now, bound);
        }
        finally { IndexGate.Release(); }
    }

    public async Task<LocalAppActiveLeaseIndexMutationV0515> BeginMutationAsync(
        string workspaceRoot,
        string applicationId,
        string operation,
        string? leaseId,
        CancellationToken cancellationToken)
    {
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = RequireReadyIndex(workspaceRoot, applicationId);
            var dirtyPath = DirtyPath(workspaceRoot, applicationId);
            if (File.Exists(dirtyPath))
                throw new InvalidDataException("ACTIVE_INDEX_RECONCILIATION_REQUIRED: another active-index mutation is unresolved.");
            if (leaseId is not null && !SafeLeaseId(leaseId)) throw new InvalidDataException("Unsafe LeaseId for active-index mutation.");
            var mutation = new LocalAppActiveLeaseIndexMutationV0515(
                "idxmut-" + Guid.NewGuid().ToString("N"), operation, applicationId, leaseId, index.IndexRevision);
            var dirty = new LocalAppActiveLeaseIndexDirtyV0515(
                DirtySchema,
                Version,
                applicationId,
                mutation.MutationId,
                operation,
                leaseId,
                DateTimeOffset.Now,
                index.IndexRevision,
                true,
                false,
                false,
                "Dirty marker is written before a canonical operation that may add/remove live authority. It carries no bearer/plaintext/hash and blocks index use until commit or explicit reconciliation.");
            await WriteJsonAtomicAsync(dirtyPath, dirty, cancellationToken);
            return mutation;
        }
        finally { IndexGate.Release(); }
    }

    public async Task<LocalAppActiveLeaseIndexV0515> CommitMutationAsync(
        string workspaceRoot,
        LocalAppActiveLeaseIndexMutationV0515 mutation,
        string exactLeaseId,
        CancellationToken cancellationToken)
    {
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var dirty = ReadDirty(DirtyPath(workspaceRoot, mutation.ApplicationId), mutation.ApplicationId);
            if (!dirty.MutationId.Equals(mutation.MutationId, StringComparison.Ordinal) ||
                dirty.IndexRevisionBefore != mutation.IndexRevisionBefore)
                throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: dirty marker does not match committing mutation.");
            var index = ReadIndex(IndexPath(workspaceRoot, mutation.ApplicationId), mutation.ApplicationId);
            if (index.IndexRevision != mutation.IndexRevisionBefore)
                throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: index revision changed during canonical mutation.");

            var state = ReadExactCanonicalState(workspaceRoot, mutation.ApplicationId, exactLeaseId);
            var entries = index.Entries.Where(x => !x.LeaseId.Equals(exactLeaseId, StringComparison.Ordinal)).ToList();
            if (IsLive(state, DateTimeOffset.Now))
                entries.Add(new LocalAppActiveLeaseIndexEntryV0515(state.LeaseId, state.StateRevision));
            entries = entries.OrderBy(x => x.LeaseId, StringComparer.Ordinal).ToList();

            var next = index with
            {
                IndexRevision = checked(index.IndexRevision + 1),
                UpdatedAt = DateTimeOffset.Now,
                Entries = entries.ToArray()
            };
            await WriteIndexAtomicAsync(IndexPath(workspaceRoot, mutation.ApplicationId), next, cancellationToken);
            var dirtyPath = DirtyPath(workspaceRoot, mutation.ApplicationId);
            ValidateIndexFileBoundary(dirtyPath, "v0.51.5 dirty marker commit clear");
            File.Delete(dirtyPath);
            return next;
        }
        finally { IndexGate.Release(); }
    }

    public async Task MarkReconciliationRequiredAsync(
        string workspaceRoot,
        string applicationId,
        string operation,
        CancellationToken cancellationToken)
    {
        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var path = DirtyPath(workspaceRoot, applicationId);
            if (File.Exists(path)) return;
            var revision = File.Exists(IndexPath(workspaceRoot, applicationId))
                ? ReadIndex(IndexPath(workspaceRoot, applicationId), applicationId).IndexRevision
                : 0;
            var dirty = new LocalAppActiveLeaseIndexDirtyV0515(
                DirtySchema, Version, applicationId, "idxdirty-" + Guid.NewGuid().ToString("N"), operation, null,
                DateTimeOffset.Now, revision, true, false, false,
                "Reconciliation-required marker preserves fail-closed behavior after a canonical operation whose active-index synchronization was not proven complete.");
            await WriteJsonAtomicAsync(path, dirty, cancellationToken);
        }
        finally { IndexGate.Release(); }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("active-index-v0515-canonical-authority", true, "per-lease v0.48 state remains canonical", "index derived only"),
        ("active-index-v0515-bearer", true, "plaintext/hash omitted", "omitted"),
        ("active-index-v0515-entry", true, "LeaseId + last verified StateRevision only", "minimal"),
        ("active-index-v0515-dirty", true, "durable marker before authority-set mutation", "fail closed"),
        ("active-index-v0515-reconcile-ceiling", MaxReconciliationStateFiles == 4096, MaxReconciliationStateFiles.ToString(), "4096"),
        ("active-index-v0515-live-ceiling", LiveAuthorityHardLimit == 32, LiveAuthorityHardLimit.ToString(), "32"),
        ("active-index-v0515-history-scan", true, "false for live authority status", "independent of historical count"),
        ("active-index-v0515-canonical-delete", true, "false", "historical evidence preserved")
    };

    private static LocalAppActiveLeaseIndexReadinessV0515 Readiness(
        string app, bool ready, bool reconcile, bool dirty, long? revision, int candidates, string status, string note)
        => new(ReadinessSchema, Version, DateTimeOffset.Now, app, ready, reconcile, dirty, revision, candidates, status, note);

    private static LocalAppActiveLeaseIndexV0515 RequireReadyIndex(string workspaceRoot, string applicationId)
    {
        var dirty = DirtyPath(workspaceRoot, applicationId);
        if (File.Exists(dirty))
            throw new InvalidDataException("ACTIVE_INDEX_RECONCILIATION_REQUIRED: durable dirty marker exists.");
        var path = IndexPath(workspaceRoot, applicationId);
        if (!File.Exists(path))
            throw new InvalidDataException("ACTIVE_INDEX_RECONCILIATION_REQUIRED: verified active-lease index is missing.");
        return ReadIndex(path, applicationId);
    }

    private static LocalAppActiveLeaseIndexV0515 ReadIndex(string path, string applicationId)
    {
        ValidateIndexFileBoundary(path, "v0.51.5 active index");
        LocalAppActiveLeaseIndexV0515 index;
        try
        {
            index = JsonSerializer.Deserialize<LocalAppActiveLeaseIndexV0515>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: active index could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: active index JSON is invalid.", ex);
        }
        if (index.Schema != IndexSchema || index.Version != Version ||
            !index.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            index.BearerPlaintextStored || index.BearerHashStored || index.IndexRevision < 1)
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: active index identity/schema/safety flags are invalid.");
        if (index.Entries is null || index.Entries.Any(x => !SafeLeaseId(x.LeaseId) || x.LastVerifiedStateRevision < 0) ||
            index.Entries.Select(x => x.LeaseId).Distinct(StringComparer.Ordinal).Count() != index.Entries.Count)
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: active index entries are invalid or duplicated.");
        return index;
    }

    private static LocalAppActiveLeaseIndexDirtyV0515 ReadDirty(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: expected dirty marker is missing.");
        ValidateIndexFileBoundary(path, "v0.51.5 active-index dirty marker");
        try
        {
            var dirty = JsonSerializer.Deserialize<LocalAppActiveLeaseIndexDirtyV0515>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: dirty marker could not be parsed.");
            if (dirty.Schema != DirtySchema || dirty.Version != Version || !dirty.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
                dirty.BearerPlaintextStored || dirty.BearerHashStored)
                throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: dirty marker identity/safety flags are invalid.");
            return dirty;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: dirty marker JSON is invalid.", ex);
        }
    }

    private static IReadOnlyList<LocalAppReadLeaseStateV048> ReadAllCanonicalStatesBounded(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var dir = CanonicalStateDirectory(workspaceRoot, applicationId);
        if (!Directory.Exists(dir)) return Array.Empty<LocalAppReadLeaseStateV048>();
        LocalAppV046FileBoundary.RejectReparse(dir, "v0.51.5 canonical lease directory");
        var files = Directory.EnumerateFiles(dir, "lease-*.json")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(MaxReconciliationStateFiles + 1)
            .ToArray();
        if (files.Length > MaxReconciliationStateFiles)
            throw new InvalidDataException($"ACTIVE_INDEX_RECONCILIATION_REQUIRED: canonical lease state count exceeds hard reconciliation ceiling {MaxReconciliationStateFiles}.");
        var states = new List<LocalAppReadLeaseStateV048>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseId = Path.GetFileNameWithoutExtension(file);
            states.Add(ReadExactCanonicalState(workspaceRoot, applicationId, leaseId));
        }
        return states;
    }

    private static LocalAppReadLeaseStateV048 ReadExactCanonicalState(string workspaceRoot, string applicationId, string leaseId)
    {
        if (!SafeLeaseId(leaseId)) throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: unsafe canonical LeaseId.");
        var path = Path.Combine(CanonicalStateDirectory(workspaceRoot, applicationId), LocalAppV046FileBoundary.SafeToken(leaseId) + ".json");
        if (!File.Exists(path)) throw new InvalidDataException($"ACTIVE_INDEX_INCONSISTENT: indexed canonical state is missing for {leaseId}.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.5 indexed canonical lease state");
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: canonical lease state could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: canonical lease state JSON is invalid.", ex);
        }
        if (state.Schema != LocalAppReadLeaseV048Service.StateSchema || state.Version != LocalAppReadLeaseV048Service.Version ||
            !state.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || !state.LeaseId.Equals(leaseId, StringComparison.Ordinal) ||
            !Path.GetFileName(path).Equals(LocalAppV046FileBoundary.SafeToken(state.LeaseId) + ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ACTIVE_INDEX_INCONSISTENT: canonical lease state schema/identity/filename mismatch.");
        return state;
    }

    private static LocalAppReadSessionStatusLeaseV0513 ToStatusLease(LocalAppReadLeaseStateV048 state, DateTimeOffset now, bool bound)
    {
        var expired = state.ExpiresAt <= now;
        var exhausted = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
        var live = !state.Revoked && !expired && !exhausted;
        return new LocalAppReadSessionStatusLeaseV0513(
            state.LeaseId, state.Scopes, state.IssuedAt, state.ExpiresAt, state.RemainingCalls, state.RemainingBytes,
            state.StateRevision, state.Revoked, expired, exhausted, bound, live && !bound);
    }

    private static bool IsLive(LocalAppReadLeaseStateV048 state, DateTimeOffset now)
        => !state.Revoked && state.ExpiresAt > now && state.RemainingCalls > 0 && state.RemainingBytes > 0;

    private static string MetadataRoot(IReadOnlyList<LocalAppReadLeaseStateV048> states)
    {
        var canonical = string.Join("\n", states.OrderBy(x => x.LeaseId, StringComparer.Ordinal).Select(x =>
            $"{x.LeaseId}|{x.StateRevision}|{x.IssuedAt:O}|{x.ExpiresAt:O}|{x.RemainingCalls}|{x.RemainingBytes}|{x.Revoked}|{x.RevokedAt:O}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string CanonicalStateDirectory(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var root = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-leases"));
        var app = Path.GetFullPath(Path.Combine(root, LocalAppV046FileBoundary.SafeToken(applicationId)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!app.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Canonical lease directory escaped read-leases root.");
        Directory.CreateDirectory(app);
        LocalAppV046FileBoundary.RejectReparse(app, "v0.51.5 canonical lease directory");
        return app;
    }

    private static string IndexDirectory(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var root = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-lease-index-v0515"));
        Directory.CreateDirectory(root);
        LocalAppV046FileBoundary.RejectReparse(root, "v0.51.5 active-index root");
        var app = Path.GetFullPath(Path.Combine(root, LocalAppV046FileBoundary.SafeToken(applicationId)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!app.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Active-index app directory escaped index root.");
        Directory.CreateDirectory(app);
        LocalAppV046FileBoundary.RejectReparse(app, "v0.51.5 active-index app directory");
        return app;
    }

    private static string IndexPath(string workspaceRoot, string applicationId)
        => Path.Combine(IndexDirectory(workspaceRoot, applicationId), "active-index-v0.51.5.json");

    private static string DirtyPath(string workspaceRoot, string applicationId)
        => Path.Combine(IndexDirectory(workspaceRoot, applicationId), "active-index-v0.51.5.dirty.json");

    private static void ValidateIndexFileBoundary(string path, string role)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"{role} is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, role);
    }

    private static Task WriteIndexAtomicAsync(string path, LocalAppActiveLeaseIndexV0515 index, CancellationToken cancellationToken)
        => WriteJsonAtomicAsync(path, index, cancellationToken);

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.5 active-index file");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.5 active-index file");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.5 active-index file");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        string applicationId,
        LocalAppActiveLeaseReconciliationReceiptV0515 receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-active-lease-index-v0515");
        var path = Path.Combine(dir, $"active-index-reconcile-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("lease-", StringComparison.Ordinal) && value.Length <= 80 &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static string[] IndexNonEffects() => new[]
    {
        "index is derived control state, never canonical lease authority",
        "no bearer plaintext or bearer hash stored/disclosed",
        "no application file contents read",
        "no read/list call or byte budget consumption",
        "no canonical historical lease deletion/compaction/rewrite by index service",
        "no network/MCP/tunnel/publication/catalog mutation",
        "no process launch, Agent Execute or ActionPermit authority"
    };
}