using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV039AcceptanceHarness
{
    private readonly WorkbenchV0381AcceptanceHarness _predecessor;

    public WorkbenchV039AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0381AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var handoffChecks = CandidateLaunchHandoffV039Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(handoffChecks).ToArray();
        var passed = predecessor.Passed && handoffChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.39 Self-test launches no candidate and closes no Workbench window",
            "handoff checks are offline contract checks only",
            "no candidate acceptance/checkpoint/publication authority created by handoff checks",
            "no external process termination or signal effect",
            "no network/Git/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.39",
            "0.39.0",
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
            "Workbench v0.39 reuses the complete accepted v0.38.1 matrix and adds offline candidate-launch handoff checks. Self-test performs no launch, predecessor close, candidate acceptance, process termination, network or Agent Execute effect.");
    }
}
