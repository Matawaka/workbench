using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV054AcceptanceHarness
{
    private readonly WorkbenchV0532AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV054AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0532AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var primitiveChecks = BoundedRuntimeTreeMaterializationV054Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v054-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var surfaceChecks = _window.ObserveV054MaterializationContract()
            .Select(x => new WorkbenchAcceptanceCheck("v054-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var chooserChecks = LocalAppsActionDialogV0515.RunOfflineContractChecks()
            .Where(x => x.Id.Contains("v054", StringComparison.Ordinal))
            .Select(x => new WorkbenchAcceptanceCheck("v054-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV054Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v054-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = primitiveChecks.Concat(surfaceChecks).Concat(chooserChecks).Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.54 adds only a provider-neutral runtime-tree materialization authority between exact v0.52 acquisition evidence and unchanged v0.53 execution",
            "acceptance performs no archive materialization; primitive runtime tests remain separate qualification evidence",
            "acceptance performs no network operation or artifact acquisition",
            "acceptance performs no process start/stop and creates no execution/model/benchmark/game authority",
            "v0.52 acquisition and v0.53 runtime execution semantics remain separate and are not widened",
            "public remote publication remains a separate explicit post-realhost decision"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.54",
            "0.54",
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
            "Workbench v0.54 introduces a one-shot receipt-bound ZIP-to-runtime-tree materialization lease that emits v0.53-compatible MATERIALIZED_VERIFIED evidence without granting execution/model/benchmark/game authority.");
    }
}
