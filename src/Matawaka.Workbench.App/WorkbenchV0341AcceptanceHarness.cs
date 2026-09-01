using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// Patch-level qualification successor over accepted v0.34. It preserves the
/// complete v0.34 read-only matrix and adds only offline checks that the
/// lifecycle evidence adapter is version/tag generic and remains fail-closed.
/// </summary>
public sealed class WorkbenchV0341AcceptanceHarness
{
    private readonly WorkbenchV034AcceptanceHarness _predecessor;

    public WorkbenchV0341AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV034AcceptanceHarness(
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
            "v0.34.1 Self-test does not inspect real lifecycle artifact directories",
            "v0.34.1 Self-test does not derive runtime authority from accepted-tag parsing",
            "v0.34.1 Self-test does not invoke update/build/launch/checkpoint/publication",
            "v0.34.1 Self-test does not write a lifecycle receipt",
            "generic lifecycle adapter checks create no trust, Agent Execute, ActionPermit, retry, rollback or network authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.34.1",
            "0.34.1",
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
            "Workbench-local qualification/stabilization acceptance v0.34.1 preserves the complete accepted v0.34 read-only matrix and adds only offline checks for successor-generic lifecycle evidence routing. Accepted tag parsing and dynamic schema construction route evidence only; they do not discover trust or authority. Self-test performs no update/build/launch/checkpoint/publication/lifecycle effect, Agent Execute, ActionPermit, catalog mutation, general network authority, canonical UU-AAP conformance or Stable Core promotion.");
    }
}
