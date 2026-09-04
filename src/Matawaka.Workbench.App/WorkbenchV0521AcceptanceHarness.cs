using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0521AcceptanceHarness
{
    private readonly WorkbenchV052AcceptanceHarness _predecessor;

    public WorkbenchV0521AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV052AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var diagnosticChecks = NetworkFailureDiagnosticsV0521.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0521-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV0521Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0521-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = diagnosticChecks.Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.52.1 classifies HttpRequestException using bounded HttpRequestError/socket metadata only",
            "raw transport exception messages, request headers, proxy credentials and acquisition bearer are not persisted by diagnostic receipt",
            "diagnostic classification creates no retry/resume/network/acquisition/execution authority",
            "v0.52 one-shot consumption-before-network and exact route/redirect/byte/TTL/hash boundaries are preserved",
            "no KONTUR artifact, extraction, runtime, benchmark, model-request or game authority is added",
            "public remote publication remains deferred"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.52.1",
            "0.52.1",
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
            "Workbench v0.52.1 closes the real-host HTTPS diagnostic gap without widening the accepted v0.52 artifact acquisition authority corridor.");
    }
}
