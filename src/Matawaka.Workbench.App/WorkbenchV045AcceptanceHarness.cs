using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV045AcceptanceHarness
{
    private readonly WorkbenchV0441AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV045AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0441AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var surfaceChecks = OperatorSurfaceV045Contract.Observe(_window)
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var offlineChecks = OperatorSurfaceV045Contract.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();

        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = surfaceChecks.Concat(offlineChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(check => check.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.45 quarantines legacy WPF compatibility controls without deleting historical source/evidence",
            "retired Self-test/Accept/Stop/Launch candidate controls are not active operator actions",
            "legacy Agent/git-fetch checkboxes remain unchecked and disabled",
            "Workspace/Catalog state values remain internally available to existing bounded maintenance services",
            "the active operator surface remains exactly Update Workbench / Local apps / Publish accepted / Lifecycle receipt",
            "accepted v0.44.1 app tree, text inspection, close-tab and search behavior remain predecessor behavior",
            "no new application mutation, process, network, catalog, Agent Execute or publication authority",
            "Publish accepted and Lifecycle receipt remain separate explicit actions"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.45",
            OperatorSurfaceV045Contract.Version,
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
            "Workbench v0.45 reconciles the active four-button product surface with a runtime-enforced legacy-control quarantine. It preserves hidden state needed by existing bounded services and creates no new authority.");
    }
}
