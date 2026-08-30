using System.Text.Json;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

public sealed record AgentEvidence(
    string Repository,
    string File,
    int Line,
    IReadOnlyList<string> Terms,
    string Snippet);

public sealed record AgentRepositoryFinding(
    string Repository,
    string Branch,
    string Head,
    int FilesInspected,
    int CandidateEvidenceItems,
    int SelectedEvidenceItems,
    IReadOnlyList<string> TopTerms);

public sealed record AgentRepositoryCoverage(
    string Repository,
    int FilesInspected,
    int CandidateEvidenceItems,
    int SelectedEvidenceItems);

public sealed record AgentEvidenceCoverage(
    string Strategy,
    int TotalBudget,
    int TotalSelected,
    int RepositoriesWithCandidates,
    int RepositoriesRepresented,
    IReadOnlyList<AgentRepositoryCoverage> Repositories);

public sealed record AgentProposal(
    string Title,
    IReadOnlyList<string> Actions,
    string StopBoundary);

public sealed record DevelopmentAgentReceipt(
    string Provider,
    string Status,
    string Mode,
    string AuthorityUsed,
    CapabilityRequest CapabilityRequest,
    CapabilityDecision CapabilityDecision,
    IReadOnlyList<CatalogRepository> CatalogSnapshot,
    IReadOnlyList<AgentRepositoryFinding> Findings,
    AgentEvidenceCoverage Coverage,
    IReadOnlyList<AgentEvidence> Evidence,
    AgentProposal? Proposal,
    SemanticProviderBoundaryReceipt? SemanticProviderBoundary,
    IReadOnlyList<string> Mutations,
    string Limitation);

public interface IDevelopmentAgentProvider
{
    Task<DevelopmentAgentReceipt> ObserveProposeAsync(
        CommandEnvelope command,
        IReadOnlyList<CatalogRepository> catalog,
        CapabilityRequest capabilityRequest,
        CapabilityDecision capabilityDecision,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ICapabilityPolicy
{
    CapabilityRequest CreateRequest(CommandEnvelope command);
    CapabilityDecision Decide(CapabilityRequest request, bool agentEnabled);
}

/// <summary>
/// Workbench-local bridge inspired by FREESHIELD's authority boundary.
/// It is intentionally not represented as canonical FREESHIELD policy.
/// v0.2 can grant only read-only Observe/Propose authority.
/// </summary>
public sealed class FreeShieldReadOnlyCapabilityPolicy : ICapabilityPolicy
{
    private const string PolicyId = "freeshield-read-only-bridge/v0.2";

    public CapabilityRequest CreateRequest(CommandEnvelope command)
    {
        var mode = ReadString(command.Payload, "mode") ?? "propose";
        var requestedMutationBudget = ReadInt(
            command.Payload,
            "mutationBudget",
            string.Equals(mode, "execute", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            0,
            1000);
        var requestedNetwork = ReadBool(command.Payload, "networkAccess", false);
        var requestedProcess = ReadBool(command.Payload, "arbitraryProcessExecution", false);

        var capability = mode.ToLowerInvariant() switch
        {
            "observe" => "agent.observe",
            "propose" => "agent.propose",
            "execute" => "agent.execute",
            _ => "agent.unknown"
        };

        var requestedAuthority = string.Equals(mode, "execute", StringComparison.OrdinalIgnoreCase)
            ? "repository-mutation"
            : "read-only";

        return new CapabilityRequest(
            "matawaka.capability-request/v1",
            $"{command.Id}:capability",
            "development-agent",
            capability,
            mode,
            command.Target,
            requestedAuthority,
            requestedMutationBudget,
            requestedNetwork,
            requestedProcess);
    }

    public CapabilityDecision Decide(CapabilityRequest request, bool agentEnabled)
    {
        var nonEffects = new[]
        {
            "no repository mutation",
            "no git fetch",
            "no network model call",
            "no arbitrary process execution",
            "no materialization authority created",
            "no execution authority created",
            "no ActionPermit created",
            "no self-expansion of authority"
        };

        if (!agentEnabled)
        {
            return Deny(request, "agent-host-disabled", nonEffects);
        }

        if (!string.Equals(request.Operation, "observe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Operation, "propose", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Operation, "execute", StringComparison.OrdinalIgnoreCase))
        {
            return Deny(request, "unsupported-agent-operation", nonEffects);
        }

        if (string.Equals(request.Operation, "execute", StringComparison.OrdinalIgnoreCase))
        {
            return Deny(request, "execute-not-available-in-v0.2", nonEffects);
        }

        if (request.RequestedMutationBudget != 0 ||
            request.RequestedNetworkAccess ||
            request.RequestedArbitraryProcessExecution ||
            !string.Equals(request.RequestedAuthority, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            return Deny(request, "read-only-operation-requested-extra-authority", nonEffects);
        }

        return new CapabilityDecision(
            "matawaka.capability-decision/v1",
            request.Id,
            "allow",
            PolicyId,
            "read-only",
            0,
            false,
            false,
            new[]
            {
                "agent-host-explicitly-enabled",
                "operation-is-observe-or-propose",
                "requested-mutation-budget-is-zero",
                "network-and-arbitrary-process-access-not-requested"
            },
            nonEffects);
    }

    private static CapabilityDecision Deny(
        CapabilityRequest request,
        string reason,
        IReadOnlyList<string> nonEffects)
        => new(
            "matawaka.capability-decision/v1",
            request.Id,
            "deny",
            PolicyId,
            "none",
            0,
            false,
            false,
            new[] { reason, "deny-wins" },
            nonEffects);

    private static string? ReadString(JsonElement payload, string name)
        => payload.ValueKind == JsonValueKind.Object &&
           payload.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement payload, string name, int fallback, int minimum, int maximum)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed))
            return Math.Clamp(parsed, minimum, maximum);

        return fallback;
    }

    private static bool ReadBool(JsonElement payload, string name, bool fallback)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(name, out var value) &&
            (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            return value.GetBoolean();

        return fallback;
    }
}

public sealed class ReadOnlyDevelopmentProvider : IDevelopmentAgentProvider
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".yaml", ".yml", ".cs", ".csproj", ".sln", ".ps1", ".xml"
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "artifacts", "node_modules", ".venv", "packages"
    };

