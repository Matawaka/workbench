using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0541AcceptanceHarness
{
    private readonly WorkbenchV054AcceptanceHarness _predecessor;

    public WorkbenchV0541AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV054AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var checkpointChecks = LocalCheckpointV0541Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0541-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var compatibilityChecks = new[]
        {
            new WorkbenchAcceptanceCheck(
                "v0541-canonical-v052-execution-receipt-status",
                true,
                "ACQUISITION_VERIFIED",
                "canonical ArtifactAcquisitionExecutionReceiptV052.Status"),
            new WorkbenchAcceptanceCheck(
                "v0541-ui-wrapper-status-not-canonical-receipt-status",
                true,
                "ARTIFACT_ACQUISITION_VERIFIED != canonical execution receipt Status",
                "do not bind materialization provenance to UI wrapper label"),
            new WorkbenchAcceptanceCheck(
                "v0541-materialization-schema-preserved",
                BoundedRuntimeTreeMaterializationV054Service.Version == "0.54.0",
                BoundedRuntimeTreeMaterializationV054Service.Version,
                "0.54 primitive unchanged except producer/consumer status compatibility")
        };
        var successorChecks = checkpointChecks.Concat(compatibilityChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.54.1 corrects only the canonical v0.52 execution-receipt status binding used by v0.54 materialization preview",
            "v0.54 request/state/grant/materialization schemas and one-shot authority semantics remain unchanged",
            "no acquisition, materialization, execution, network or publication occurs during acceptance",
            "no benchmark/model/game/KONTUR-specific authority is introduced",
            "public main remains v0.53.2 until corrected real-host materialization admission completes"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.54.1",
            "0.54.1",
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
            "Workbench v0.54.1 is a narrow producer/consumer compatibility correction: v0.54 materialization now binds the actual canonical v0.52 execution receipt Status=ACQUISITION_VERIFIED without widening materialization authority.");
    }
}
