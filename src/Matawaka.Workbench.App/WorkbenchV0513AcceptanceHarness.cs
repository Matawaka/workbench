using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0513AcceptanceHarness
{
    private readonly WorkbenchV0512AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0513AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0512AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppReadSessionStatusV0513Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppsActionDialogV0513.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0513ReadSessionStatusContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0513-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.3 status reads only Workbench-owned lease control state and active in-process MCP binding",
            "status omits bearer plaintext and persisted bearer hash",
            "status performs no application file-content read and consumes no read/list budget",
            "orphan closure is available only for a fresh live lease not bound to the active local MCP",
            "orphan closure reuses v0.51.2 exact-revoke primitive and never invokes revoke-all",
            "active local MCP/tunnel state and sibling leases are not changed by orphan closure",
            "no automatic revocation on startup and no automatic retry",
            "no network/publication/catalog mutation/Agent Execute/ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.3",
            "0.51.3",
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
            "Workbench v0.51.3 adds bearer-free read-session status and exact orphan closure while preserving the normal v0.51.2 bound-session lifecycle and all read/list authority boundaries.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
