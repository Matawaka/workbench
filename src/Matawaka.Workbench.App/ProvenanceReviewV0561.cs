using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.App;

public sealed record ProvenanceReviewFactV0561(string Label, string Value, string Explanation);

public sealed record ProvenanceReviewPresentationV0561(
    string Headline,
    string Summary,
    IReadOnlyList<ProvenanceReviewFactV0561> ObservedFacts,
    IReadOnlyList<ProvenanceReviewFactV0561> Boundaries,
    IReadOnlyList<ProvenanceReviewFactV0561> EvidenceSource,
    string HumanReviewPrompt);

public static class ProvenanceReviewPresentationServiceV0561
{
    public const string Headline = "PROVENANCE OBSERVED — NO AUTHORITY";
    public const string UnsignedTag = "UNSIGNED / NOT VERIFIED";
    public const string NotEstablished = "NOT ESTABLISHED";
    public const string NotCreated = "NOT CREATED";
    public const string ExpectedDecision = "PROVENANCE_OBSERVED_NO_AUTHORITY";
    public const string ExpectedReleaseCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";

    public static ProvenanceReviewPresentationV0561 Create(ProvenanceAdmissionReceipt receipt)
    {
        if (!string.Equals(receipt.Decision, ExpectedDecision, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected provenance admission decision: {receipt.Decision}.");
        if (!receipt.C2paExternalReferenceBindingEstablished)
            throw new InvalidDataException("C2PA external-reference binding was not established.");
        if (!receipt.ImmutableExternalResolutionExactBytesMatched)
            throw new InvalidDataException("Immutable external evidence did not match exact bytes.");
        if (!receipt.LiveC2paValidationAccepted)
            throw new InvalidDataException("Live C2PA validation was not accepted.");
        if (receipt.GitTagSignatureVerified)
            throw new InvalidDataException("Unsigned Workbench tag must not be presented as cryptographically verified.");
        if (!string.Equals(receipt.ObservedWorkbenchReleaseCommit, ExpectedReleaseCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected observed Workbench release commit.");

        if (receipt.AuthorityCreated ||
            receipt.NetworkAccessPerformed ||
            receipt.ProcessStarted ||
            receipt.ModelInvocationPerformed ||
            receipt.RuntimeExecutionPerformed ||
            receipt.ResponseAuthorityCreated ||
            receipt.DisplayPermitCreated ||
            receipt.ActionPermitCreated ||
            receipt.SuccessorPermitCreated)
            throw new InvalidDataException("Provenance admission contains an authority/effect promotion and cannot be presented as review-only evidence.");

        return new ProvenanceReviewPresentationV0561(
            Headline,
            "A cryptographic provenance relationship was admitted as evidence. This screen does not grant trust, truth, permission, or authority to act.",
            new[]
            {
                new ProvenanceReviewFactV0561("C2PA external-reference", "HASH BINDING OBSERVED", "The admitted evidence reports one standard c2pa.external-reference binding."),
                new ProvenanceReviewFactV0561("External evidence bytes", "EXACT MATCH", "The immutable external resolution matched the expected evidence bytes."),
                new ProvenanceReviewFactV0561("Live C2PA validation", "ACCEPTED", "The accepted evidence reports a successful live C2PA validation surface."),
                new ProvenanceReviewFactV0561("Workbench release observed", receipt.ObservedWorkbenchReleaseCommit, "Observed release identity only; this does not create release authority.")
            },
            new[]
            {
                new ProvenanceReviewFactV0561("Git tag signature", UnsignedTag, "The annotated Workbench tag was observed as unsigned. No verified Git tag signature is claimed."),
                new ProvenanceReviewFactV0561("Truth", NotEstablished, "A valid provenance relationship does not certify the truth of the underlying claims."),
                new ProvenanceReviewFactV0561("Publication authority", NotEstablished, "Repository publication and provenance do not establish who was authorized to publish."),
                new ProvenanceReviewFactV0561("Runtime / model authority", NotCreated, "No runtime execution or model-request authority is created by this provenance observation."),
                new ProvenanceReviewFactV0561("Response / display authority", NotCreated, "No response or display permission is created by this provenance observation."),
                new ProvenanceReviewFactV0561("Action / successor permits", NotCreated, "No external action or successor permission is created by this provenance observation.")
            },
            new[]
            {
                new ProvenanceReviewFactV0561("Evidence SHA-256", receipt.ObservedEvidenceSha256, "Exact admitted evidence digest."),
                new ProvenanceReviewFactV0561("Evidence source", $"{receipt.EvidenceBinding.Repository}@{receipt.EvidenceBinding.Frontier}", "Pinned source frontier for the admitted evidence."),
                new ProvenanceReviewFactV0561("Evidence path", receipt.EvidenceBinding.Path, "Pinned repository path of the admitted qualification receipt."),
                new ProvenanceReviewFactV0561("Admission decision", receipt.Decision, "Workbench classified the supplied evidence as observation-only, with no authority.")
            },
            "Human review: within five seconds, is it unmistakable that provenance was observed while truth, trust, and authority were NOT granted?");
    }
}
