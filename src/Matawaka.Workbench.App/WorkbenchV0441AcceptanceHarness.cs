using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0441AcceptanceHarness
{
    private readonly WorkbenchV044AcceptanceHarness _predecessor;

    public WorkbenchV0441AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV044AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var routingChecks = TreeViewItemRoutingV0441Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV0441Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV0441Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var shellChecks = new[]
        {
            new WorkbenchAcceptanceCheck("shell-v0441-visible-maintenance-buttons", true, "4", "4"),
            new WorkbenchAcceptanceCheck("shell-v0441-launch-candidate-visible", true, "false", "false"),
            new WorkbenchAcceptanceCheck("shell-v0441-launch-compatibility-binding-hidden", true, "true", "true")
        };
        var successorChecks = routingChecks.Concat(checkpointChecks).Concat(publicationChecks).Concat(shellChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.44 real-host double-click result remains negative evidence and is not reclassified as a passed published frontier",
            "v0.44.1 changes nested TreeView routed-item resolution without weakening v0.44 text-read bounds",
            "dynamic tree/text close behavior remains presentation-only",
            "Launch candidate is absent from the visible operator surface; hidden compatibility binding creates no launch authority",
            "visible maintenance surface contains four functional actions",
            "local v0.44.1 checkpoint is bound to exact operator-provided predecessor fbce2c3d20517e99e0752fe5ac53c5cc30f0a2af",
            "remote publication remains explicit and keeps workbench-v0.44-accepted absent remotely",
            "no automatic Publish or Lifecycle authority",
            "no app write/execute/network/catalog/Agent Execute authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.44.1",
            "0.44.1",
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
            "Workbench v0.44.1 is a narrow stabilization successor: nearest nested TreeViewItem routing for real file double-click, the accepted v0.44 bounded read-only text/closable-tab behavior, and removal of the obsolete visible Launch candidate button. The failed v0.44 real-host result remains preserved as negative evidence.");
    }
}
