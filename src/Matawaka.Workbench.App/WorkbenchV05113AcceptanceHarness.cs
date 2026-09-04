using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV05113AcceptanceHarness
{
    private readonly WorkbenchV05112AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV05113AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV05112AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        successorChecks.AddRange(LocalAppMcpShutdownTransactionV05113Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v05113-shutdown-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(_window.ObserveV05113ShutdownContract()
            .Select(x => new WorkbenchAcceptanceCheck("v05113-ui-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window)
            .Select(x => new WorkbenchAcceptanceCheck("v05113-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.13 adds reverse shutdown provenance/control state only; canonical v0.48 read lease remains the authority source of truth",
            "SHUTDOWN_PREPARED, LISTENER_STOPPED, OWNER_RELEASED, LEASE_REVOKED/LEASE_ALREADY_TERMINAL and SHUTDOWN_COMPLETED remain distinct observations",
            "SHUTDOWN_PREPARED is not evidence that a listener stopped and LISTENER_STOPPED is not evidence that owner authority was released",
            "OWNER_RELEASED is not canonical lease revocation; exact indexed revoke remains a separate inherited authority-bearing operation",
            "shutdown reconciliation runs first under reacquired app owner.lock and never auto-starts/resumes a listener or auto-revokes/renews a live lease",
            "a live exact lease after prior runtime loss becomes OWNER_RELEASED_LEASE_LIVE and blocks silent successor generation until explicit closure or expiry",
            "shutdown transaction performs no historical canonical enumeration and no canonical lease/active-index mutation itself",
            "shutdown transaction refuses sibling-lease revocation and stores/discloses no bearer plaintext/hash or reusable endpoint path token",
            "v0.51.12 listener readiness, v0.51.11 owner-to-lease binding and v0.51.11.1 exclusive Local Apps routing remain preserved",
            "KONTUR integration anchors remain planning/contracts only; v0.51.13 creates no model/download/runtime/game authority",
            "no automatic Secure MCP Tunnel/network publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.13",
            "0.51.13",
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
            "Workbench v0.51.13 makes reverse local-MCP shutdown crash-consistent: stop intent, material listener stop, owner release, exact lease terminality and completed closure are independently evidenced without turning recovery evidence into authority.");
    }
}
