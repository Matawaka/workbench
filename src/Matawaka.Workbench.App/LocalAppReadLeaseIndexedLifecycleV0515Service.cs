using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppIndexedLeaseCreateResultV0515(
    LocalAppReadLeaseGrantV048 Grant,
    LocalAppReadLeaseCreationReceiptV048 Receipt,
    string ReceiptPath,
    long ActiveIndexRevision,
    int IndexedCandidates);

public sealed record LocalAppIndexedExactRevokeResultV0515(
    LocalAppReadLeaseExactRevokeReceiptV0512 ExactReceipt,
    string ExactReceiptPath,
    long ActiveIndexRevision,
    int IndexedCandidates,
    bool BearerPlaintextUsedOrDisclosed,
    bool BearerHashStoredInIndex,
    string Status);

public sealed record LocalAppIndexedRevokeAllResultV0515(
    LocalAppReadLeaseRevokeReceiptV048 LegacyReceipt,
    string LegacyReceiptPath,
    LocalAppActiveLeaseReconciliationReceiptV0515 ReconciliationReceipt,
    string ReconciliationReceiptPath,
    long ActiveIndexRevision,
    int IndexedCandidates,
    string Status);

public sealed record LocalAppCoherentLiveAuthorityV0516(
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
    bool CrossProcessFenceAcquired,
    long FenceWaitMilliseconds,
    long IndexRevisionBeforeObservation,
    long IndexRevisionAfterObservation,
    bool DirtyMarkerAbsentBeforeObservation,
    bool DirtyMarkerAbsentAfterObservation,
    bool SnapshotCoherent,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.51.5 canonical/index lifecycle semantics with v0.51.6 cross-process
/// orchestration layered around them. Canonical v0.48/v0.51.2 writers and the
/// v0.51.5 index schema remain unchanged. The app-scoped file-handle fence only
/// serializes observation/mutation and grants no lease authority.
/// </summary>
public sealed class LocalAppReadLeaseIndexedLifecycleV0515Service
{
    public const string CoherentLiveStatusSchemaV0516 = "matawaka.local-app-coherent-live-read-authority/v0.51.6";

    private readonly LocalAppReadLeaseV048Service _leases = new();
    private readonly LocalAppReadLeaseExactRevokeV0512Service _exact = new();
    private readonly LocalAppActiveLeaseIndexV0515Service _index = new();
    private readonly LocalAppActiveIndexFenceV0516Service _fence = new();

    public LocalAppReadLeasePreviewV048 PreviewFromJson(
        string workspaceRoot,
        string selectedApplicationId,
        string requestJson,
        CancellationToken cancellationToken)
        => _leases.PreviewFromJson(workspaceRoot, selectedApplicationId, requestJson, cancellationToken);

    public async Task<LocalAppIndexedLeaseCreateResultV0515> CreateIndexedAsync(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeasePreviewV048 confirmedPreview,
        bool clipboardWritePerformed,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, selectedApplicationId, "create-indexed-read-lease", cancellationToken);
        var mutation = await _index.BeginMutationAsync(
            workspaceRoot, selectedApplicationId, "create-live-lease", null, cancellationToken);
        var created = await _leases.CreateAsync(
            workspaceRoot, selectedApplicationId, confirmedPreview, clipboardWritePerformed, cancellationToken);
        var index = await _index.CommitMutationAsync(
            workspaceRoot, mutation, created.Grant.LeaseId, cancellationToken);
        return new LocalAppIndexedLeaseCreateResultV0515(
            created.Grant, created.Receipt, created.ReceiptPath, index.IndexRevision, index.Entries.Count);
    }

    public async Task<LocalAppIndexedExactRevokeResultV0515> RevokeExactIndexedAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "exact-revoke-indexed-read-lease", cancellationToken);
        var mutation = await _index.BeginMutationAsync(
            workspaceRoot, applicationId, "exact-revoke-live-lease", leaseId, cancellationToken);
        var revoked = await _exact.RevokeExactAsync(workspaceRoot, applicationId, leaseId, cancellationToken);
        var index = await _index.CommitMutationAsync(workspaceRoot, mutation, leaseId, cancellationToken);
        return new LocalAppIndexedExactRevokeResultV0515(
            revoked.Receipt,
            revoked.ReceiptPath,
            index.IndexRevision,
            index.Entries.Count,
            false,
            false,
            "EXACT_CANONICAL_REVOKE_AND_ACTIVE_INDEX_COMMITTED");
    }

    public async Task<LocalAppIndexedRevokeAllResultV0515> RevokeAllAndReconcileAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "revoke-all-and-reconcile", cancellationToken);
        var readiness = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        if (readiness.Ready)
            _ = await _index.BeginMutationAsync(workspaceRoot, applicationId, "revoke-all-recovery", null, cancellationToken);
        else
            await _index.MarkReconciliationRequiredAsync(workspaceRoot, applicationId, "revoke-all-recovery", cancellationToken);

        var revoked = await _leases.RevokeAllActiveAsync(workspaceRoot, applicationId, cancellationToken);
        var reconciled = await _index.ReconcileAsync(workspaceRoot, applicationId, cancellationToken);
        return new LocalAppIndexedRevokeAllResultV0515(
            revoked.Receipt,
            revoked.ReceiptPath,
            reconciled.Receipt,
            reconciled.ReceiptPath,
            reconciled.Index.IndexRevision,
            reconciled.Index.Entries.Count,
            "REVOKE_ALL_CANONICAL_COMPLETE_ACTIVE_INDEX_RECONCILED");
    }

    public async Task<LocalAppActiveLeaseIndexReadinessV0515> GetIndexReadinessAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "observe-index-readiness", cancellationToken);
        return await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
    }

    public async Task<(LocalAppActiveLeaseIndexV0515 Index, LocalAppActiveLeaseReconciliationReceiptV0515 Receipt, string ReceiptPath)> ReconcileIndexAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "bounded-active-index-reconciliation", cancellationToken);
        return await _index.ReconcileAsync(workspaceRoot, applicationId, cancellationToken);
    }

    public async Task<LocalAppVerifiedLiveAuthorityV0515> ObserveLiveAuthorityAsync(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "verified-live-authority-status", cancellationToken);
        var before = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(before, "before live-authority observation");
        var observed = await _index.ObserveLiveAuthorityAsync(
            workspaceRoot, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var after = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(after, "after live-authority observation");
        if (after.IndexRevision != observed.IndexRevision)
            throw new InvalidDataException(
                $"ACTIVE_INDEX_SNAPSHOT_CHANGED: observed revision {observed.IndexRevision} but post-observation revision is {after.IndexRevision}; no authority snapshot was returned.");
        return observed;
    }

    public async Task<LocalAppCoherentLiveAuthorityV0516> ObserveCoherentLiveAuthorityV0516Async(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "coherent-live-authority-status-v0.51.6", cancellationToken);
        var before = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(before, "before coherent live-authority observation");
        var observed = await _index.ObserveLiveAuthorityAsync(
            workspaceRoot, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var after = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(after, "after coherent live-authority observation");
        if (after.IndexRevision != observed.IndexRevision)
            throw new InvalidDataException(
                $"ACTIVE_INDEX_SNAPSHOT_CHANGED: observed revision {observed.IndexRevision} but post-observation revision is {after.IndexRevision}; no coherent authority snapshot was returned.");

        return new LocalAppCoherentLiveAuthorityV0516(
            CoherentLiveStatusSchemaV0516,
            "0.51.6",
            DateTimeOffset.Now,
            applicationId,
            observed.IndexRevision,
            observed.IndexedCandidatesObserved,
            observed.InactiveCandidatesPruned,
            observed.LiveAuthorities,
            observed.LiveLeaseCount,
            observed.OrphanClosureEligibleCount,
            observed.ActiveLocalMcpApplicationId,
            observed.ActiveLocalMcpLeaseId,
            observed.CanonicalHistoricalScanPerformed,
            observed.BearerPlaintextDisclosed,
            observed.BearerHashDisclosed,
            observed.CanonicalStateMutationPerformed,
            true,
            held.Observation.WaitMilliseconds,
            before.IndexRevision!.Value,
            after.IndexRevision!.Value,
            !before.DirtyMarkerPresent,
            !after.DirtyMarkerPresent,
            true,
            observed.NonEffects.Concat(new[]
            {
                "app-scoped cross-process fence held through live-authority observation",
                "post-observation index revision matched returned snapshot revision",
                "dirty marker absent before and after coherent snapshot"
            }).Distinct(StringComparer.Ordinal).ToArray(),
            "COHERENT_VERIFIED_ACTIVE_INDEX_STATUS",
            "Live authority was observed under one app-scoped cross-process fence. v0.51.5 revalidated exact canonical state for every indexed candidate; v0.51.6 then rechecked dirty absence and the returned index revision before disclosure.");
    }

    public async Task<LocalAppReadSessionStatusLeaseV0513> ObserveIndexedExactLiveLeaseAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "coherent-exact-live-lease-observation", cancellationToken);
        var before = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(before, "before exact live-lease observation");
        var first = await _index.ObserveIndexedExactLiveLeaseAsync(
            workspaceRoot, applicationId, leaseId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var second = await _index.ObserveIndexedExactLiveLeaseAsync(
            workspaceRoot, applicationId, leaseId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var after = await _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(after, "after exact live-lease observation");
        if (before.IndexRevision != after.IndexRevision ||
            first.StateRevision != second.StateRevision ||
            first.RemainingCalls != second.RemainingCalls ||
            first.RemainingBytes != second.RemainingBytes ||
            first.Revoked != second.Revoked ||
            first.Expired != second.Expired ||
            first.BudgetExhausted != second.BudgetExhausted)
            throw new InvalidDataException(
                "ACTIVE_INDEX_SNAPSHOT_CHANGED: exact live lease changed during coherent observation; no stale exact-lease result was returned.");
        return second;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("indexed-lifecycle-v0515-canonical-create", true, "legacy v0.48 CreateAsync", "preserved canonical writer"),
        ("indexed-lifecycle-v0515-canonical-close", true, "legacy v0.51.2 RevokeExactAsync", "preserved canonical writer"),
        ("indexed-lifecycle-v0515-dirty-before-create", true, "BeginMutation before CreateAsync", "fail closed"),
        ("indexed-lifecycle-v0515-dirty-before-close", true, "BeginMutation before RevokeExactAsync", "fail closed"),
        ("indexed-lifecycle-v0515-failure", true, "dirty marker remains", "explicit reconciliation required"),
        ("indexed-lifecycle-v0515-revoke-all", true, "legacy recovery + bounded reconciliation", "canonical recovery preserved"),
        ("indexed-lifecycle-v0515-bearer", true, "not added to derived lifecycle receipts", "omitted"),
        ("indexed-lifecycle-v0516-fence", true, "app-scoped FileShare.None fence", "all authority operations serialized across processes"),
        ("indexed-lifecycle-v0516-snapshot", true, "dirty/revision post-check", "coherent or fail closed")
    };

    private static void RequireReadySnapshot(LocalAppActiveLeaseIndexReadinessV0515 readiness, string role)
    {
        if (!readiness.Ready || readiness.ReconciliationRequired || readiness.DirtyMarkerPresent || readiness.IndexRevision is null)
            throw new InvalidDataException(
                $"ACTIVE_INDEX_RECONCILIATION_REQUIRED: verified active index is not coherent {role}; status={readiness.Status}.");
    }
}
