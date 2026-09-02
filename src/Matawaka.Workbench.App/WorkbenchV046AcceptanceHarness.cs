using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV046AcceptanceHarness
{
    private readonly WorkbenchV045AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV046AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV045AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppsActionDialogV046.RunOfflineContractChecks());
        Add(successorChecks, LocalAppLaunchV046Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppUpdateContextV046Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppSourceBindingV046Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppPrivateContextV046Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppReadToolV046Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV046Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV046Service.RunOfflineContractChecks());

        var observedSurface = OperatorSurfaceV045Contract.Observe(_window);
        successorChecks.AddRange(observedSurface.Select(x => new WorkbenchAcceptanceCheck(
            "v046-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.46 adds explicit local-app operational actions only inside the existing Local apps chooser; top-level surface remains four buttons",
            "Launch app requires a separately selected exact registered-root EXE and explicit confirmation; no automatic launch is created",
            "Export update context contains paths/SHA-256/sizes only and no application file contents",
            "development source binding creates one .matawaka-source.json under fixed Workspace/AppSources/<ApplicationId> and imports no bytes",
            "PRIVATE development context export is local-only and performs no upload/network/publication",
            "local-app read primitive is fixed-root/bounded and has no external connector or network transport in v0.46",
            "private application/source bytes remain outside Workbench Git checkpoint/publication",
            "no catalog mutation, Agent Execute, ActionPermit, Stable Core or canonical UU-AAP promotion"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.46",
            "0.46.0",
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
            "Workbench v0.46 adds explicit registered-app launch, content-free sparse-update context, fixed-root development source binding, local-only PRIVATE development context export, and a reusable bounded content-read primitive without external transport authority.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
