using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

public sealed record SemanticRepositoryRef(
    string Name,
    string Branch,
    string Head,
    int SelectedEvidenceItems,
    IReadOnlyList<string> TopTerms);

public sealed record SemanticEvidencePacket(
    string Schema,
    string CommandId,
    string Target,
    IReadOnlyList<SemanticRepositoryRef> Repositories,
    AgentEvidenceCoverage Coverage,
    IReadOnlyList<AgentEvidence> Evidence,
    CapabilityReceipt AuthorityReceipt);

public sealed record SemanticProviderBoundaryReceipt(
    string Schema,
    string Provider,
    string InputSchema,
    string ExpectedUuAapFrontier,
    string? ObservedUuAapFrontier,
    bool SourceFrontierMatched,
    IReadOnlyList<ProtocolSourceBinding> SourceBindings,
    bool RepositoryRootsProvided,
    bool FileHandlesProvided,
    bool ArbitraryProcessExecutionProvided,
    bool NetworkAccessProvided,
    bool MutationAuthorityProvided,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderResult(
    AgentProposal Proposal,
    SemanticProviderBoundaryReceipt Boundary,
    string Note);

public interface ISemanticProvider
{
    string ProviderId { get; }

    Task<SemanticProviderResult> AnalyzeAsync(
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// The first interchangeable semantic provider boundary. It receives only a
/// sanitized evidence packet and typed authority receipt. It gets no repository
/// roots, file handles, process runner, network client, or mutation capability.
/// </summary>
public sealed class DeterministicSemanticProvider : ISemanticProvider
{
    public string ProviderId => "deterministic-evidence-semantic-v0.2";

    public Task<SemanticProviderResult> AnalyzeAsync(
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.started",
            94,
            $"Semantic provider receives {packet.Evidence.Count} evidence items; no ambient filesystem authority",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "EVIDENCE_BOUND_PROPOSAL_DERIVATION",
            "NONE",
            "SEMANTIC_PROPOSAL_READY",
            $"semantic:{packet.CommandId}"));

        var represented = packet.Repositories
            .Where(item => item.SelectedEvidenceItems > 0)
            .Select(item => item.Name)
            .ToArray();

        var actions = new List<string>
        {
            $"Preserve the balanced evidence frontier across {represented.Length}/{packet.Repositories.Count} repositories as the causal input to this proposal.",
            "Keep the semantic provider input restricted to evidence snippets, repository identity/branch/HEAD, coverage, and the typed authority receipt; do not pass repository roots or process handles.",
            "Use PCL-compatible progress receipts for visible phase/waiting/next-event state without exposing hidden reasoning.",
            "Treat scoped authority evidence and materialization authority as separate future gates; neither is equivalent to execution authority or an ActionPermit.",
            "Keep repository mutation closed in v0.2; any later materialization or execute path requires a fresh separately evaluated authority chain."
        };

        var proposal = new AgentProposal(
            "Evidence-bounded semantic-provider checkpoint",
            actions,
            "STOP before repository mutation, canonical protocol claims, external model/network calls, arbitrary process execution, materialization, ActionPermit creation, or self-expansion of authority.");

        var observedUuAap = packet.Repositories
            .FirstOrDefault(item => string.Equals(item.Name, "uu-aap", StringComparison.OrdinalIgnoreCase))
            ?.Head;

        var bindings = new[]
        {
            PclCompatibleProgress.ProgressSource,
            PclCompatibleProgress.HumanViewSource,
            PclCompatibleProgress.ScopedAuthoritySource,
            PclCompatibleProgress.MaterializationAuthoritySource
        };

        var boundary = new SemanticProviderBoundaryReceipt(
            "matawaka.semantic-provider-boundary-receipt/v0.2",
            ProviderId,
            packet.Schema,
            PclCompatibleProgress.UuAapFrontier,
            observedUuAap,
            string.Equals(observedUuAap, PclCompatibleProgress.UuAapFrontier, StringComparison.OrdinalIgnoreCase),
            bindings,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "no repository mutation",
                "no repository root supplied to semantic provider",
                "no file handle supplied to semantic provider",
                "no network model call",
                "no arbitrary process execution",
                "no materialization authority created",
                "no execution authority created",
                "no ActionPermit created",
                "no canonical UU-AAP conformance claim from this adapter",
                "no hidden reasoning disclosure"
            });

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.completed",
            98,
            proposal.Title,
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "PROPOSAL_BOUND",
            "NONE",
            "AGENT_CHECKPOINT_READY",
            $"semantic:{packet.CommandId}"));

        return Task.FromResult(new SemanticProviderResult(
            proposal,
            boundary,
            "Deterministic v0.2 provider demonstrates the interchangeable evidence-only boundary; it is not an LLM and makes no network call."));
    }
}
