using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0551AcceptanceHarness
{
    private readonly WorkbenchV055AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0551AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV055AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)), _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var admissionChecks = RealHostModelInvocationAdmissionVerifierV0551.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0551-" + x.Id, x.Passed, x.Observed, x.Expected))
            .Concat(_window.ObserveV0551PublicationAdmissionContract()
                .Select(x => new WorkbenchAcceptanceCheck("v0551-" + x.Id, x.Passed, x.Observed, x.Expected)))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV0551Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0551-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV0551Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0551-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = admissionChecks.Concat(publicationChecks).Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.55.1 adds only exact real-host v0.55 invocation admission and fixed publication closure",
            "acceptance revalidates Workbench-owned invocation/lease/output/runtime/model evidence locally and performs no network operation",
            "acceptance performs no acquisition, materialization, process start/stop, model invocation, benchmark/game/display effect",
            "v0.52 acquisition, v0.53 execution, v0.54 materialization and v0.55 invocation primitive semantics are unchanged",
            "remote publication remains a separate explicit Publish accepted confirmation after local v0.55.1 acceptance",
            "intermediate local workbench-v0.55-accepted remains unpublished",
            "no KONTUR-specific authority, response authority, Agent Execute, ActionPermit or SuccessorPermit is introduced"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.55.1",
            "0.55.1",
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
            "Workbench v0.55.1 closes publication only after the exact local v0.55 real-host UNTRUSTED_LOCAL_MODEL_OUTPUT evidence and terminal consumed lease are revalidated; publication remains fixed, fast-forward-only and separately human-confirmed.");
    }
}
