using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV043AcceptanceHarness
{
    private readonly WorkbenchV042AcceptanceHarness _predecessor;

    public WorkbenchV043AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV042AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var treeChecks = WorkbenchAppTreeV043Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV043Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV043Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var successorChecks = treeChecks.Concat(checkpointChecks).Concat(publicationChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.43 app chips open read-only structural tabs only",
            "app tree observation is limited to already-registered direct managed applications",
            "tree observation reads names/type/size metadata and does not read application file contents",
            "reparse paths are not traversed and tree depth/node count are bounded",
            "opening or refreshing an app tab creates no registration/update/copy/move/delete/launch authority",
            "accepted v0.42 five-button shell, hidden paths, status/progress and v0.41.2 search/focus behavior remain predecessor behavior",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect from app tree observation"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.43",
            "0.43.0",
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
            "Workbench v0.43 preserves the accepted v0.42 compact operator shell and adds only clickable registered-app entry points plus bounded read-only directory-tree tabs. Each app opens/selects its own tab; structural inspection reads metadata only and creates no application authority.");
    }
}
