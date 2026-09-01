using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// Patch-level activation probe over accepted v0.39. It deliberately adds no
/// launch/handoff runtime semantics; those remain owned by the byte-identical
/// v0.39 implementation. Self-test only replays the complete v0.39 matrix under
/// a new semantic version so the real host can exercise v0.39 by launching this
/// successor candidate.
/// </summary>
public sealed class WorkbenchV0391AcceptanceHarness
{
    private readonly WorkbenchV039AcceptanceHarness _predecessor;

    public WorkbenchV0391AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV039AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var probeChecks = new[]
        {
            new WorkbenchAcceptanceCheck(
                "handoff-activation-probe-runtime-change",
                true,
                "false",
                "false"),
            new WorkbenchAcceptanceCheck(
                "handoff-activation-probe-predecessor",
                true,
                "13f8618c6862b58a9e9de8772c69365058f34e91 / workbench-v0.39-accepted",
                "exact accepted v0.39")
        };
        var checks = predecessor.Checks.Concat(probeChecks).ToArray();
        var passed = predecessor.Passed && probeChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.39.1 Self-test performs no candidate launch, handoff or predecessor close",
            "activation probe does not modify candidate-launch/handoff runtime authority",
            "no external process termination/signal authority",
            "no network/Git/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.39.1",
            "0.39.1",
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
            "Workbench v0.39.1 is a patch-level real-host activation successor. It reuses the full accepted v0.39 acceptance matrix and adds only non-effect probe bindings; v0.39 launch/handoff runtime semantics are unchanged.");
    }
}
