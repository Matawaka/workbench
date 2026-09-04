using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0516AcceptanceHarness
{
    private readonly WorkbenchV0515AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0516AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0515AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppActiveIndexFenceV0516Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppReadLeaseIndexedLifecycleV0515Service.RunOfflineContractChecks()
            .Where(x => x.Id.StartsWith("indexed-lifecycle-v0516-", StringComparison.Ordinal)).ToArray());
        Add(successorChecks, _window.ObserveV0516FenceContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0516-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.6 adds app-scoped cross-process serialization only; canonical v0.48 lease state remains authority source of truth",
            "v0.51.5 active index schema and dirty/reconciliation semantics remain unchanged and derived only",
            "fence ownership is the exclusive open file handle; persistent lock file contents grant no authority and remain secret-free",
            "same-app fence acquisition is bounded and fails ACTIVE_INDEX_FENCE_BUSY without partial authority disclosure or mutation",
            "coherent fast status verifies dirty absence and returned index revision again before disclosure",
            "process termination releases OS fence ownership but does not clear durable v0.51.5 dirty crash-gap evidence",
            "different ApplicationIds use independent fence paths and do not serialize each other",
            "no historical evidence deletion/compaction and no historical canonical scan added to fast status",
            "no automatic lease creation/renewal/revocation, no read/list budget consumption by fence/status",
            "no automatic MCP/tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.6",
            "0.51.6",
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
            "Workbench v0.51.6 serializes active-index authority operations across Workbench processes and returns live-authority status only after a coherent fence/revision/dirty snapshot proof.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
