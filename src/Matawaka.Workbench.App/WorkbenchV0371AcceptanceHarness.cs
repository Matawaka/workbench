using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0371AcceptanceHarness
{
    private readonly WorkbenchV037AcceptanceHarness _predecessor;

    public WorkbenchV0371AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV037AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var guardChecks = LocalApplicationManagedRoleGuardV0371Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(guardChecks).ToArray();
        var passed = predecessor.Passed && guardChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.37.1 Self-test performs no app registration/update/package-build effect",
            "candidate/managed role guard is read-only refusal only",
            "no candidate import/move/copy effect",
            "no network/Git/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.37.1",
            "0.37.1",
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
            "Workbench v0.37.1 reuses the complete accepted v0.37 matrix and adds only offline candidate/managed-root role-separation checks. Candidate Source != Managed Application. Self-test creates no registration/update/package-build/import/network/Agent Execute authority or effect.");
    }
}
