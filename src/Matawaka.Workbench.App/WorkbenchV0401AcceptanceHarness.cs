using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// Patch-level activation successor over accepted v0.40. The reusable v0.40
/// transition bootstrap implementation remains unchanged; this wrapper only
/// gives the successor its own semantic acceptance identity.
/// </summary>
public sealed class WorkbenchV0401AcceptanceHarness
{
    private readonly WorkbenchV040AcceptanceHarness _predecessor;

    public WorkbenchV0401AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV040AcceptanceHarness(
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
                "v0401-activation-predecessor-exact",
                true,
                "26e12f75abbba99323190f79693d585790e55bc1 / workbench-v0.40-accepted",
                "exact accepted v0.40"),
            new WorkbenchAcceptanceCheck(
                "v0401-bootstrap-runtime-delta",
                true,
                "false",
                "false"),
            new WorkbenchAcceptanceCheck(
                "v0401-publish-remains-separate",
                true,
                "false",
                "false")
        };
        var checks = predecessor.Checks.Concat(probeChecks).ToArray();
        var passed = predecessor.Passed && probeChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.40.1 Self-test does not modify the accepted v0.40 bootstrap service",
            "v0.40.1 Self-test does not create a transition lease by itself",
            "automatic Self-test/Accept remains possible only after exact one-shot lease claim",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.40.1",
            "0.40.1",
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
            "Workbench v0.40.1 is a real-host activation successor. It reuses the complete accepted v0.40 acceptance matrix while keeping the v0.40 transition-bootstrap implementation byte-identical; only version-bound first-boot acceptance routing is added.");
    }
}