    private static readonly string[] DefaultFocusRepositories = ["FREESHIELD", "kontur", "uu-aap"];
    private static readonly string[] DefaultTerms =
    [
        "authority", "capability", "evidence", "receipt", "non-effect",
        "successor", "intent", "availability", "possibility", "ccrp",
        "companion", "solver", "hint", "attention", "agent", "reversible"
    ];

    private readonly ISemanticProvider _semanticProvider;

    public ReadOnlyDevelopmentProvider(ISemanticProvider? semanticProvider = null)
    {
        _semanticProvider = semanticProvider ?? new DeterministicSemanticProvider();
    }

    public async Task<DevelopmentAgentReceipt> ObserveProposeAsync(
        CommandEnvelope command,
        IReadOnlyList<CatalogRepository> catalog,
        CapabilityRequest capabilityRequest,
        CapabilityDecision capabilityDecision,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(capabilityDecision.Decision, "allow", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("ReadOnlyDevelopmentProvider requires an allow capability decision.");

        var options = ReadOptions(command.Payload);
        if (!string.Equals(options.Mode, "observe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Mode, "propose", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("agent.run payload.mode must be 'observe' or 'propose' after authority gating.");

        var selected = catalog
            .Where(repo => options.FocusRepositories.Count == 0 ||
                           options.FocusRepositories.Contains(repo.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (selected.Length == 0)
            throw new InvalidDataException("No focus repositories were found in the selected Matawaka catalog.");

        var candidatesByRepository = new Dictionary<string, List<AgentEvidence>>(StringComparer.OrdinalIgnoreCase);
        var filesInspectedByRepository = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        progress?.Report(new WorkbenchProgress(
            command.Id, "agent.observe.started", 0,
            $"Balanced read-only evidence scan: {selected.Length} repositories", DateTimeOffset.Now,
            "EVIDENCE_COLLECTION", "OBSERVATION_BOUND", "LOCAL_CATALOG",
            "REPOSITORY_EVIDENCE", $"catalog:{command.Id}"));

        for (var repoIndex = 0; repoIndex < selected.Length; repoIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repo = selected[repoIndex];
            progress?.Report(new WorkbenchProgress(
                command.Id, "agent.repository.started",
                ScanPercent(repoIndex, selected.Length), repo.Name, DateTimeOffset.Now,
                "EVIDENCE_COLLECTION", "REPOSITORY_SCAN", repo.Name,
                "REPOSITORY_EVIDENCE", $"repo:{repo.Name}:{repo.Head}"));

            var inspected = 0;
            var repoCandidates = new List<AgentEvidence>();

            foreach (var file in EnumerateCandidateFiles(repo.Root).Take(options.MaxFilesPerRepository))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inspected++;

                if (repoCandidates.Count >= options.MaxEvidenceItems)
                    break;

                IReadOnlyList<string> lines;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > 1_000_000) continue;
                    lines = await File.ReadAllLinesAsync(file, cancellationToken);
                }
                catch
                {
                    continue;
                }

                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = lines[lineIndex];
                    var matched = options.Terms
                        .Where(term => line.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (matched.Length == 0) continue;

                    repoCandidates.Add(new AgentEvidence(
                        repo.Name,
                        Path.GetRelativePath(repo.Root, file),
                        lineIndex + 1,
                        matched,
                        Compact(line)));

                    if (repoCandidates.Count >= options.MaxEvidenceItems) break;
                }
            }

            candidatesByRepository[repo.Name] = repoCandidates;
            filesInspectedByRepository[repo.Name] = inspected;

            progress?.Report(new WorkbenchProgress(
                command.Id, "agent.repository.completed",
                ScanPercent(repoIndex + 1, selected.Length),
                $"{repo.Name}: {repoCandidates.Count} candidates from {inspected} files",
                DateTimeOffset.Now,
                "EVIDENCE_COLLECTION", "REPOSITORY_EVIDENCE_BOUND", "LOCAL_CATALOG",
                "NEXT_REPOSITORY_OR_FRONTIER", $"repo:{repo.Name}:{repo.Head}"));
        }

        var evidence = SelectBalancedEvidence(selected, candidatesByRepository, options.MaxEvidenceItems);
        var selectedByRepository = evidence
            .GroupBy(item => item.Repository, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var findings = selected.Select(repo =>
        {
            var selectedEvidence = selectedByRepository.TryGetValue(repo.Name, out var selectedItems)
                ? selectedItems
                : Array.Empty<AgentEvidence>();
            var termCounts = selectedEvidence
                .SelectMany(item => item.Terms)
                .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Term = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Term, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(item => item.Term)
                .ToArray();

            return new AgentRepositoryFinding(
                repo.Name,
                repo.Branch,
                repo.Head,
                filesInspectedByRepository.GetValueOrDefault(repo.Name),
                candidatesByRepository.GetValueOrDefault(repo.Name)?.Count ?? 0,
                selectedEvidence.Length,
                termCounts);
        }).ToArray();

        var coverage = new AgentEvidenceCoverage(
            "deterministic-round-robin-by-repository/v1",
            options.MaxEvidenceItems,
            evidence.Count,
            candidatesByRepository.Count(pair => pair.Value.Count > 0),
            findings.Count(item => item.SelectedEvidenceItems > 0),
            findings.Select(item => new AgentRepositoryCoverage(
                item.Repository,
                item.FilesInspected,
                item.CandidateEvidenceItems,
                item.SelectedEvidenceItems)).ToArray());

        progress?.Report(new WorkbenchProgress(
            command.Id, "agent.evidence.selected", 91,
            $"Balanced frontier: {coverage.TotalSelected}/{coverage.TotalBudget} items across {coverage.RepositoriesRepresented}/{selected.Length} repositories",
            DateTimeOffset.Now,
            "EVIDENCE_FRONTIER", "EVIDENCE_BOUND", "NONE",
            "SEMANTIC_PROVIDER", $"evidence:{command.Id}"));

        AgentProposal? proposal = null;
        SemanticProviderBoundaryReceipt? semanticBoundary = null;
        if (string.Equals(options.Mode, "propose", StringComparison.OrdinalIgnoreCase))
        {
            var authorityReceipt = new CapabilityReceipt(
                "matawaka.capability-receipt/v1",
                capabilityRequest,
                capabilityDecision);

            var packet = new SemanticEvidencePacket(
                "matawaka.semantic-evidence-packet/v0.2",
                command.Id,
                command.Target,
                findings.Select(item => new SemanticRepositoryRef(
                    item.Repository,
                    item.Branch,
                    item.Head,
                    item.SelectedEvidenceItems,
                    item.TopTerms)).ToArray(),
                coverage,
                evidence,
                authorityReceipt);

            var semantic = await _semanticProvider.AnalyzeAsync(
                packet,
                progress,
                cancellationToken);

            proposal = semantic.Proposal;
            semanticBoundary = semantic.Boundary;
        }

        return new DevelopmentAgentReceipt(
            "deterministic-read-only-v0.2",
            "completed",
            options.Mode,
            capabilityDecision.AuthorityGranted,
            capabilityRequest,
            capabilityDecision,
            selected,
            findings,
            coverage,
            evidence,
            proposal,
            semanticBoundary,
            Array.Empty<string>(),
            "Evidence collection remains deterministic and read-only. Proposal derivation is now behind an interchangeable semantic-provider interface that receives only an evidence packet plus typed authority receipt, not repository roots, file handles, process execution, network access, or mutation authority. UU-AAP protocol bindings are exact-frontier references and do not claim canonical implementation execution.");
    }

    private static IReadOnlyList<AgentEvidence> SelectBalancedEvidence(
        IReadOnlyList<CatalogRepository> repositories,
        IReadOnlyDictionary<string, List<AgentEvidence>> candidates,
        int totalLimit)
    {
        var selected = new List<AgentEvidence>(Math.Min(totalLimit, candidates.Sum(pair => pair.Value.Count)));
        var positions = new int[repositories.Count];

        while (selected.Count < totalLimit)
        {
            var addedAny = false;
            for (var index = 0; index < repositories.Count && selected.Count < totalLimit; index++)
            {
                var repository = repositories[index];
                if (!candidates.TryGetValue(repository.Name, out var repoCandidates)) continue;
                if (positions[index] >= repoCandidates.Count) continue;

                selected.Add(repoCandidates[positions[index]]);
                positions[index]++;
                addedAny = true;
            }

            if (!addedAny) break;
        }

        return selected;
    }

    private static AgentProposal BuildProposal(
        IReadOnlyList<AgentRepositoryFinding> findings,
        AgentEvidenceCoverage coverage)
    {
        var focusCount = findings.Count;
        var actions = new List<string>
        {
            "Preserve the repository+branch+HEAD snapshot and balanced evidence coverage as the frontier for the next change.",
            "Use the typed capability request/decision as the authority receipt for this checkpoint rather than treating the UI enablement as authority by itself.",
            "Review reusable contract candidates across FREESHIELD, kontur, and uu-aap before introducing an interchangeable semantic provider.",
            "Keep Observe/Propose read-only; any future Execute must use a separately granted mutation capability and explicit bounded mutation budget."
        };

        if (coverage.TotalSelected == 0)
            actions.Insert(0, "Broaden the evidence vocabulary or focus files; the deterministic scan found no matching contract evidence.");
        else
            actions.Insert(0,
                $"Normalize {coverage.TotalSelected} balanced evidence anchors from {coverage.RepositoriesRepresented}/{focusCount} focus repositories into typed reusable contract candidates.");

        return new AgentProposal(
            "Balanced read-only typed-authority checkpoint",
            actions,
            "STOP before repository mutation, git fetch, network model calls, arbitrary process execution, or self-expansion of authority.");
    }

    private static AgentOptions ReadOptions(JsonElement payload)
    {
        var mode = ReadString(payload, "mode") ?? "propose";
        var focus = ReadStringArray(payload, "focusRepositories");
        if (focus.Count == 0) focus = DefaultFocusRepositories;
        if (focus.Count > 32)
            throw new InvalidDataException("agent.run focusRepositories is bounded to 32 repositories per checkpoint.");

        var terms = ReadStringArray(payload, "terms");
        if (terms.Count == 0) terms = DefaultTerms;
        if (terms.Count > 64)
            throw new InvalidDataException("agent.run terms is bounded to 64 terms per checkpoint.");

        var maxFiles = ReadInt(payload, "maxFilesPerRepository", 160, 1, 1000);
        var maxEvidence = ReadInt(payload, "maxEvidenceItems", 80, 1, 500);

        return new AgentOptions(mode, focus, terms, maxFiles, maxEvidence);
    }

    private static string? ReadString(JsonElement payload, string name)
        => payload.ValueKind == JsonValueKind.Object &&
           payload.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ReadInt(JsonElement payload, string name, int fallback, int minimum, int maximum)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed))
            return Math.Clamp(parsed, minimum, maximum);

        return fallback;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> subdirectories;
            IEnumerable<string> files;

            try
            {
                subdirectories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in subdirectories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }

            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (TextExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }
    }

