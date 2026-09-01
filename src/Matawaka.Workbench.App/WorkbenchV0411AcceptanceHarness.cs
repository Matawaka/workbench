using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0411AcceptanceHarness
{
    private readonly WorkbenchV041AcceptanceHarness _predecessor;

    public WorkbenchV0411AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV041AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var presentationChecks = JsonSearchPresentationV0411Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(presentationChecks).ToArray();
        var passed = predecessor.Passed && presentationChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.41.1 search presentation adapter does not mutate output text",
            "v0.41.1 does not modify accepted v0.41 pure search algorithm semantics",
            "visible inactive selection creates no clipboard/file/receipt authority",
            "no Local Apps import/copy/move authority",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.41.1",
            "0.41.1",
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
            "Workbench v0.41.1 preserves the complete accepted v0.41 search/handoff matrix and adds only visible-selection presentation checks. Pure search remains read-only and unchanged; the patch ensures a selected match stays visually represented after focus returns to the search box.");
    }
}
