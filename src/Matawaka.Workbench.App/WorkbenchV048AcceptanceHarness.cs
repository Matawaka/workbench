using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV048AcceptanceHarness
{
    private readonly WorkbenchV047AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV048AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV047AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppsActionDialogV048.RunOfflineContractChecks());
        Add(successorChecks, LocalAppReadLeaseRequestDialogV048.RunOfflineContractChecks());
        Add(successorChecks, LocalAppReadLeaseV048Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV048Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV048Service.RunOfflineContractChecks());

        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v048-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.48 read-session lease request is not lease authority; explicit human confirmation remains required",
            "lease scopes are fixed to exact selected ApplicationId plus installed/source exact-file or explicit existing directory-prefix paths",
            "lease bearer plaintext is returned once in a grant while persisted lease state contains only bearer SHA-256",
            "lease authority is bounded by TTL, per-read bytes, total bytes, call count, scope, revocation and optional expected file SHA",
            "expired/revoked/exhausted/wrong-bearer/out-of-scope/stale-hash requests are refused",
            "v0.47 manual Chat read relay remains available as a conservative fallback",
            "v0.48 implements no HTTP listener, tunnel, MCP server or automatic network transport",
            "mutable lease state under .workbench is local-only and excluded from checkpoint/publication",
            "no application/source mutation, process execution, catalog mutation, Agent Execute, ActionPermit, Stable Core or canonical UU-AAP promotion"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.48",
            "0.48.0",
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
            "Workbench v0.48 adds short-lived bounded read-session leases as a transport-neutral authority substrate over the accepted local read primitive, with hash-only bearer persistence, budgets, expiry and revocation, while preserving the v0.47 manual relay and adding no network/MCP authority.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
