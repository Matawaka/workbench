using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV044AcceptanceHarness
{
    private readonly WorkbenchV043AcceptanceHarness _predecessor;

    public WorkbenchV044AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV043AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var textChecks = WorkbenchAppTextV044Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV044Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV044Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var successorChecks = textChecks.Concat(checkpointChecks).Concat(publicationChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.44 file double-click reads only a bounded explicitly selected managed-app text file",
            "text target remains registered-app/tree/path/reparse gated and is limited to 2 MiB",
            "text tabs are read-only and create no application write/execute authority",
            "dynamic tree/text close buttons mutate presentation only",
            "fixed Workbench tabs remain stable non-closable surfaces",
            "reopening the same app/file is expected to refresh/select rather than duplicate its dynamic tab",
            "accepted v0.43 app-tree observation and v0.42 five-button shell remain predecessor behavior",
            "accepted v0.41.2 search/focus presentation remains predecessor behavior for direct TextBox tabs",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect from text inspection"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.44",
            "0.44.0",
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
            "Workbench v0.44 preserves accepted v0.43 clickable application trees and adds only explicit double-click bounded read-only text inspection plus closable dynamic inspection tabs. File reading is path/registration/reparse/size/encoding bounded and creates no application mutation or execution authority.");
    }
}
