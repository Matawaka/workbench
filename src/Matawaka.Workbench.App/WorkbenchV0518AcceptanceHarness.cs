using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0518AcceptanceHarness
{
    private readonly WorkbenchV0517AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0518AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0517AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppMcpOwnershipStatusV0518Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppMcpOwnershipRecoveryV0518Service.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0518OwnershipStatusContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0518-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.8 adds an observational MCP ownership status surface and explicit stale-metadata evidence rotation only",
            "owner metadata remains non-authoritative and cannot grant MCP resume, read, lease create or revoke authority",
            "ownership status distinguishes OWNED/FREE_NO_METADATA/FREE_STALE_METADATA by probing only the existing app-scoped owner handle",
            "status never creates owner.lock and performs no historical canonical lease enumeration",
            "a referenced metadata LeaseId is classified only through its exact canonical state path and does not become authority",
            "stale acknowledgement requires FREE_STALE_METADATA and a fresh exclusive guard on the existing owner.lock",
            "stale acknowledgement preserves exact prior metadata bytes under stale-evidence-v0518 and clears only the active metadata slot",
            "stale acknowledgement does not revoke, renew, create, consume or resume any canonical lease or MCP session",
            "live orphan closure remains the existing separate explicit exact action; no closure authority is inferred from stale metadata",
            "v0.51.7 singular MCP runtime ownership and stop -> owner release -> exact revoke semantics remain unchanged",
            "v0.51.6 active-index fence, v0.51.5 derived index and canonical v0.48 lease authority remain unchanged",
            "no bearer plaintext/hash or reusable endpoint path token is exposed by status/acknowledgement receipts",
            "no automatic Secure MCP Tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.8",
            "0.51.8",
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
            "Workbench v0.51.8 makes MCP runtime ownership and stale owner metadata explicitly observable/recoverable without converting metadata or a free owner domain into lease, read, revoke or MCP resume authority.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
