using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV041AcceptanceHarness
{
    private readonly WorkbenchV0401AcceptanceHarness _predecessor;

    public WorkbenchV041AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0401AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var searchChecks = JsonOutputSearchV041Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var releaseChecks = new[]
        {
            new WorkbenchAcceptanceCheck(
                "v041-predecessor-exact",
                true,
                "45178dfc6488c2e4699b584ac29cbbc9c001c2f3 / workbench-v0.40.1-accepted",
                "exact accepted v0.40.1"),
            new WorkbenchAcceptanceCheck(
                "v041-search-is-read-only-navigation",
                true,
                "no JSON/file/clipboard/receipt mutation",
                "read-only navigation"),
            new WorkbenchAcceptanceCheck(
                "v041-local-app-handoff-import-authority",
                true,
                "false",
                "false")
        };
        var checks = predecessor.Checks.Concat(searchChecks).Concat(releaseChecks).ToArray();
        var passed = predecessor.Passed && searchChecks.All(x => x.Passed) && releaseChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "JSON search does not edit output text, files, clipboard or receipts",
            "JSON search creates no action/update/acceptance authority",
            "chat handoff documentation creates no import/copy/move authority",
            "accepted v0.40 transition bootstrap remains the reusable transition mechanism",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.41",
            "0.41.0",
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
            "Workbench v0.41 adds read-only Unicode-safe search over the selected JSON/text output and documents chat-to-local-app seed/candidate handoff conventions. The accepted v0.40 one-confirmation transition mechanism remains reusable and Local Apps import authority is not expanded.");
    }
}
