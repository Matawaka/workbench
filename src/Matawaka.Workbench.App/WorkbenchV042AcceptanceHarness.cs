using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV042AcceptanceHarness
{
    private readonly WorkbenchV0412AcceptanceHarness _predecessor;

    public WorkbenchV042AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0412AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var shellChecks = InstalledAppsV042Service.RunOfflineContractChecks()
            .Concat(StatusForegroundV042Converter.RunOfflineContractChecks())
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV042Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV042Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var successorChecks = shellChecks.Concat(checkpointChecks).Concat(publicationChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.42 removes manual Self-test/Accept/Stop from the visible surface but preserves bootstrap validation/acceptance",
            "Workspace/Catalog fields are hidden presentation state and their stored values remain unchanged",
            "installed Apps strip reads registered identity sidecars only and creates no app authority",
            "status severity colors and progress overlay do not alter terminal-state semantics",
            "accepted v0.41.2 search/focus behavior remains the predecessor implementation",
            "no automatic Publish or Lifecycle authority",
            "no catalog/Agent Execute effect from shell presentation"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.42",
            "0.42.0",
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
            "Workbench v0.42 preserves the accepted v0.41.2 behavior and changes only the operator shell: five visible maintenance buttons, hidden path fields, read-only installed Apps strip, Find below output, and bottom status-over-progress presentation. First-boot validation and automatic local acceptance remain behind the one-confirmation transition bootstrap even though manual Self-test/Accept controls are no longer visible.");
    }
}
