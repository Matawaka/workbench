using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV05112AcceptanceHarness
{
    private readonly WorkbenchV051111AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV05112AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV051111AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        successorChecks.AddRange(LocalAppMcpListenerReadinessV05112Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v05112-listener-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(_window.ObserveV05112ListenerReadinessContract()
            .Select(x => new WorkbenchAcceptanceCheck("v05112-ui-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window)
            .Select(x => new WorkbenchAcceptanceCheck("v05112-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.12 adds listener-readiness provenance/control state only; canonical v0.48 read lease remains the authority source of truth",
            "OWNER_BOUND, PREPARED_LISTENER_START, LISTENER_STARTED and LISTENER_READY remain distinct observations and none is inferred from the later state",
            "PREPARED_LISTENER_START creates no listener and LISTENER_STARTED is not committed readiness",
            "LISTENER_READY requires the same process-local IPv4-loopback adapter to be re-observed active for the exact ApplicationId/LeaseId while the canonical lease remains live",
            "listener-readiness reconciliation runs first under reacquired app owner.lock and never auto-starts, auto-resumes, auto-renews or auto-revokes",
            "a live exact bound lease without current listener authority becomes LIVE_BOUND_NO_LISTENER and blocks silent successor owner generation until explicit inherited closure or expiry",
            "listener-readiness transaction performs no historical canonical lease enumeration and no canonical lease/active-index mutation",
            "listener-readiness receipts contain no bearer plaintext/hash or reusable endpoint path token and grant no lease/read/revoke/resume authority",
            "v0.51.11.1 exclusive Local Apps routing and v0.51.11 owner-to-lease transaction semantics remain preserved",
            "KONTUR integration files included in this source frontier are planning/contract anchors only and create no cross-project runtime/download/model/game authority",
            "no automatic Secure MCP Tunnel/network publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.12",
            "0.51.12",
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
            "Workbench v0.51.12 makes local MCP listener readiness crash-consistent and exact-bound: OWNER_BOUND, PREPARED_LISTENER_START, LISTENER_STARTED and LISTENER_READY are independently evidenced while crash recovery blocks silent runtime replacement without widening authority.");
    }
}
