using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0542AcceptanceHarness
{
    private readonly WorkbenchV0541AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0542AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0541AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)), _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var admissionChecks = RealHostMaterializationAdmissionVerifierV0542.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0542-" + x.Id, x.Passed, x.Observed, x.Expected))
            .Concat(_window.ObserveV0542PublicationAdmissionContract()
                .Select(x => new WorkbenchAcceptanceCheck("v0542-" + x.Id, x.Passed, x.Observed, x.Expected)))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV0542Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0542-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV0542Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0542-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = admissionChecks.Concat(publicationChecks).Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.54.2 adds only exact real-host materialization admission and fixed publication closure",
            "acceptance revalidates Workbench-owned materialization evidence locally and performs no network operation",
            "acceptance performs no acquisition, materialization, process start/stop, benchmark/model/game effect",
            "v0.52 acquisition, v0.53 execution and v0.54 materialization primitive semantics are unchanged",
            "remote publication remains a separate explicit Publish accepted confirmation after local acceptance",
            "no KONTUR-specific authority, Agent Execute or ActionPermit is introduced"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.54.2",
            "0.54.2",
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
            "Workbench v0.54.2 closes publication only after exact local v0.54.1 real-host RUNTIME_TREE_MATERIALIZATION_VERIFIED evidence is revalidated; publication remains fixed, fast-forward-only and separately human-confirmed.");
    }
}
