using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0552AcceptanceHarness
{
    private readonly WorkbenchV0542AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0552AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0542AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)), _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        // Deliberately do not run WorkbenchV055AcceptanceHarness: its historical exact-title
        // assertion belongs to v0.55 and must not be reinterpreted under a v0.55.2 title.
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);

        var modelServiceChecks = BoundedLocalModelInvocationV055Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-model-" + x.Id, x.Passed, x.Observed, x.Expected));
        var parserChecks = LocalModelInvocationRequestV055Parser.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-parser-" + x.Id, x.Passed, x.Observed, x.Expected));
        var publicProvenanceChecks = ProvenanceBoundRuntimeExecutionV055Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-public82-" + x.Id, x.Passed, x.Observed, x.Expected));
        var routingChecks = _window.ObserveV0552RoutingCompatibilityContract()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-" + x.Id, x.Passed, x.Observed, x.Expected));
        var admissionChecks = RealHostModelInvocationAdmissionVerifierV0551.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-admission-" + x.Id, x.Passed, x.Observed, x.Expected));
        var convergenceContractChecks = WorkbenchConvergenceEvidenceVerifierV0552.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-convergence-" + x.Id, x.Passed, x.Observed, x.Expected));
        var activeEvidenceChecks = _window.ObserveV0552ConvergenceAdmissionContract()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-evidence-" + x.Id, x.Passed, x.Observed, x.Expected));
        var checkpointChecks = LocalCheckpointV0552Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-checkpoint-" + x.Id, x.Passed, x.Observed, x.Expected));
        var publicationChecks = FixedGitHubPublicationV0552Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0552-publication-" + x.Id, x.Passed, x.Observed, x.Expected));

        var successorChecks = modelServiceChecks
            .Concat(parserChecks)
            .Concat(publicProvenanceChecks)
            .Concat(routingChecks)
            .Concat(admissionChecks)
            .Concat(convergenceContractChecks)
            .Concat(activeEvidenceChecks)
            .Concat(checkpointChecks)
            .Concat(publicationChecks)
            .ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.55.2 converges two independently evidenced authority classes without reinterpreting either one",
            "historical v0.55 exact-title acceptance assertion remains historical and is not run under the successor title",
            "accepted v0.55 model-invocation service/parser semantics are rerun directly as semantic contract checks",
            "public #82 provenance-bound runtime-execution lease is verified as a distinct non-model authority class",
            "acceptance revalidates exact real-host v0.55 invocation, recovery and no-ref public-import evidence locally",
            "acceptance performs no network operation, acquisition, materialization, runtime execution or model invocation",
            "two-parent local checkpoint remains separate and publication remains a later explicit human confirmation",
            "no benchmark/game/display/send/ResponseAuthority/Agent Execute/ActionPermit/SuccessorPermit is introduced"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            LocalCheckpointV0552Service.AcceptanceSchema,
            LocalCheckpointV0552Service.Version,
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
            "Workbench v0.55.2 accepts a converged source tree only after local v0.55 real-host model-invocation evidence, exact fail-closed recovery evidence and exact no-ref import of public #82 are revalidated. The checkpoint is two-parent and publication remains separate, fixed and fast-forward-only.");
    }
}