    private static string Compact(string value)
    {
        var normalized = value.Replace('\t', ' ').Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized.Length <= 240 ? normalized : normalized[..237] + "...";
    }

    private static int ScanPercent(int value, int total)
        => total == 0 ? 90 : (int)Math.Round(value * 90d / total);

    private sealed record AgentOptions(
        string Mode,
        IReadOnlyList<string> FocusRepositories,
        IReadOnlyList<string> Terms,
        int MaxFilesPerRepository,
        int MaxEvidenceItems);
}

public sealed class DevelopmentAgentHost
{
    private readonly IDevelopmentAgentProvider _provider;
    private readonly ICapabilityPolicy _capabilityPolicy;

    public DevelopmentAgentHost(
        IDevelopmentAgentProvider? provider = null,
        ICapabilityPolicy? capabilityPolicy = null)
    {
        _provider = provider ?? new ReadOnlyDevelopmentProvider();
        _capabilityPolicy = capabilityPolicy ?? new FreeShieldReadOnlyCapabilityPolicy();
    }

    public async Task<DevelopmentAgentReceipt> RunAsync(
        CommandEnvelope command,
        IReadOnlyList<CatalogRepository> catalog,
        bool enabled,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new WorkbenchProgress(
            command.Id, "agent.started", 0, command.Target, DateTimeOffset.Now,
            "AUTHORITY_GATE", "RUN_STARTED", "AUTHORITY_DECISION",
            "AUTHORITY_DECISION", $"authority:{command.Id}"));

