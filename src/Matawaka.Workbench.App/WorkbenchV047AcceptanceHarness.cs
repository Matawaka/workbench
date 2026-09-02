using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV047AcceptanceHarness
{
    private readonly WorkbenchV046AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV047AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV046AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppsActionDialogV047.RunOfflineContractChecks());
        Add(successorChecks, LocalAppChatReadRequestDialogV047.RunOfflineContractChecks());
        Add(successorChecks, LocalAppChatReadRelayV047Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV047Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV047Service.RunOfflineContractChecks());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v047-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.47 adds Chat read relay only inside the registered-app chooser; the top-level Workbench surface remains four buttons",
            "a pasted chat request creates no local read or clipboard authority by itself",
            "preview resolves fixed root/path and hashes metadata without reading/disclosing file contents",
            "explicit confirmation is required before the bounded v0.46 read primitive is invoked",
            "file SHA/size/range are revalidated after confirmation and stale previews fail closed",
            "the exact response is copied only to the local Windows clipboard; Workbench performs no upload/network/listener/tunnel/MCP exposure",
            "v0.46 launch/source/update/private-context/app inspection behavior remains predecessor behavior",
            "no application/source mutation, process execution through chat read, catalog mutation, Agent Execute, ActionPermit, Stable Core or canonical UU-AAP promotion"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.47",
            "0.47.0",
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
            "Workbench v0.47 adds a human-gated transport-neutral chat read relay over the accepted bounded local read primitive: exact request -> metadata preview -> explicit disclosure confirmation -> stale revalidation -> local clipboard response, with no automatic network transport.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
