using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.35 acceptance successor. Reuses the complete accepted v0.34.1 matrix and
/// adds deterministic offline checks for bounded local-application maintenance.
/// Self-test performs no local-app update, network effect, launch, checkpoint or publication.
/// </summary>
public sealed class WorkbenchV035AcceptanceHarness
{
    private readonly WorkbenchV0341AcceptanceHarness _predecessor;

    public WorkbenchV035AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0341AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var appChecks = LocalApplicationMaintenanceService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(appChecks).ToArray();
        var passed = predecessor.Passed && appChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.35 Self-test does not open or apply a real local-app update package",
            "v0.35 Self-test does not mutate <WorkspaceRoot>/Apps",
            "v0.35 Self-test does not launch an application or installer",
            "v0.35 Self-test does not perform git fetch/push or other network access",
            "v0.35 Self-test does not create a checkpoint, publication or lifecycle receipt",
            "local-app offline checks create no filesystem/network/process/Agent Execute authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.35",
            "0.35.0",
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
            "Workbench-local acceptance automation v0.35 preserves the accepted v0.34.1 semantic/runtime/orchestrator/publisher/lifecycle qualification matrix and adds only deterministic offline local-app maintenance contract checks. Self-test performs no local-app mutation, package installation, application launch, installer execution, Git/network/catalog effect, Agent Execute, ActionPermit, canonical UU-AAP conformance or Stable Core promotion.");
    }
}
