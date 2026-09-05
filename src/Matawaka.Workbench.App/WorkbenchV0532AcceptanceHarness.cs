using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0532AcceptanceHarness
{
    private readonly WorkbenchV053AcceptanceHarness _predecessor;

    public WorkbenchV0532AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV053AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var checkpointChecks = LocalCheckpointV0532Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0532-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV0532Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0532-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = checkpointChecks.Concat(publicationChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.53.2 changes only local checkpoint/publication admission; v0.53 bounded runtime execution primitive is preserved",
            "v0.53.1 process diagnostics candidate remains abandoned/not-planned and is not silently reinterpreted",
            "acceptance performs no remote Git/network operation",
            "real-host publication evidence is evaluated only when the operator explicitly invokes Publish accepted",
            "publication preview is local/no-effect; remote ls-remote/add/push starts only after explicit confirmation",
            "no force push, arbitrary remote/ref/Git command or automatic retry authority",
            "no runtime start/stop, artifact acquisition, extraction/materialization, benchmark, model request or game authority",
            "public remote publication is not automatic"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.53.2",
            "0.53.2",
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
            "Workbench v0.53.2 adds real-host evidence admission and explicit fixed GitHub publication without widening the accepted v0.53 runtime execution authority corridor.");
    }
}
