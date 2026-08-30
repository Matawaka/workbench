using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed record SemanticSignal(
    string Id,
    int EvidenceItems,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> Terms);

public sealed record SemanticAnalysisReceipt(
    string Schema,
    string Provider,
    string InputDigest,
    string OutputDigest,
    int EvidenceItemCount,
    int RepositoryCount,
    IReadOnlyList<SemanticSignal> Signals,
    IReadOnlyList<string> Invariants,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderSelectionReceipt(
    string Schema,
    string RegistryVersion,
    string RequestedProvider,
    string SelectedProvider,
    bool ProviderFound,
    bool OfflineOnly,
    bool DynamicProviderLoadingAllowed,
    string IsolationLevel,
    IReadOnlyList<string> AvailableProviders,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderBoundaryReceipt(
    string Schema,
    string Provider,
    string RegistryVersion,
    string InputSchema,
    string InputDigest,
    string OutputDigest,
    string ExpectedUuAapFrontier,
    string? ObservedUuAapFrontier,
    bool SourceFrontierMatched,
    IReadOnlyList<ProtocolSourceBinding> SourceBindings,
    bool OfflineOnly,
    bool DynamicProviderLoadingAllowed,
    string IsolationLevel,
    bool RepositoryRootsProvided,
    bool FileHandlesProvided,
    bool ArbitraryProcessExecutionProvided,
    bool NetworkAccessProvided,
    bool MutationAuthorityProvided,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderResult(
    AgentProposal Proposal,
    SemanticProviderBoundaryReceipt Boundary,
    SemanticAnalysisReceipt Analysis,
    string Note);

public interface ISemanticProvider
{
    string ProviderId { get; }

    Task<SemanticProviderResult> AnalyzeAsync(
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ISemanticProviderRegistry
{
    string RegistryVersion { get; }
    IReadOnlyList<string> ProviderIds { get; }
    SemanticProviderSelectionReceipt Select(string? requestedProvider);
    ISemanticProvider Resolve(SemanticProviderSelectionReceipt selection);
}

/// <summary>
/// Workbench-local registry proving provider substitution at one sanitized input
/// boundary. It is not a UU-AAP Stable Core admission and does not create
/// provider, network, filesystem, process, or mutation authority.
/// </summary>
public sealed class SemanticProviderRegistry : ISemanticProviderRegistry
{
    public const string Version = "workbench-local-semantic-provider-registry/v0.3";

    private readonly IReadOnlyDictionary<string, ISemanticProvider> _providers;

    public SemanticProviderRegistry(IEnumerable<ISemanticProvider>? providers = null)
    {
        var selected = (providers ?? new ISemanticProvider[]
        {
            new LocalContractSynthesisProvider(),
            new DeterministicSemanticProvider()
        }).ToArray();

        _providers = selected.ToDictionary(item => item.ProviderId, StringComparer.OrdinalIgnoreCase);
        ProviderIds = _providers.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string RegistryVersion => Version;
    public IReadOnlyList<string> ProviderIds { get; }

    public SemanticProviderSelectionReceipt Select(string? requestedProvider)
    {
        var requested = string.IsNullOrWhiteSpace(requestedProvider)
            ? LocalContractSynthesisProvider.Id
            : requestedProvider.Trim();

        var found = _providers.TryGetValue(requested, out var provider);
        if (!found || provider is null)
            throw new InvalidDataException(
                $"Unknown semanticProvider '{requested}'. Available: {string.Join(", ", ProviderIds)}");

        return new SemanticProviderSelectionReceipt(
            "matawaka.semantic-provider-selection-receipt/v0.3",
            RegistryVersion,
            requested,
            provider.ProviderId,
            true,
            true,
            false,
            "in-process built-in provider boundary; not an OS sandbox",
            ProviderIds,
            SemanticProviderSupport.ProviderNonEffects);
    }

    public ISemanticProvider Resolve(SemanticProviderSelectionReceipt selection)
    {
        if (!selection.ProviderFound ||
            !_providers.TryGetValue(selection.SelectedProvider, out var provider))
            throw new InvalidDataException($"Semantic provider is not available: {selection.SelectedProvider}");

        return provider;
    }
}

public static class SemanticProviderSupport
{
    public static readonly string[] ProviderNonEffects =
    [
        "no repository mutation",
        "no repository root supplied to semantic provider",
        "no file handle supplied to semantic provider",
        "no network model call",
        "no arbitrary process execution",
        "no materialization authority created",
        "no execution authority created",
        "no ActionPermit created",
        "no provider self-selection after authority decision",
        "no dynamic provider assembly/path loading from JSON",
        "no Stable Core or interface-registry promotion",
        "no canonical UU-AAP conformance claim from this adapter",
        "no hidden reasoning disclosure"
    ];

    public static string ComputeInputDigest(SemanticEvidencePacket packet)
    {
        var projection = new
        {
            target = packet.Target,
            repositories = packet.Repositories.Select(item => new
            {
                item.Name,
                item.Branch,
                item.Head,
                item.SelectedEvidenceItems,
                item.TopTerms
            }),
            coverage = packet.Coverage,
            evidence = packet.Evidence,
            authority = new
            {
                packet.AuthorityReceipt.Request.Capability,
                packet.AuthorityReceipt.Request.Operation,
                packet.AuthorityReceipt.Request.Target,
                packet.AuthorityReceipt.Decision.Decision,
                packet.AuthorityReceipt.Decision.AuthorityGranted,
                packet.AuthorityReceipt.Decision.MutationBudgetGranted,
                packet.AuthorityReceipt.Decision.NetworkAccessGranted,
                packet.AuthorityReceipt.Decision.ArbitraryProcessExecutionGranted
            }
        };

        return Digest(projection);
    }

    public static string ComputeOutputDigest(AgentProposal proposal, IReadOnlyList<SemanticSignal> signals)
        => Digest(new { proposal, signals });

    public static IReadOnlyList<SemanticSignal> BuildSignals(SemanticEvidencePacket packet)
    {
        var definitions = new[]
        {
            new { Id = "AUTHORITY_BOUNDARY", Terms = new[] { "authority", "capability" } },
            new { Id = "EVIDENCE_PROVENANCE", Terms = new[] { "evidence", "receipt" } },
            new { Id = "POSSIBILITY_INTENT", Terms = new[] { "intent", "availability", "possibility" } },
            new { Id = "NON_BINDING_ATTENTION", Terms = new[] { "companion", "solver", "hint", "attention" } },
            new { Id = "REVERSIBILITY", Terms = new[] { "reversible" } }
        };

        var signals = new List<SemanticSignal>();
        foreach (var definition in definitions)
        {
            var matched = packet.Evidence
                .Where(item => item.Terms.Any(term =>
                    definition.Terms.Contains(term, StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            if (matched.Length == 0) continue;

            signals.Add(new SemanticSignal(
                definition.Id,
                matched.Length,
                matched.Select(item => item.Repository)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                matched.SelectMany(item => item.Terms)
                    .Where(term => definition.Terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray()));
        }

        return signals
            .OrderByDescending(item => item.EvidenceItems)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string RequireExactSourceFrontier(SemanticEvidencePacket packet)
    {
        var observed = packet.Repositories
            .FirstOrDefault(item => string.Equals(item.Name, "uu-aap", StringComparison.OrdinalIgnoreCase))
            ?.Head;

        if (!string.Equals(observed, PclCompatibleProgress.UuAapFrontier, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Semantic source frontier mismatch. Expected uu-aap {PclCompatibleProgress.UuAapFrontier}, observed {observed ?? "<missing>"}.");

        return observed!;
    }

    public static SemanticProviderBoundaryReceipt BuildBoundary(
        ISemanticProvider provider,
        SemanticEvidencePacket packet,
        string inputDigest,
        string outputDigest,
        string observedUuAap)
    {
        var bindings = new[]
        {
            PclCompatibleProgress.ProgressSource,
            PclCompatibleProgress.HumanViewSource,
            PclCompatibleProgress.ScopedAuthoritySource,
            PclCompatibleProgress.MaterializationAuthoritySource,
            PclCompatibleProgress.ReusableAdmissionAuditSource
        };

        return new SemanticProviderBoundaryReceipt(
            "matawaka.semantic-provider-boundary-receipt/v0.3",
            provider.ProviderId,
            SemanticProviderRegistry.Version,
            packet.Schema,
            inputDigest,
            outputDigest,
            PclCompatibleProgress.UuAapFrontier,
            observedUuAap,
            true,
            bindings,
            true,
            false,
            "in-process built-in provider boundary; not an OS sandbox",
            false,
            false,
            false,
            false,
            false,
            ProviderNonEffects);
    }

    public static IReadOnlyList<string> CommonInvariants =>
    [
        "Evidence != Authority",
        "Provider Selection != Authority Grant",
        "Proposal != Materialization",
        "Scoped Authority Evidence != Materialization Authority",
        "Materialization Authority != Execution Authority",
        "Supported Evidence != ActionPermit",
        "Semantic Similarity != Stable Core Admission",
        "Visible Progress != Hidden Reasoning"
    ];

    private static string Digest(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

/// <summary>
/// v0.2-compatible deterministic provider retained as an alternate provider so
/// v0.3 can prove substitution without changing the evidence-collection path.
/// </summary>
public sealed class DeterministicSemanticProvider : ISemanticProvider
{
    public const string Id = "deterministic-evidence-semantic-v0.2";
    public string ProviderId => Id;

    public Task<SemanticProviderResult> AnalyzeAsync(
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedUuAap = SemanticProviderSupport.RequireExactSourceFrontier(packet);
        var inputDigest = SemanticProviderSupport.ComputeInputDigest(packet);

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.started",
            94,
            $"{ProviderId} receives {packet.Evidence.Count} sanitized evidence items; offline only",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "EVIDENCE_BOUND_PROPOSAL_DERIVATION",
            "NONE",
            "SEMANTIC_PROPOSAL_READY",
            $"semantic:{packet.CommandId}:{ProviderId}"));

        var signals = SemanticProviderSupport.BuildSignals(packet);
        var represented = packet.Repositories.Count(item => item.SelectedEvidenceItems > 0);

        var proposal = new AgentProposal(
            "Evidence-bounded deterministic semantic checkpoint",
            new[]
            {
                $"Preserve the balanced evidence frontier across {represented}/{packet.Repositories.Count} repositories as the causal input.",
                "Keep provider input restricted to sanitized evidence, repository identity/branch/HEAD, coverage, and typed authority receipt.",
                "Keep PCL-compatible liveness visible without exposing hidden reasoning.",
                "Keep scoped authority evidence and materialization authority separate from execution authority.",
                "Keep repository mutation and external model/network calls closed."
            },
            "STOP before repository mutation, external model/network calls, arbitrary process execution, materialization, ActionPermit creation, or self-expansion of authority.");

        var outputDigest = SemanticProviderSupport.ComputeOutputDigest(proposal, signals);
        var analysis = new SemanticAnalysisReceipt(
            "matawaka.semantic-analysis-receipt/v0.3",
            ProviderId,
            inputDigest,
            outputDigest,
            packet.Evidence.Count,
            packet.Repositories.Count,
            signals,
            SemanticProviderSupport.CommonInvariants,
            SemanticProviderSupport.ProviderNonEffects);

        var boundary = SemanticProviderSupport.BuildBoundary(
            this, packet, inputDigest, outputDigest, observedUuAap);

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.completed",
            98,
            $"{ProviderId}: {proposal.Title}",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "PROPOSAL_BOUND",
            "NONE",
            "AGENT_CHECKPOINT_READY",
            $"semantic:{packet.CommandId}:{ProviderId}"));

        return Task.FromResult(new SemanticProviderResult(
            proposal,
            boundary,
            analysis,
            "Deterministic v0.2 provider executed through the v0.3 provider registry. It remains offline and receives only the sanitized semantic evidence packet."));
    }
}

/// <summary>
/// First new provider behind the interchangeable boundary. It performs local,
/// categorical synthesis over the sanitized evidence packet. It has no direct
/// repository, file, process, network, materialization, or execution access.
/// </summary>
public sealed class LocalContractSynthesisProvider : ISemanticProvider
{
    public const string Id = "local-contract-synthesis-v0.3";
    public string ProviderId => Id;

    public Task<SemanticProviderResult> AnalyzeAsync(
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedUuAap = SemanticProviderSupport.RequireExactSourceFrontier(packet);
        var inputDigest = SemanticProviderSupport.ComputeInputDigest(packet);

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.started",
            94,
            $"{ProviderId} receives digest {inputDigest[..12]}… from {packet.Evidence.Count} sanitized evidence items",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "LOCAL_CONTRACT_SYNTHESIS",
            "NONE",
            "SEMANTIC_PROPOSAL_READY",
            $"semantic:{packet.CommandId}:{ProviderId}"));

        var signals = SemanticProviderSupport.BuildSignals(packet);
        var actions = new List<string>
        {
            $"Bind this proposal to semantic input digest {inputDigest}.",
            $"Preserve balanced representation across {packet.Coverage.RepositoriesRepresented}/{packet.Repositories.Count} repositories.",
            "Keep this provider Workbench-local; repeated provider mechanics do not establish a reusable UU-AAP component or Stable Core admission."
        };

        foreach (var signal in signals.Take(4))
        {
            actions.Add(signal.Id switch
            {
                "AUTHORITY_BOUNDARY" =>
                    $"Authority boundary is evidenced in {signal.Repositories.Count} repositories; keep capability/authority claims typed and fail-closed.",
                "EVIDENCE_PROVENANCE" =>
                    $"Evidence/provenance surfaces are evidenced in {signal.Repositories.Count} repositories; preserve receipt and frontier references independently of proposal text.",
                "POSSIBILITY_INTENT" =>
                    $"Possibility/availability/intent distinctions are evidenced in {signal.Repositories.Count} repositories; do not collapse availability into intent or authority.",
                "NON_BINDING_ATTENTION" =>
                    $"Non-binding attention/companion terms are evidenced in {signal.Repositories.Count} repositories; preserve hint/attention as non-instructional candidates.",
                "REVERSIBILITY" =>
                    $"Reversibility is evidenced in {signal.Repositories.Count} repositories; prefer reversible successor proposals and keep materialization separately authorized.",
                _ => $"Preserve signal {signal.Id} as a bounded evidence-backed candidate."
            });
        }

        actions.Add("Do not infer materialization, execution, ActionPermit, canonicality, or external-effect authority from semantic synthesis.");

        var proposal = new AgentProposal(
            "Local evidence-bounded contract synthesis checkpoint",
            actions,
            "STOP at proposal. A later successor/materialization path requires fresh scoped authority evidence and a separate materialization-authority evaluation; execution remains closed.");

        var outputDigest = SemanticProviderSupport.ComputeOutputDigest(proposal, signals);
        var analysis = new SemanticAnalysisReceipt(
            "matawaka.semantic-analysis-receipt/v0.3",
            ProviderId,
            inputDigest,
            outputDigest,
            packet.Evidence.Count,
            packet.Repositories.Count,
            signals,
            SemanticProviderSupport.CommonInvariants,
            SemanticProviderSupport.ProviderNonEffects);

        var boundary = SemanticProviderSupport.BuildBoundary(
            this, packet, inputDigest, outputDigest, observedUuAap);

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.completed",
            98,
            $"{ProviderId}: {signals.Count} bounded semantic signals; output {outputDigest[..12]}…",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "LOCAL_SYNTHESIS_BOUND",
            "NONE",
            "AGENT_CHECKPOINT_READY",
            $"semantic:{packet.CommandId}:{ProviderId}"));

        return Task.FromResult(new SemanticProviderResult(
            proposal,
            boundary,
            analysis,
            "Local contract synthesis is deterministic, categorical and offline. It proves provider substitution at the sanitized boundary; it is not an LLM and does not establish a reusable UU-AAP component."));
    }
}
