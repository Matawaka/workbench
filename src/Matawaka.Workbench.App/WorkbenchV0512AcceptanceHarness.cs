using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0512AcceptanceHarness
{
    private readonly WorkbenchV0511AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0512AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0511AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppReadLeaseExactRevokeV0512Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppsActionDialogV0512.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0512EndSessionContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0512-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.2 adds only an explicit local session-closure orchestration and exact bound lease revoke receipt",
            "read_local_app_chunk and list_local_app_entries runtime semantics remain inherited unchanged",
            "End Read Session stops the local MCP adapter before exact lease-state revocation",
            "exact closure addresses only the bound ApplicationId/LeaseId state and performs no sibling lease enumeration",
            "existing revoke-all remains a separate recovery action and is not invoked automatically",
            "Secure MCP Tunnel remains separate and must already be stopped before local End Read Session",
            "no automatic retry, network access, remote publication, catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.2",
            "0.51.2",
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
            "Workbench v0.51.2 makes the local lease-gated read lifecycle symmetric: v0.51.1 starts lease + MCP in one explicit action, while v0.51.2 ends the bound session by stopping MCP and revoking exactly its LeaseId without touching sibling leases.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
