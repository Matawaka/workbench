using System.Text;
using System.Text.Json;
using Matawaka.Workbench.AgentHost;

namespace Matawaka.Workbench.SemanticHost;

internal static class Program
{
    private const int MaxInputBytes = ProcessSemanticProviderClient.MaxInputBytes;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !string.Equals(args[0], "--stdio-v0.5", StringComparison.Ordinal))
            return await FailAsync("semantic host requires --stdio-v0.5");

        try
        {
            var input = await Console.In.ReadToEndAsync();
            if (Encoding.UTF8.GetByteCount(input) > MaxInputBytes)
                return await FailAsync($"semantic host input exceeds {MaxInputBytes} bytes");

            var request = JsonSerializer.Deserialize<SemanticHostRequest>(input, JsonOptions)
                ?? throw new InvalidDataException("semantic host request is empty");

            if (!string.Equals(request.Schema, "matawaka.semantic-host-request/v0.5", StringComparison.Ordinal))
                throw new InvalidDataException("unsupported semantic host request schema");

            if (!SemanticProviderCatalog.ProviderIds.Contains(request.Provider, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("requested provider is not built into this semantic host");

            var recomputedInput = SemanticProviderSupport.ComputeInputDigest(request.Packet);
            if (!string.Equals(recomputedInput, request.InputDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("semantic host input digest mismatch");

            SemanticHostComputation computation = request.Provider switch
            {
                SemanticProviderCatalog.LocalContractSynthesisId => AnalyzeLocalContractSynthesis(request.Packet),
                SemanticProviderCatalog.DeterministicEvidenceId => AnalyzeDeterministic(request.Packet),
                _ => throw new InvalidDataException("unsupported built-in provider")
            };

            var outputDigest = SemanticProviderSupport.ComputeOutputDigest(
                computation.Proposal,
                computation.Signals);

            var response = new SemanticHostResponse(
                "matawaka.semantic-host-response/v0.5",
                true,
                request.Provider,
                request.InputDigest,
                outputDigest,
                computation.Proposal,
                computation.Signals,
                computation.Note,
                null);

            await Console.Out.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            return await FailAsync(ex.Message);
        }
    }

    private static SemanticHostComputation AnalyzeDeterministic(SemanticEvidencePacket packet)
    {
        SemanticProviderSupport.RequireExactSourceFrontier(packet);
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

        return new SemanticHostComputation(
            proposal,
            signals,
            "Deterministic v0.2 provider executed inside the fixed v0.5 semantic host process. The provider logic is offline and receives only the sanitized semantic evidence packet.");
    }

    private static SemanticHostComputation AnalyzeLocalContractSynthesis(SemanticEvidencePacket packet)
    {
        SemanticProviderSupport.RequireExactSourceFrontier(packet);
        var inputDigest = SemanticProviderSupport.ComputeInputDigest(packet);
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

        return new SemanticHostComputation(
            proposal,
            signals,
            "Local contract synthesis executed inside the fixed v0.5 semantic host process. It remains deterministic, categorical and offline; Job Object containment and separate process isolation are not represented as an OS sandbox.");
    }

    private static async Task<int> FailAsync(string message)
    {
        var response = new SemanticHostResponse(
            "matawaka.semantic-host-response/v0.5",
            false,
            string.Empty,
            string.Empty,
            null,
            null,
            Array.Empty<SemanticSignal>(),
            null,
            message);

        await Console.Out.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
        return 2;
    }

    private sealed record SemanticHostComputation(
        AgentProposal Proposal,
        IReadOnlyList<SemanticSignal> Signals,
        string Note);
}
