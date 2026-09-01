using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV038AcceptanceHarness
{
    private readonly WorkbenchV0371AcceptanceHarness _predecessor;

    public WorkbenchV038AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0371AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var chooserChecks = LocalAppsActionDialogV038.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var storeChecks = LocalApplicationPackageBuildReceiptStoreV038Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(chooserChecks).Concat(storeChecks).ToArray();
        var passed = predecessor.Passed && chooserChecks.All(item => item.Passed) && storeChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.38.1 Self-test opens no Local Apps chooser and performs no package-build/update effect",
            "chooser layout sizing is presentation-only and creates no default action authority",
            "receipt-store checks are offline contract checks only",
            "no local-app package receipt is written by Self-test",
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
            "Workbench v0.38.1 reuses the v0.38 chooser/receipt contracts and adds content-height chooser sizing after real-host Cancel clipping evidence. Self-test creates no Local Apps/package/update/launch/network/Agent Execute authority or effect.");
    }
}