        var request = _capabilityPolicy.CreateRequest(command);
        progress?.Report(new WorkbenchProgress(
            command.Id, "authority.requested", 0,
            $"{request.Capability}; authority={request.RequestedAuthority}; mutationBudget={request.RequestedMutationBudget}",
            DateTimeOffset.Now,
            "AUTHORITY_GATE", "AUTHORITY_REQUEST_BOUND", "AUTHORITY_DECISION",
            "AUTHORITY_DECISION", request.Id));

        var decision = _capabilityPolicy.Decide(request, enabled);
        progress?.Report(new WorkbenchProgress(
            command.Id, "authority.decided", 0,
            $"{decision.Decision}; authority={decision.AuthorityGranted}; mutationBudget={decision.MutationBudgetGranted}; policy={decision.Policy}",
            DateTimeOffset.Now,
            "AUTHORITY_GATE", "AUTHORITY_DECISION_BOUND", "NONE",
            string.Equals(decision.Decision, "allow", StringComparison.OrdinalIgnoreCase) ? "EVIDENCE_COLLECTION" : "STOP_DENIED",
            request.Id));

        if (!string.Equals(decision.Decision, "allow", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new WorkbenchProgress(
                command.Id, "agent.denied", 100,
                string.Join(", ", decision.Reasons), DateTimeOffset.Now,
                "TERMINAL", "DENIED", "NONE", "NONE", request.Id));

            return new DevelopmentAgentReceipt(
                "authority-gate-v0.2",
                "denied",
                request.Operation,
                "none",
                request,
                decision,
                catalog,
                Array.Empty<AgentRepositoryFinding>(),
                new AgentEvidenceCoverage(
                    "not-run-authority-denied",
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<AgentRepositoryCoverage>()),
                Array.Empty<AgentEvidence>(),
                null,
                null,
                Array.Empty<string>(),
                "The provider was not invoked because the typed authority decision denied the request. No repository writes, network actions, git fetch, arbitrary processes, materialization authority, execution authority, or ActionPermit were created.");
        }

        var receipt = await _provider.ObserveProposeAsync(
            command,
            catalog,
            request,
            decision,
            progress,
            cancellationToken);

        progress?.Report(new WorkbenchProgress(
            command.Id, "agent.completed", 100,
            $"{receipt.Mode} completed; authority={receipt.AuthorityUsed}; evidence={receipt.Evidence.Count}; mutations={receipt.Mutations.Count}",
            DateTimeOffset.Now,
            "TERMINAL", "COMPLETED", "NONE", "NONE", $"agent:{command.Id}"));

        return receipt;
    }
}
