using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0412AcceptanceHarness
{
    private readonly WorkbenchV0411AcceptanceHarness _predecessor;

    public WorkbenchV0412AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0411AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var presentationChecks = JsonSearchPresentationV0412Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV0412Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var publicationChecks = FixedGitHubPublicationV0412Service.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var successorChecks = presentationChecks.Concat(checkpointChecks).Concat(publicationChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.41.2 focus pulse is presentation-only and does not mutate output text",
            "v0.41.2 does not modify accepted v0.41 pure search algorithm semantics",
            "focus acquisition and restoration create no clipboard/file/receipt authority",
            "locally accepted failed-qualification v0.41.1 tag is not publication authority",
            "v0.41.1 accepted tag is not pushed by v0.41.2 publication",
            "no Local Apps import/copy/move authority",
            "no automatic Publish or Lifecycle authority",
            "no network/catalog/Agent Execute effect from search presentation"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.41.2",
            "0.41.2",
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
            "Workbench v0.41.2 preserves the complete accepted v0.41 search/handoff semantics and v0.41.1 source behavior, while adding only a focus-primed presentation repair. Offline acceptance proves the bounded contracts; visible rendering remains subject to the separate real-host qualification required by issue #32 before publication.");
    }
}
