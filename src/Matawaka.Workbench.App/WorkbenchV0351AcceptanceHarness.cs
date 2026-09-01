using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.35.1 stabilization acceptance successor. Reuses the complete accepted
/// v0.35 matrix and adds only deterministic offline checks for lifecycle v2
/// tag/schema-token vs semantic-Version normalization. No lifecycle artifacts
/// are read or written by Self-test.
/// </summary>
public sealed class WorkbenchV0351AcceptanceHarness
{
    private readonly WorkbenchV035AcceptanceHarness _predecessor;

    public WorkbenchV0351AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV035AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var lifecycleChecks = MaintenanceLifecycleReceiptV2Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(lifecycleChecks).ToArray();
        var passed = predecessor.Passed && lifecycleChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.35.1 Self-test performs no lifecycle artifact discovery or write",
            "v0.35.1 Self-test performs no local-app update",
            "v0.35.1 Self-test performs no source/checkpoint/publication effect",
            "lifecycle version-key normalization creates no trust or authority",
            "tag/schema token normalization does not rewrite existing receipts or tags"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.35.1",
            "0.35.1",
            predecessor.RunId,
            DateTimeOffset.Now,
            passed,
            predecessor.AppExecutableSha256,
            predecessor.ProviderA,
            predecessor.ProviderB,
            predecessor.ExecuteTerminalState,
            predecessor.ExecuteProgressEvents,
            checks,
            nonEffects,
            "Workbench-local acceptance automation v0.35.1 reuses the accepted v0.35 matrix and adds only offline lifecycle v2 normalization regression checks. Accepted tag/schema token and semantic runtime Version are bound explicitly rather than assumed equal. No lifecycle/local-app/network/Git/Agent Execute effect is exercised by Self-test.");
    }
}
