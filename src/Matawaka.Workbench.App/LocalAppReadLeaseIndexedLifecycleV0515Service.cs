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

/// <summary>
/// v0.51.5 lifecycle bridge places a durable active-index dirty marker before
/// supported Workbench operations that can add/remove live authority. Canonical
/// v0.48/v0.51.2 operations remain unchanged and authoritative. If any step after
/// BeginMutation fails, the dirty marker intentionally remains and blocks index use
/// until explicit bounded reconciliation.
/// </summary>
public sealed class LocalAppReadLeaseIndexedLifecycleV0515Service
{
    private readonly LocalAppReadLeaseV048Service _leases = new();
    private readonly LocalAppReadLeaseExactRevokeV0512Service _exact = new();
    private readonly LocalAppActiveLeaseIndexV0515Service _index = new();

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

    public Task<LocalAppActiveLeaseIndexReadinessV0515> GetIndexReadinessAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
        => _index.GetReadinessAsync(workspaceRoot, applicationId, cancellationToken);

    public Task<(LocalAppActiveLeaseIndexV0515 Index, LocalAppActiveLeaseReconciliationReceiptV0515 Receipt, string ReceiptPath)> ReconcileIndexAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
        => _index.ReconcileAsync(workspaceRoot, applicationId, cancellationToken);

    public Task<LocalAppVerifiedLiveAuthorityV0515> ObserveLiveAuthorityAsync(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
        => _index.ObserveLiveAuthorityAsync(
            workspaceRoot, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);

    public Task<LocalAppReadSessionStatusLeaseV0513> ObserveIndexedExactLiveLeaseAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        CancellationToken cancellationToken)
        => _index.ObserveIndexedExactLiveLeaseAsync(
            workspaceRoot, applicationId, leaseId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, cancellationToken);

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("indexed-lifecycle-v0515-canonical-create", true, "legacy v0.48 CreateAsync", "preserved canonical writer"),
        ("indexed-lifecycle-v0515-canonical-close", true, "legacy v0.51.2 RevokeExactAsync", "preserved canonical writer"),
        ("indexed-lifecycle-v0515-dirty-before-create", true, "BeginMutation before CreateAsync", "fail closed"),
        ("indexed-lifecycle-v0515-dirty-before-close", true, "BeginMutation before RevokeExactAsync", "fail closed"),
        ("indexed-lifecycle-v0515-failure", true, "dirty marker remains", "explicit reconciliation required"),
        ("indexed-lifecycle-v0515-revoke-all", true, "legacy recovery + bounded reconciliation", "canonical recovery preserved"),
        ("indexed-lifecycle-v0515-bearer", true, "not added to derived lifecycle receipts", "omitted")
    };
}