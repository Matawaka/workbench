using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.33 acceptance successor. It preserves the complete v0.32 read-only
/// semantic/runtime + fixed-publisher contract matrix and adds only offline
/// Maintenance Update Orchestrator + v0.33 publisher successor contract checks.
/// Self-test never selects a package, materializes, mutates source, builds,
/// launches, checkpoints or publishes.
/// </summary>
public sealed class WorkbenchV033AcceptanceHarness
{
    private readonly WorkbenchV032AcceptanceHarness _predecessor;

    public WorkbenchV033AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV032AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var orchestratorChecks = MaintenanceUpdateOrchestratorService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publisherChecks = FixedGitHubPublicationV033Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(orchestratorChecks).Concat(publisherChecks).ToArray();
        var passed = predecessor.Passed &&
                     orchestratorChecks.All(item => item.Passed) &&
                     publisherChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.33 Self-test does not open or inspect a candidate update package",
            "v0.33 Self-test performs no staging materialization",
            "v0.33 Self-test performs no tracked source mutation or build",
            "v0.33 Self-test performs no candidate launch",
            "v0.33 Self-test performs no checkpoint or accepted-tag creation",
            "v0.33 Self-test performs no remote publication or remote readback",
            "v0.33 publisher successor checks are deterministic and network-free",
            "orchestrator/publisher contract validation does not create Agent Execute or ActionPermit authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.33",
            "0.33.0",
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
            "Workbench-local acceptance automation v0.33 preserves the complete accepted v0.32 semantic/runtime and fixed-publication contract matrix, then adds deterministic offline checks that Maintenance Update Orchestrator is sequencing/UX composition over the existing typed intake/materialize/staged-plan/apply-build gates and stops before candidate launch, plus exact v0.33 fixed-remote/tag publisher successor checks. Self-test performs no update effect, launch, acceptance or publication and grants no Agent Execute, ActionPermit, catalog mutation, general network authority, canonical UU-AAP conformance or Stable Core membership.");
    }
}
