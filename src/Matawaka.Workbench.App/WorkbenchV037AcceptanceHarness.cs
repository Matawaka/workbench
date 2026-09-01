using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.37 acceptance successor. Reuses the complete accepted v0.36 matrix and
/// adds deterministic offline checks for the local-app package builder contract.
/// Self-test writes no package and performs no application mutation.
/// </summary>
public sealed class WorkbenchV037AcceptanceHarness
{
    private readonly WorkbenchV036AcceptanceHarness _predecessor;

    public WorkbenchV037AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV036AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var builderChecks = LocalApplicationPackageBuilderService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(builderChecks).ToArray();
        var passed = predecessor.Passed && builderChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.37 Self-test does not write a real local-app update package",
            "v0.37 Self-test does not mutate Apps or AppCandidates",
            "v0.37 Self-test does not invoke local-app update Apply",
            "builder offline checks create no package-write/update/launch authority",
            "package-builder qualification does not broaden registered-app roots"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.37",
            "0.37.0",
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
            "Workbench-local acceptance automation v0.37 reuses the accepted v0.36 matrix and adds only deterministic offline local-app package-builder checks. Self-test creates no real package, performs no app registration/update/launch, network/Git/installer effect, Agent Execute or ActionPermit, and makes no Stable Core/canonical conformance claim.");
    }
}
