using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV053AcceptanceHarness
{
    private readonly WorkbenchV0521AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV053AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0521AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var serviceChecks = BoundedRuntimeExecutionV053Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v053-runtime-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var surfaceChecks = _window.ObserveV053RuntimeExecutionContract()
            .Select(x => new WorkbenchAcceptanceCheck("v053-surface-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV053Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v053-checkpoint-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = serviceChecks.Concat(surfaceChecks).Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.53 execution accepts only separately materialized runtime-tree evidence and performs no archive extraction/materialization",
            "one-shot execution authority is consumed before executable SHA-256 revalidation and Process.Start",
            "UseShellExecute=false and exact ArgumentList are used; cmd/PowerShell/script/interpreter indirection is refused",
            "process start is followed by exact Windows image path and SHA-256 verification",
            "optional alive-after-delay readiness is observational only: Process Started != Runtime Ready",
            "runtime readiness creates no benchmark/model-request/game/general-process authority",
            "stop accepts no arbitrary PID and targets only the exact in-memory Process object/tree created by the lease",
            "failure/expiry/cancellation creates no retry/resume/start authority",
            "primitive is provider-neutral; KONTUR remains only a future caller",
            "public remote publication remains a separate explicit decision"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.53",
            "0.53",
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
            "Workbench v0.53 adds a provider-neutral one-shot bounded runtime execution lease above separately materialized runtime-tree evidence without granting model, benchmark, game or general process authority.");
    }
}
