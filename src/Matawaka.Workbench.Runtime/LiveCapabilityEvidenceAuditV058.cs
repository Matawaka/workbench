using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.Runtime;

public sealed record LiveCapabilityEvidenceAuditReceiptV058(
    string Schema,
    string Status,
    bool EvidenceAvailable,
    CapabilityEvidenceCompositionReceipt? Composition,
    bool BaseDecisionChanged,
    bool ProviderAuthorityChanged,
    bool SemanticAuthorityContractChanged,
    bool EvidenceRequiredForAllow,
    bool AuthorityCreated,
    bool ModelInvocationAuthorityCreated,
    bool RuntimeExecutionAuthorityCreated,
    bool ResponseAuthorityCreated,
    bool DisplayPermitCreated,
    bool ActionPermitCreated,
    bool SuccessorPermitCreated,
    IReadOnlyList<string> NonEffects);

public interface ILiveCapabilityEvidenceAuditSourceV058
{
    ProvenanceAdmissionReceipt Read();
}

public sealed class EmbeddedAcceptedProvenanceAuditSourceV058 : ILiveCapabilityEvidenceAuditSourceV058
{
    public const string ResourceName = "Matawaka.Workbench.Runtime.ProvenanceCapabilityEvidenceV058.Qualification";

    public ProvenanceAdmissionReceipt Read()
    {
        using var stream = typeof(EmbeddedAcceptedProvenanceAuditSourceV058).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded provenance audit evidence is unavailable: {ResourceName}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return C2paProvenanceAdmissionService.AdmitAcceptedWorkbenchV0552(buffer.ToArray());
    }
}

/// <summary>
/// Supplemental audit-only observation composed after the independently evaluated
/// capability decision. This service never supplies authority to the provider or
/// SemanticHost and never changes the base decision.
/// </summary>
public sealed class LiveCapabilityEvidenceAuditServiceV058
{
    public const string ReceiptSchema = "matawaka.live-capability-evidence-audit-receipt/v0.58";
    public const string ComposedStatus = "COMPOSED_NO_AUTHORITY_CHANGE";
    public const string RejectedStatus = "EVIDENCE_REJECTED_NO_AUTHORITY_CHANGE";

    private readonly ILiveCapabilityEvidenceAuditSourceV058 _source;

    public LiveCapabilityEvidenceAuditServiceV058(ILiveCapabilityEvidenceAuditSourceV058? source = null)
    {
        _source = source ?? new EmbeddedAcceptedProvenanceAuditSourceV058();
    }

    public LiveCapabilityEvidenceAuditReceiptV058 Observe(
        CapabilityRequest request,
        CapabilityDecision baseDecision)
    {
        try
        {
            var provenance = _source.Read();
            var composition = ProvenanceCapabilityEvidenceComposer.Compose(request, baseDecision, provenance);

            RequireSameDecisionGrants(baseDecision, composition.BaseDecision, "composition base decision");
            RequireNoIncrease(baseDecision, composition.EffectiveDecision);

            return new LiveCapabilityEvidenceAuditReceiptV058(
                ReceiptSchema,
                ComposedStatus,
                true,
                composition,
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
                false,
                new[]
                {
                    "supplemental provenance audit is not provider authority",
                    "base CapabilityDecision remains provider authority input",
                    "SemanticEvidencePacket authority contract is unchanged",
                    "evidence is not required for the base policy to allow",
                    "audit composition cannot increase mutation, network or process grants",
                    "audit composition cannot create model/runtime/response/display/action/successor authority"
                });
        }
        catch (InvalidDataException error)
        {
            return new LiveCapabilityEvidenceAuditReceiptV058(
                ReceiptSchema,
                RejectedStatus,
                false,
                null,
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
                false,
                new[]
                {
                    "supplemental provenance audit was rejected without changing the base decision",
                    $"evidence rejection: {error.Message}",
                    "rejected evidence creates no provider authority",
                    "rejected evidence creates no SemanticHost authority",
                    "no retry, refresh, network fetch or process execution is authorized by audit rejection"
                });
        }
    }

    private static void RequireNoIncrease(CapabilityDecision baseDecision, CapabilityDecision composed)
    {
        if (string.Equals(baseDecision.Decision, "deny", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(composed.Decision, "deny", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Supplemental audit attempted to upgrade base deny.");

        if (composed.MutationBudgetGranted > baseDecision.MutationBudgetGranted)
            throw new InvalidDataException("Supplemental audit attempted to increase mutation budget.");
        if (!baseDecision.NetworkAccessGranted && composed.NetworkAccessGranted)
            throw new InvalidDataException("Supplemental audit attempted to increase network authority.");
        if (!baseDecision.ArbitraryProcessExecutionGranted && composed.ArbitraryProcessExecutionGranted)
            throw new InvalidDataException("Supplemental audit attempted to increase arbitrary-process authority.");
        if (!string.Equals(composed.AuthorityGranted, baseDecision.AuthorityGranted, StringComparison.Ordinal))
            throw new InvalidDataException("Supplemental audit attempted to change the base authority string.");
    }

    private static void RequireSameDecisionGrants(
        CapabilityDecision expected,
        CapabilityDecision observed,
        string label)
    {
        if (!string.Equals(expected.Schema, observed.Schema, StringComparison.Ordinal) ||
            !string.Equals(expected.RequestId, observed.RequestId, StringComparison.Ordinal) ||
            !string.Equals(expected.Decision, observed.Decision, StringComparison.Ordinal) ||
            !string.Equals(expected.Policy, observed.Policy, StringComparison.Ordinal) ||
            !string.Equals(expected.AuthorityGranted, observed.AuthorityGranted, StringComparison.Ordinal) ||
            expected.MutationBudgetGranted != observed.MutationBudgetGranted ||
            expected.NetworkAccessGranted != observed.NetworkAccessGranted ||
            expected.ArbitraryProcessExecutionGranted != observed.ArbitraryProcessExecutionGranted)
            throw new InvalidDataException($"Supplemental audit changed {label}.");
    }
}
