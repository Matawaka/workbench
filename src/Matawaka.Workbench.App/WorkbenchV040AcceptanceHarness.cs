using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV040AcceptanceHarness
{
    private readonly WorkbenchV0391AcceptanceHarness _predecessor;

    public WorkbenchV040AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0391AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var bootstrapChecks = TransitionBootstrapV040Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(bootstrapChecks).ToArray();
        var passed = predecessor.Passed && bootstrapChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.40 Self-test itself creates no transition bootstrap lease",
            "v0.40 Self-test itself launches no candidate and closes no predecessor window",
            "v0.40 Self-test PASS alone does not create Publish or Lifecycle authority",
            "automatic local Accept is possible only through a separately validated one-shot first-boot lease",
            "no automatic retry authority is created by Self-test failure",
            "no network/Git remote/catalog/Agent Execute effect from Self-test"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.40",
            "0.40.0",
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
            "Workbench v0.40 reuses the full accepted v0.39.1 acceptance matrix and adds one-shot transition-bootstrap contract checks. The same harness is used for manual Self-test and first-boot bootstrap Self-test; only the separately verified lease controls whether the latter may be invoked automatically.");
    }
}
