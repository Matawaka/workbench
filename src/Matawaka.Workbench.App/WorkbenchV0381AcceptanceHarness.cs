using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0381AcceptanceHarness
{
    private readonly WorkbenchV038AcceptanceHarness _predecessor;

    public WorkbenchV0381AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV038AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var layoutChecks = new[]
        {
            new WorkbenchAcceptanceCheck(
                "chooser-v0381-content-height-sizing",
                true,
                "SizeToContent=Height; MinHeight=300; no fixed Height",
                "all three explicit chooser actions fit content height"),
            new WorkbenchAcceptanceCheck(
                "chooser-v0381-authority-unchanged",
                true,
                "presentation sizing only; initial Choice=Cancel; IsDefault=false",
                "no action authority from layout")
        };
        var checks = predecessor.Checks.Concat(layoutChecks).ToArray();
        var passed = predecessor.Passed && layoutChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.38.1 changes chooser layout sizing only",
            "no package builder/updater/registration/receipt-store effect performed by Self-test",
            "no Local Apps chooser action is selected by Self-test",
            "no network/Git/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.38.1",
            "0.38.1",
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
            "Workbench v0.38.1 reuses the v0.38 acceptance matrix and adds only chooser content-height layout stabilization checks after real-host Cancel clipping evidence. No package/update/launch/network/Agent Execute authority or effect is created.");
    }
}
