using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

/// <summary>
/// Pure composition boundary between an independently produced capability decision
/// and an already-admitted provenance observation. Provenance can be recorded as
/// evidence but cannot widen or replace the base authority decision.
/// </summary>
public static class ProvenanceCapabilityEvidenceComposer
{
    public const string ContextSchema = "matawaka.capability-evidence-context/v0.57";
    public const string ReceiptSchema = "matawaka.capability-evidence-composition-receipt/v0.57";
    public const string EvidenceClass = "C2PA_PROVENANCE_OBSERVATION";
    public const string EvidenceRole = "CONSTRAINT_ONLY_NO_AUTHORITY";
    public const string ExpectedPolicy = "freeshield-read-only-bridge/v0.7";

    public static CapabilityEvidenceCompositionReceipt Compose(
        CapabilityRequest request,
        CapabilityDecision baseDecision,
        ProvenanceAdmissionReceipt provenance)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(baseDecision);
        ArgumentNullException.ThrowIfNull(provenance);

        ValidateRequestDecisionBinding(request, baseDecision);
        ValidateBaseDecision(request, baseDecision);
        ValidateProvenance(provenance);

        var context = new CapabilityEvidenceContext(
            ContextSchema,
            EvidenceClass,
            provenance.Decision,
            provenance.EvidenceBinding.Repository,
            provenance.EvidenceBinding.Frontier,
            provenance.EvidenceBinding.Path,
            provenance.ObservedEvidenceSha256,
            EvidenceRole,
            false,
            false,
            false);

        // Effective authority is intentionally identical to the independently
        // produced base decision. Evidence is context, never a grant source.
        var effective = baseDecision;

        return new CapabilityEvidenceCompositionReceipt(
            ReceiptSchema,
            request,
            baseDecision,
            context,
            effective,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "evidence admission does not grant capability authority",
                "provenance cannot convert deny to allow",
                "provenance cannot increase mutation budget",
                "provenance cannot create network access",
                "provenance cannot create arbitrary process execution",
                "provenance cannot create model or runtime authority",
                "provenance cannot create response or display authority",
                "provenance cannot create action or successor permits",
                "unsigned Git tag remains cryptographically unverified",
                "no network access",
                "no process start",
                "no repository or file mutation"
            });
    }

    private static void ValidateRequestDecisionBinding(CapabilityRequest request, CapabilityDecision decision)
    {
        if (!string.Equals(request.Schema, "matawaka.capability-request/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected capability request schema.");
        if (!string.Equals(decision.Schema, "matawaka.capability-decision/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected capability decision schema.");
        if (!string.Equals(decision.RequestId, request.Id, StringComparison.Ordinal))
            throw new InvalidDataException("Capability decision is not bound to the supplied request.");
        if (!string.Equals(decision.Policy, ExpectedPolicy, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected capability policy identity.");
    }

    private static void ValidateBaseDecision(CapabilityRequest request, CapabilityDecision decision)
    {
        if (string.Equals(decision.Decision, "deny", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(decision.AuthorityGranted, "none", StringComparison.OrdinalIgnoreCase) ||
                decision.MutationBudgetGranted != 0 ||
                decision.NetworkAccessGranted ||
                decision.ArbitraryProcessExecutionGranted)
            {
                throw new InvalidDataException("Deny decision contains a positive authority grant.");
            }
            return;
        }

        if (!string.Equals(decision.Decision, "allow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported capability decision value.");

        if (!string.Equals(request.Operation, "observe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Operation, "propose", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only observe/propose can carry the accepted read-only allow decision.");

        if (!string.Equals(request.RequestedAuthority, "read-only", StringComparison.OrdinalIgnoreCase) ||
            request.RequestedMutationBudget != 0 ||
            request.RequestedNetworkAccess ||
            request.RequestedArbitraryProcessExecution)
        {
            throw new InvalidDataException("Allow request asks for authority outside the accepted read-only policy.");
        }

        if (!string.Equals(decision.AuthorityGranted, "read-only", StringComparison.OrdinalIgnoreCase) ||
            decision.MutationBudgetGranted != 0 ||
            decision.NetworkAccessGranted ||
            decision.ArbitraryProcessExecutionGranted)
        {
            throw new InvalidDataException("Allow decision exceeds the accepted read-only authority envelope.");
        }
    }

    private static void ValidateProvenance(ProvenanceAdmissionReceipt provenance)
    {
        var profile = C2paProvenanceAdmissionService.AcceptedWorkbenchV0552Profile;

        if (!string.Equals(provenance.Schema, C2paProvenanceAdmissionService.ReceiptSchema, StringComparison.Ordinal) ||
            !string.Equals(provenance.ProfileId, profile.Id, StringComparison.Ordinal) ||
            !string.Equals(provenance.Decision, C2paProvenanceAdmissionService.Decision, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected provenance admission identity or decision.");

        var expected = profile.Evidence;
        var observed = provenance.EvidenceBinding;
        if (!string.Equals(observed.Repository, expected.Repository, StringComparison.Ordinal) ||
            !string.Equals(observed.Frontier, expected.Frontier, StringComparison.Ordinal) ||
            !string.Equals(observed.Path, expected.Path, StringComparison.Ordinal) ||
            !string.Equals(observed.GitBlobSha1, expected.GitBlobSha1, StringComparison.Ordinal) ||
            !string.Equals(observed.Sha256, expected.Sha256, StringComparison.Ordinal) ||
            observed.Bytes != expected.Bytes ||
            !string.Equals(observed.EvidenceSchema, expected.EvidenceSchema, StringComparison.Ordinal) ||
            !string.Equals(observed.EvidenceVerdict, expected.EvidenceVerdict, StringComparison.Ordinal))
            throw new InvalidDataException("Provenance evidence binding drifted from the accepted profile.");

        if (!string.Equals(provenance.ObservedEvidenceSha256, expected.Sha256, StringComparison.Ordinal) ||
            provenance.ObservedEvidenceBytes != expected.Bytes ||
            !string.Equals(provenance.ObservedEvidenceSchema, expected.EvidenceSchema, StringComparison.Ordinal) ||
            !string.Equals(provenance.ObservedEvidenceVerdict, expected.EvidenceVerdict, StringComparison.Ordinal) ||
            !string.Equals(provenance.ObservedWorkbenchReleaseCommit, profile.ExpectedWorkbenchReleaseCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Observed provenance evidence identity drifted.");

        if (!provenance.C2paExternalReferenceBindingEstablished ||
            !provenance.ImmutableExternalResolutionExactBytesMatched ||
            !provenance.LiveC2paValidationAccepted ||
            provenance.GitTagSignatureVerified)
            throw new InvalidDataException("Provenance validation/signature boundary is not the accepted one.");

        if (provenance.AuthorityCreated ||
            provenance.NetworkAccessPerformed ||
            provenance.ProcessStarted ||
            provenance.ModelInvocationPerformed ||
            provenance.RuntimeExecutionPerformed ||
            provenance.ResponseAuthorityCreated ||
            provenance.DisplayPermitCreated ||
            provenance.ActionPermitCreated ||
            provenance.SuccessorPermitCreated)
            throw new InvalidDataException("Provenance receipt contains an effect or authority promotion.");
    }
}
