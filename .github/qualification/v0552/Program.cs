using Matawaka.Workbench.App;

static void Check(string group, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
{
    foreach (var check in checks)
    {
        if (!check.Passed)
            throw new Exception($"{group}:{check.Id} failed; observed={check.Observed}; expected={check.Expected}");
        Console.WriteLine($"V0552_QUALIFICATION_PASS {group}:{check.Id}");
    }
}

Check("model-service", BoundedLocalModelInvocationV055Service.RunOfflineContractChecks());
Check("model-parser", LocalModelInvocationRequestV055Parser.RunOfflineContractChecks());
Check("public82-provenance", ProvenanceBoundRuntimeExecutionV055Service.RunOfflineContractChecks());
Check("realhost-admission", RealHostModelInvocationAdmissionVerifierV0551.RunOfflineContractChecks());
Check("convergence-evidence", WorkbenchConvergenceEvidenceVerifierV0552.RunOfflineContractChecks());
Check("checkpoint", LocalCheckpointV0552Service.RunOfflineContractChecks());
Check("publication", FixedGitHubPublicationV0552Service.RunOfflineContractChecks());

if (typeof(ProvenanceBoundRuntimeExecutionV055Service) == typeof(BoundedLocalModelInvocationV055Service))
    throw new Exception("Distinct authority classes collapsed unexpectedly.");
if (ProvenanceBoundRuntimeExecutionV055Service.SourceAuthorityEffect != "NONE_BY_SOURCE_RECORD")
    throw new Exception("Public #82 source evidence unexpectedly grants authority.");
if (BoundedLocalModelInvocationV055Service.RequestSchema == ProvenanceBoundRuntimeExecutionV055Service.SourceBindingSchema)
    throw new Exception("Model-request and provenance-source schemas unexpectedly collapsed.");

Console.WriteLine("WORKBENCH_V0552_OFFLINE_QUALIFICATION_PASS distinctAuthorityClasses=true historicalV055TitleNotReinterpreted=true publicationPreviewNetwork=false");
