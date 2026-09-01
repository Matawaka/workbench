using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.36 acceptance successor. Reuses the complete accepted v0.35.1 matrix and
/// adds deterministic offline registration contract checks. Real registration
/// and local-app update fixture effects belong to CI, not Self-test.
/// </summary>
public sealed class WorkbenchV036AcceptanceHarness
{
    private readonly WorkbenchV0351AcceptanceHarness _predecessor;

    public WorkbenchV036AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness)
    {
        _predecessor = new WorkbenchV0351AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)));
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var registrationChecks = LocalApplicationRegistrationService.RunOfflineContractChecks()
            .Select(item => new WorkbenchAcceptanceCheck(item.Id, item.Passed, item.Observed, item.Expected))
            .ToArray();
        var checks = predecessor.Checks.Concat(registrationChecks).ToArray();
        var passed = predecessor.Passed && registrationChecks.All(item => item.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.36 Self-test performs no real local-app registration",
            "v0.36 Self-test performs no real local-app update",
            "v0.36 Self-test does not create .matawaka-app.json in a user application",
            "v0.36 Self-test does not copy/move/delete application files",
            "v0.36 Self-test does not launch an application or installer",
            "v0.36 Self-test does not perform network/Git/catalog/Agent Execute effects"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.36",
            "0.36.0",
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
            "Workbench-local acceptance automation v0.36 reuses the accepted v0.35.1 matrix and adds only offline Local Apps registration contract checks. Real fixture registration/update is CI evidence, while Self-test itself creates no app identity/update, launch, installer, network, Git, Agent Execute, ActionPermit, canonical UU-AAP conformance or Stable Core authority.");
    }
}
