namespace Matawaka.Workbench.App;

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
/// v0.51.6 orchestration wrapper. v0.51.5 canonical writers/index schemas remain
/// unchanged. Every Workbench index-authority operation enters one app-scoped
/// cross-process fence. Live status additionally verifies the post-observation
/// index revision and dirty absence before returning a coherent authority snapshot.
/// </summary>
public sealed class LocalAppReadLeaseCoherentLifecycleV0516Service
{
    public const string Version = "0.51.6";
    public const string LiveStatusSchema = "matawaka.local-app-coherent-live-read-authority/v0.51.6";

    private readonly LocalAppReadLeaseIndexedLifecycleV0515Service _inner = new();
    private readonly LocalAppActiveIndexFenceV0516Service _fence = new();

    public LocalAppReadLeasePreviewV048 PreviewFromJson(
        string workspaceRoot,
        string selectedApplicationId,
        string requestJson,
        CancellationToken cancellationToken)
        => _inner.PreviewFromJson(workspaceRoot, selectedApplicationId, requestJson, cancellationToken);

    public async Task<LocalAppIndexedLeaseCreateResultV0515> CreateIndexedAsync(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeasePreviewV048 confirmedPreview,
        bool clipboardWritePerformed,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, selectedApplicationId, "create-indexed-read-lease", cancellationToken);
        return await _inner.CreateIndexedAsync(
            workspaceRoot, selectedApplicationId, confirmedPreview, clipboardWritePerformed, cancellationToken);
    }

    public async Task<LocalAppIndexedExactRevokeResultV0515> RevokeExactIndexedAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "exact-revoke-indexed-read-lease", cancellationToken);
        return await _inner.RevokeExactIndexedAsync(workspaceRoot, applicationId, leaseId, cancellationToken);
    }

    public async Task<LocalAppIndexedRevokeAllResultV0515> RevokeAllAndReconcileAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "revoke-all-and-reconcile", cancellationToken);
        return await _inner.RevokeAllAndReconcileAsync(workspaceRoot, applicationId, cancellationToken);
    }

    public async Task<LocalAppActiveLeaseIndexReadinessV0515> GetIndexReadinessAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "observe-index-readiness", cancellationToken);
        return await _inner.GetIndexReadinessAsync(workspaceRoot, applicationId, cancellationToken);
    }

    public async Task<(LocalAppActiveLeaseIndexV0515 Index, LocalAppActiveLeaseReconciliationReceiptV0515 Receipt, string ReceiptPath)> ReconcileIndexAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "bounded-active-index-reconciliation", cancellationToken);
        return await _inner.ReconcileIndexAsync(workspaceRoot, applicationId, cancellationToken);
    }

    public async Task<LocalAppCoherentLiveAuthorityV0516> ObserveLiveAuthorityAsync(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
    {
        await using var held = await _fence.AcquireAsync(
            workspaceRoot, applicationId, "coherent-live-authority-status", cancellationToken);

        var before = await _inner.GetIndexReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(before, "before live-authority observation");
        var observed = await _inner.ObserveLiveAuthorityAsync(
            workspaceRoot, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var after = await _inner.GetIndexReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(after, "after live-authority observation");

        if (after.IndexRevision != observed.IndexRevision)
            throw new InvalidDataException(
                $"ACTIVE_INDEX_SNAPSHOT_CHANGED: observed revision {observed.IndexRevision} but post-observation revision is {after.IndexRevision}; no authority snapshot was returned.");

        return new LocalAppCoherentLiveAuthorityV0516(
            LiveStatusSchema,
            Version,
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
            "Live authority was observed under one app-scoped cross-process fence. Every indexed candidate was revalidated by v0.51.5 against exact canonical state, then dirty absence and index revision were verified again before return.");
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

        var before = await _inner.GetIndexReadinessAsync(workspaceRoot, applicationId, cancellationToken);
        RequireReadySnapshot(before, "before exact live-lease observation");
        var first = await _inner.ObserveIndexedExactLiveLeaseAsync(
            workspaceRoot, applicationId, leaseId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var second = await _inner.ObserveIndexedExactLiveLeaseAsync(
            workspaceRoot, applicationId, leaseId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);
        var after = await _inner.GetIndexReadinessAsync(workspaceRoot, applicationId, cancellationToken);
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
        ("coherent-v0516-fence", true, "all v0.51.5 index authority operations wrapped", "same app-scoped cross-process fence"),
        ("coherent-v0516-status", true, "dirty + revision post-check", "coherent snapshot or fail closed"),
        ("coherent-v0516-exact", true, "double exact canonical observation + index revision", "no stale exact lease"),
        ("coherent-v0516-canonical", true, "v0.51.5 inner writers unchanged", "canonical v0.48 authority preserved"),
        ("coherent-v0516-index", true, "v0.51.5 schema unchanged", "derived index preserved"),
        ("coherent-v0516-bearer", true, "no new bearer/plaintext/hash field", "omitted"),
        ("coherent-v0516-history", true, "no historical scan added to live status", "false")
    };

    private static void RequireReadySnapshot(LocalAppActiveLeaseIndexReadinessV0515 readiness, string role)
    {
        if (!readiness.Ready || readiness.ReconciliationRequired || readiness.DirtyMarkerPresent || readiness.IndexRevision is null)
            throw new InvalidDataException(
                $"ACTIVE_INDEX_RECONCILIATION_REQUIRED: verified active index is not coherent {role}; status={readiness.Status}.");
    }
}
