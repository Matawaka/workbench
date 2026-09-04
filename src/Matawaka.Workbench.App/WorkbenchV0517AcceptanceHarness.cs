using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0517AcceptanceHarness
{
    private readonly WorkbenchV0516AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0517AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0516AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppMcpSessionOwnershipV0517Service.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0517McpOwnershipContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0517-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.7 adds app-scoped cross-process local MCP runtime serialization only; canonical v0.48 lease state remains authority source of truth",
            "MCP ownership is acquired before auto-start lease creation, so same-app ownership contention creates no replacement lease/listener authority",
            "ownership remains an exclusive open file handle for the listener lifetime; persistent owner metadata is non-authoritative",
            "owner metadata and ownership receipts contain no bearer plaintext, bearer hash or reusable endpoint path token",
            "normal closure order is listener stop proof -> MCP ownership release -> exact LeaseId revoke through the existing verified-index lifecycle",
            "listener-stop uncertainty refuses ownership release and exact canonical closure fail-closed",
            "process termination releases only OS runtime ownership and does not revoke, renew or resume the canonical lease",
            "stale owner metadata cannot authorize MCP resume; surviving live lease remains subject to existing orphan/expiry/exact-revoke semantics",
            "orphan closure and revoke-all recovery require a free app MCP ownership domain and cannot damage another Workbench process listener",
            "different ApplicationIds retain independent MCP ownership domains",
            "v0.51.6 active-index cross-process fence and v0.51.5 derived-index semantics remain unchanged",
            "no historical evidence deletion/compaction and no automatic lease renewal/revocation",
            "no automatic Secure MCP Tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.7",
            "0.51.7",
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
            "Workbench v0.51.7 serializes local MCP runtime ownership across Workbench processes while preserving canonical lease authority, verified-index semantics and explicit orphan recovery boundaries.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
