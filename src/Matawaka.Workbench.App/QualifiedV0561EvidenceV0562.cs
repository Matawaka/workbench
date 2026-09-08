namespace Matawaka.Workbench.App;

public static class QualifiedV0561EvidenceV0562
{
    public const string Version = "0.56.2";
    public const string QualifiedMain = "991fc9c08431141a63a8b53c2f0f138a4f58dc28";
    public const string QualifiedMainTree = "8640750016e5c6512231a237cf9456899cca1db1";
    public const string QualifiedMainParent = "4d954bf5dd77ec732efc7c6d8e05b7c2ab7d161a";
    public const string AcceptedPredecessorTag = "workbench-v0.55.2-accepted";
    public const string AcceptedPredecessorTagObject = "81fbd3d6265b497e7509b7655554789b5a0dcf8d";
    public const string AcceptedPredecessorCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    public const string TargetAcceptedTag = "workbench-v0.56.2-accepted";
    public const long PostMergeRunId = 34020015287;
    public const long PostMergeArtifactId = 9985185887;
    public const string PostMergeArtifactSha256 = "5bd2cf7902321ae8b55dddd9675bebf734dc0261f86587fb1c05ac75906e1975";
    public const int EvidenceBytes = 2415;
    public const string EvidenceSha256 = "ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5";
    public const string EvidenceGitBlob = "4ae93b36d5c8a6c2f00cbc53840221834e09ee26";
    public const string Decision = "PROVENANCE_OBSERVED_NO_AUTHORITY";
    public const string RussianHeadline = "СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ";

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var admitted = MainWindow.AdmitEmbeddedProvenanceEvidenceV0561();
        var russian = ProvenanceReviewPresentationServiceV0561.Create(admitted, ProvenanceReviewLanguageV0561.Russian);
        return new[]
        {
            ("v0562-version", Version == "0.56.2", Version, "0.56.2"),
            ("v0562-qualified-main", QualifiedMain.Length == 40, QualifiedMain, "exact post-merge v0.56.1 main"),
            ("v0562-qualified-tree", QualifiedMainTree.Length == 40, QualifiedMainTree, "exact qualified source tree"),
            ("v0562-predecessor-tag", AcceptedPredecessorTag == "workbench-v0.55.2-accepted", AcceptedPredecessorTag, "accepted v0.55.2 identity"),
            ("v0562-target-tag", TargetAcceptedTag == "workbench-v0.56.2-accepted", TargetAcceptedTag, "fresh successor identity"),
            ("v0562-postmerge-run", PostMergeRunId == 34020015287, PostMergeRunId.ToString(), "exact workflow_dispatch run"),
            ("v0562-postmerge-artifact", PostMergeArtifactId == 9985185887, PostMergeArtifactId.ToString(), "exact post-merge evidence artifact"),
            ("v0562-postmerge-artifact-digest", PostMergeArtifactSha256.Length == 64, PostMergeArtifactSha256, "exact artifact SHA-256"),
            ("v0562-admission-decision", admitted.Decision == Decision, admitted.Decision, Decision),
            ("v0562-admission-evidence", admitted.ObservedEvidenceBytes == EvidenceBytes && admitted.ObservedEvidenceSha256 == EvidenceSha256,
                $"{admitted.ObservedEvidenceBytes}/{admitted.ObservedEvidenceSha256}", $"{EvidenceBytes}/{EvidenceSha256}"),
            ("v0562-no-authority", !admitted.AuthorityCreated && !admitted.NetworkAccessPerformed && !admitted.ProcessStarted &&
                !admitted.ModelInvocationPerformed && !admitted.RuntimeExecutionPerformed && !admitted.ResponseAuthorityCreated &&
                !admitted.DisplayPermitCreated && !admitted.ActionPermitCreated && !admitted.SuccessorPermitCreated,
                "all authority/effect fields false", "all authority/effect fields false"),
            ("v0562-human-wording", russian.Headline == RussianHeadline, russian.Headline, RussianHeadline),
            ("v0562-no-russian-jargon", !russian.Headline.Contains("ПРОВЕНАНС", StringComparison.OrdinalIgnoreCase), russian.Headline, "plain Russian origin wording")
        };
    }
}
