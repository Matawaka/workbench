using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.32 acceptance wrapper. It reuses the accepted v0.31 read-only semantic/runtime
/// self-test and adds only deterministic, network-free checks for the new fixed
/// GitHub publication contract. Publication itself is never exercised by Self-test.
/// </summary>
public sealed class WorkbenchV032AcceptanceHarness
{
    private readonly WorkbenchAcceptanceHarness _inner;

    public WorkbenchV032AcceptanceHarness(WorkbenchAcceptanceHarness inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _inner.RunAsync(context, cancellationToken);
        var publicationChecks = FixedGitHubPublicationService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(publicationChecks).ToArray();
        var passed = predecessor.Passed && publicationChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.32 Self-test performs no remote read or write",
            "v0.32 Self-test does not add or change git remotes",
            "fixed GitHub publisher is not invoked by Self-test",
            "publisher contract validation does not grant network authority",
            "publisher contract validation does not grant Agent Execute or ActionPermit"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.32",
            "0.32.0",
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
            "Workbench-local acceptance automation v0.32 reuses the complete accepted v0.31 read-only semantic/runtime matrix and adds only offline contract checks for a fixed, fast-forward-only GitHub publication boundary. Self-test never performs network access, never changes a remote, never pushes, never exercises the Publish accepted effect, and never converts publication capability into Agent Execute, ActionPermit, catalog mutation, general network authority, canonical UU-AAP conformance, or Stable Core membership.");
    }
}
