using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV05111AcceptanceHarness
{
    private readonly WorkbenchV05110AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV05111AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV05110AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppMcpOwnerLeaseBindingV05111Service.RunOfflineContractChecks());
        Add(successorChecks, LocalAppPreparedIndexedLeaseV05111Service.RunOfflineContractChecks());
        Add(successorChecks, _window.ObserveV05111OwnerLeaseBindingContract());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v05111-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.11 adds a non-authoritative owner->lease binding transaction; prepared LeaseId naming is not canonical lease authority",
            "prior incomplete binding is reconciled under inherited app-scoped owner.lock before v0.51.10 can replace prior owner metadata",
            "exact LeaseId is preallocated in PREPARED_BINDING so crash recovery resolves one exact canonical path without historical enumeration",
            "prepared exact LeaseId creation preserves v0.48 state/grant/creation-receipt schemas and bearer-hash-only persistence semantics",
            "v0.51.5 active-index dirty marker/commit and v0.51.6 cross-process fence remain the derived-index control corridor around canonical creation",
            "LEASE_CREATED requires exact canonical state + creation receipt evidence and is explicitly not OWNER_BOUND",
            "OWNER_BOUND requires exact owner SessionId + LeaseId metadata and exact canonical state observation and is explicitly not listener readiness",
            "a live canonical lease from an incomplete binding is never auto-revoked and blocks a successor owner generation until inherited explicit closure or expiry",
            "owner->lease transaction reconciliation performs no historical lease scan and no canonical lease/index mutation",
            "binding transaction/receipts contain no bearer plaintext/hash or endpoint path secret and grant no lease/read/revoke/resume authority",
            "v0.51.10 owner-generation PREPARED/COMMITTED semantics remain unchanged and must complete before new binding PREPARE",
            "no automatic Secure MCP Tunnel/network/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.11",
            "0.51.11",
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
            "Workbench v0.51.11 makes owner->lease provenance crash-consistent by naming an exact LeaseId before canonical creation, distinguishing PREPARED_BINDING, LEASE_CREATED and OWNER_BOUND, and recovering incomplete exact relations without historical enumeration or authority widening.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
