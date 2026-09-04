using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0515AcceptanceHarness
{
    private readonly WorkbenchV0514AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0515AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0514AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppActiveLeaseIndexV0515Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppReadLeaseIndexedLifecycleV0515Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppsActionDialogV0515.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0515VerifiedIndexContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0515-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.5 active index is derived control state; exact per-lease v0.48 state remains canonical authority",
            "active index stores no bearer plaintext/hash and does not duplicate scope as authority truth",
            "fast live-authority status verifies indexed LeaseIds against exact canonical state and performs no historical canonical scan",
            "first-use and crash-gap recovery require explicit bounded reconciliation with hard ceiling 4096 canonical state files",
            "durable dirty marker is written before supported create/exact-revoke/revoke-all authority-set transitions and blocks index use until commit/reconciliation",
            "expired/exhausted indexed candidates may be pruned only from derived index after canonical verification; canonical evidence remains unchanged",
            "historical evidence page remains an explicit separate canonical scan and preserves v0.51.4 bounded pagination",
            "no automatic lease creation/renewal/revocation, no read/list budget consumption by status/index verification",
            "no automatic MCP/tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.5",
            "0.51.5",
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
            "Workbench v0.51.5 bounds live-authority discovery cost through a fail-closed verified derived index while preserving canonical per-lease evidence and separate bounded historical pagination.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
