using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV05110AcceptanceHarness
{
    private readonly WorkbenchV0519AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV05110AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0519AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppMcpOwnerGenerationTransactionV05110Service.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV05110GenerationTransactionContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v05110-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.10 adds non-authoritative owner-generation transaction closure only; PREPARED is explicitly not successor commit evidence",
            "transaction reconciliation runs only after inherited app-scoped owner.lock acquisition and before prior evidence/successor metadata work",
            "prior metadata archives are content-addressed by exact SHA-256 for retry deduplication, while verified legacy v0.51.9 archive paths remain reusable",
            "PREPARED + exact prior bytes still active is recorded ABANDONED_BEFORE_SUCCESSOR and may reuse the verified archive without duplicating prior evidence bytes",
            "PREPARED + exact recorded successor owner metadata is recorded COMMITTED_RECOVERED as owner-generation materialization evidence only",
            "metadata absence closes PREPARED without guessing whether a successor committed; epistemic uncertainty does not become authority",
            "transaction/archive/metadata inconsistency fails closed before the owner is returned to existing lease/listener creation flow",
            "COMMITTED is written only after exact successor owner metadata contract/session observation",
            "transaction/evidence records grant no lease, read, revoke or MCP resume authority and disclose no bearer plaintext/hash or endpoint path secret",
            "canonical v0.48 lease state, v0.51.5 derived active index, v0.51.6 index fence, v0.51.7 singular ownership, v0.51.8 status/recovery and v0.51.9 evidence preservation remain semantically bounded",
            "no historical canonical lease enumeration or canonical/index mutation is performed by generation transaction closure",
            "no automatic Secure MCP Tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.10",
            "0.51.10",
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
            "Workbench v0.51.10 makes MCP owner-generation transitions crash-consistent and epistemically explicit: prior evidence may be PREPARED before successor metadata exists, COMMITTED only after exact successor observation, and retries reuse verified prior bytes without authority widening.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
