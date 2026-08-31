using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.34 acceptance successor. It preserves the complete accepted v0.33
/// read-only semantic/runtime, orchestrator and publisher contract matrix and
/// adds deterministic offline checks for lifecycle evidence composition only.
/// Self-test does not require or synthesize real v0.34 lifecycle artifacts.
/// </summary>
public sealed class WorkbenchV034AcceptanceHarness
{
    private readonly WorkbenchV033AcceptanceHarness _predecessor;

    public WorkbenchV034AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV033AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var lifecycleChecks = MaintenanceLifecycleReceiptService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(lifecycleChecks).ToArray();
        var passed = predecessor.Passed && lifecycleChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.34 Self-test does not scan real lifecycle artifact directories",
            "v0.34 Self-test does not invoke Update candidate or build",
            "v0.34 Self-test does not launch a candidate",
            "v0.34 Self-test does not create a checkpoint or accepted tag",
            "v0.34 Self-test does not perform remote publication",
            "v0.34 Self-test does not write a lifecycle receipt",
            "lifecycle offline checks create no Agent Execute, ActionPermit, retry, rollback or network authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.34",
            "0.34.0",
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
            "Workbench-local acceptance automation v0.34 preserves the complete accepted v0.33 read-only matrix and adds only deterministic offline checks for Maintenance Lifecycle Receipt semantics. In particular, missing/ambiguous evidence must fail closed and lifecycle summary remains non-authorizing. Self-test performs no real lifecycle scan/write, update/build/launch/checkpoint/publication effect, Agent Execute, ActionPermit, catalog mutation, general network authority, canonical UU-AAP conformance or Stable Core promotion.");
    }
}
