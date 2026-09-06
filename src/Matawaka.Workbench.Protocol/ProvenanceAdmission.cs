namespace Matawaka.Workbench.Protocol;

public sealed record ProvenanceEvidenceBinding(
    string Repository,
    string Frontier,
    string Path,
    string GitBlobSha1,
    string Sha256,
    int Bytes,
    string EvidenceSchema,
    string EvidenceVerdict);

public sealed record ProvenanceAdmissionProfile(
    string Schema,
    string Id,
    ProvenanceEvidenceBinding Evidence,
    string ExpectedAssertionLabel,
    string ExpectedDigestAlgorithm,
    string ExpectedMediaType,
    string ExpectedWorkbenchReleaseCommit,
    string Decision);

public sealed record ProvenanceAdmissionReceipt(
    string Schema,
    string ProfileId,
    ProvenanceEvidenceBinding EvidenceBinding,
    string ObservedEvidenceSha256,
    int ObservedEvidenceBytes,
    string ObservedEvidenceSchema,
    string ObservedEvidenceVerdict,
    string ObservedWorkbenchReleaseCommit,
    bool C2paExternalReferenceBindingEstablished,
    bool ImmutableExternalResolutionExactBytesMatched,
    bool LiveC2paValidationAccepted,
    bool GitTagSignatureVerified,
    string Decision,
    bool AuthorityCreated,
    bool NetworkAccessPerformed,
    bool ProcessStarted,
    bool ModelInvocationPerformed,
    bool RuntimeExecutionPerformed,
    bool ResponseAuthorityCreated,
    bool DisplayPermitCreated,
    bool ActionPermitCreated,
    bool SuccessorPermitCreated,
    IReadOnlyList<string> NonEffects);
