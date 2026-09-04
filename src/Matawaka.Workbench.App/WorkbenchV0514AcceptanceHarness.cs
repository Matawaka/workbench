using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0514AcceptanceHarness
{
    private readonly WorkbenchV0513AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0514AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0513AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppReadSessionStatusV0514Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppHistoryPageChooserV0514.RunOfflineContractChecks());
        Add(successorChecks, LocalAppsActionDialogV0514.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0514BoundedStatusContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0514-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.4 bounds only status representation; it does not delete, compact, archive or reinterpret persisted lease evidence",
            "all live read authority is represented in full up to the fixed hard ceiling; overflow is explicit and fail-closed",
            "historical lease evidence is paginated newest-first with default 16 and hard max 64 records per page",
            "v0.51.4 intentionally preserves the v0.51.3 full classification scan; durable active-index optimization is a separate future layer",
            "status and pagination omit bearer plaintext/hash and consume no read/list budget",
            "exact orphan closure remains exact-LeaseId and is independent of historical pagination",
            "no automatic revocation, retry, MCP/tunnel start, network access or remote publication",
            "no application/source/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.4",
            "0.51.4",
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
            "Workbench v0.51.4 bounds the operator-facing Read Session Status without destroying evidence or silently hiding live authority. Historical pagination is representational only; exact orphan closure remains pagination-independent.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
