using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0519AcceptanceHarness
{
    private readonly WorkbenchV0518AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0519AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0518AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppMcpOwnerGenerationV0519Service.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV0519GenerationContinuityContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0519-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.9 adds generation-to-generation MCP owner provenance continuity only; it does not create a new authority primitive",
            "prior owner metadata is preserved only after the app-scoped owner handle is acquired and before successor owner metadata is written",
            "valid prior metadata is archived byte-for-byte and hash-verified; invalid prior metadata is archived as opaque untrusted evidence",
            "prior metadata evidence does not grant lease, read, revoke or MCP resume authority",
            "evidence preservation failure releases the newly acquired owner handle before the existing UI can create a lease or listener",
            "a busy owner domain still fails through inherited MCP_SESSION_OWNED_BY_OTHER_PROCESS before generation/archive/lease mutation",
            "v0.51.8 explicit stale acknowledgement remains available and compatible; if it already cleared active metadata, the next generation records NO_PRIOR_OWNER_METADATA",
            "canonical v0.48 lease state, v0.51.5 derived active index, v0.51.6 index fence and v0.51.7 singular MCP ownership semantics remain unchanged",
            "no historical canonical lease enumeration or canonical/index mutation is performed by generation preservation",
            "transition receipts contain no prior raw bytes, bearer plaintext/hash or endpoint path secret",
            "no automatic Secure MCP Tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.9",
            "0.51.9",
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
            "Workbench v0.51.9 prevents silent stale MCP owner metadata overwrite by preserving and hash-verifying prior owner evidence under the already-held app-scoped owner lock before any successor generation metadata is written.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
