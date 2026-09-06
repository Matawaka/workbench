namespace Matawaka.Workbench.Protocol;

public sealed record CapabilityEvidenceContext(
    string Schema,
    string EvidenceClass,
    string EvidenceDecision,
    string EvidenceRepository,
    string EvidenceFrontier,
    string EvidencePath,
    string EvidenceSha256,
    string EvidenceRole,
    bool CanIncreaseAuthority,
    bool CanOverrideDeny,
    bool CanCreatePermission);

public sealed record CapabilityEvidenceCompositionReceipt(
    string Schema,
    CapabilityRequest Request,
    CapabilityDecision BaseDecision,
    CapabilityEvidenceContext EvidenceContext,
    CapabilityDecision EffectiveDecision,
    bool BaseDecisionPreserved,
    bool AuthorityIncreased,
    bool MutationBudgetIncreased,
    bool NetworkGrantIncreased,
    bool ArbitraryProcessGrantIncreased,
    bool ModelAuthorityCreated,
    bool RuntimeAuthorityCreated,
    bool ResponseAuthorityCreated,
    bool DisplayPermitCreated,
    bool ActionPermitCreated,
    bool SuccessorPermitCreated,
    IReadOnlyList<string> NonEffects);
