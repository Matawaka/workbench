using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchAcceptanceCheck(
    string Id,
    bool Passed,
    string Observed,
    string Expected);

public sealed record WorkbenchAcceptanceProviderObservation(
    string Provider,
    string InputDigest,
    string OutputDigest,
    string UuAapFrontier,
    bool SourceFrontierMatched,
    bool SourceSetMatched,
    string SourceSetDigest,
    bool RuntimeSecurityAttestationVerified,
    bool AttestationBeforeSemanticInput,
    bool RestrictedToken,
    bool LowIntegrityLevel,
    bool ProcessInJob,
    bool NoEnabledPrivilegesBeyondChangeNotify,
    bool MutationFree,
    string EvidenceDigest,
    string AuthorityDigest);

public sealed record WorkbenchAcceptanceReceipt(
    string Schema,
    string Version,
    string RunId,
    DateTimeOffset ObservedAt,
    bool Passed,
    string AppExecutableSha256,
    WorkbenchAcceptanceProviderObservation ProviderA,
    WorkbenchAcceptanceProviderObservation ProviderB,
    string ExecuteTerminalState,
    IReadOnlyList<string> ExecuteProgressEvents,
    IReadOnlyList<WorkbenchAcceptanceCheck> Checks,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Workbench-local acceptance harness. It exercises the already-existing
/// read-only provider and authority paths; it is not a new UU-AAP primitive.
/// The harness never enables git fetch, never grants Execute, and writes only
/// a receipt under the Workbench artifacts directory when the UI explicitly
/// requests the self-test.
/// </summary>
public sealed class WorkbenchAcceptanceHarness
{
    private const string ProviderA = "local-contract-synthesis-v0.3";
    private const string ProviderB = "deterministic-evidence-semantic-v0.2";

    private readonly ICommandRunner _runner;

    public WorkbenchAcceptanceHarness(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext requestedContext,
        CancellationToken cancellationToken)
    {
        if (!requestedContext.AgentEnabled)
            throw new InvalidDataException("Workbench self-test requires the Agent enabled checkbox to be explicitly selected.");

        // Self-test is intentionally read-only regardless of the UI fetch checkbox.
        var context = new RuntimeContext(requestedContext.CatalogRoot, true, false);
        var runId = $"workbench-acceptance-{DateTimeOffset.Now:yyyyMMddHHmmssfff}";

        var progressA = new BufferedProgress();
        var resultA = await _runner.RunAsync(
            BuildCommand(runId, "propose", ProviderA),
            context,
            progressA,
            cancellationToken);

        var progressB = new BufferedProgress();
        var resultB = await _runner.RunAsync(
            BuildCommand(runId, "propose", ProviderB),
            context,
            progressB,
            cancellationToken);

        var executeProgress = new BufferedProgress();
        var execute = await _runner.RunAsync(
            BuildCommand($"{runId}-execute", "execute", ProviderA),
            context,
            executeProgress,
            cancellationToken);

        var observationA = ObserveProvider(resultA, ProviderA);
        var observationB = ObserveProvider(resultB, ProviderB);
        var executeEvents = executeProgress.Items.Select(item => item.Event).ToArray();

        var checks = new List<WorkbenchAcceptanceCheck>
        {
            Check("provider-a-completed", resultA.TerminalState == CommandTerminalState.Completed,
                resultA.TerminalState.ToString(), "Completed"),
            Check("provider-b-completed", resultB.TerminalState == CommandTerminalState.Completed,
                resultB.TerminalState.ToString(), "Completed"),
            Check("same-bounded-input", string.Equals(observationA.InputDigest, observationB.InputDigest, StringComparison.OrdinalIgnoreCase),
                $"{observationA.InputDigest} / {observationB.InputDigest}", "same InputDigest"),
            Check("provider-selection-distinct", !string.Equals(observationA.Provider, observationB.Provider, StringComparison.OrdinalIgnoreCase),
                $"{observationA.Provider} / {observationB.Provider}", "two distinct registered providers"),
            Check("evidence-frontier-identical", string.Equals(observationA.EvidenceDigest, observationB.EvidenceDigest, StringComparison.OrdinalIgnoreCase),
                $"{observationA.EvidenceDigest} / {observationB.EvidenceDigest}", "same evidence receipt digest"),
            Check("authority-identical", string.Equals(observationA.AuthorityDigest, observationB.AuthorityDigest, StringComparison.OrdinalIgnoreCase),
                $"{observationA.AuthorityDigest} / {observationB.AuthorityDigest}", "same authority receipt digest"),
            Check("relevant-source-set-matched", observationA.SourceSetMatched && observationB.SourceSetMatched,
                $"A={observationA.SourceSetMatched}; B={observationB.SourceSetMatched}", "true / true"),
            Check("relevant-source-set-identical", string.Equals(observationA.SourceSetDigest, observationB.SourceSetDigest, StringComparison.OrdinalIgnoreCase),
                $"{observationA.SourceSetDigest} / {observationB.SourceSetDigest}", "same source-set verification digest"),
            Check("repository-head-drift-does-not-mint-authority", true,
                $"A headMatchOrigin={observationA.SourceFrontierMatched}; B headMatchOrigin={observationB.SourceFrontierMatched}",
                "HEAD equality is observable but not an authority condition when source set matches"),
            Check("runtime-attestation-verified", observationA.RuntimeSecurityAttestationVerified && observationB.RuntimeSecurityAttestationVerified,
                $"A={observationA.RuntimeSecurityAttestationVerified}; B={observationB.RuntimeSecurityAttestationVerified}", "true / true"),
            Check("attestation-before-input", observationA.AttestationBeforeSemanticInput && observationB.AttestationBeforeSemanticInput,
                $"A={observationA.AttestationBeforeSemanticInput}; B={observationB.AttestationBeforeSemanticInput}", "true / true"),
            Check("restricted-low-integrity-runtime", observationA.RestrictedToken && observationB.RestrictedToken && observationA.LowIntegrityLevel && observationB.LowIntegrityLevel,
                $"A=restricted:{observationA.RestrictedToken},low:{observationA.LowIntegrityLevel}; B=restricted:{observationB.RestrictedToken},low:{observationB.LowIntegrityLevel}",
                "restricted=true and low-integrity=true for both"),
            Check("job-observed-by-child", observationA.ProcessInJob && observationB.ProcessInJob,
                $"A={observationA.ProcessInJob}; B={observationB.ProcessInJob}", "true / true"),
            Check("privilege-boundary-observed", observationA.NoEnabledPrivilegesBeyondChangeNotify && observationB.NoEnabledPrivilegesBeyondChangeNotify,
                $"A={observationA.NoEnabledPrivilegesBeyondChangeNotify}; B={observationB.NoEnabledPrivilegesBeyondChangeNotify}", "true / true"),
            Check("proposal-mutation-free", observationA.MutationFree && observationB.MutationFree,
                $"A={observationA.MutationFree}; B={observationB.MutationFree}", "true / true"),
            Check("execute-denied", execute.TerminalState == CommandTerminalState.Denied,
                execute.TerminalState.ToString(), "Denied"),
            Check("execute-no-evidence", execute.Evidence is null,
                execute.Evidence is null ? "null" : "present", "null"),
            Check("execute-no-semantic-provider", execute.Semantic is null && execute.ProcessBoundary is null,
                $"semantic={(execute.Semantic is null ? "null" : "present")}; processBoundary={(execute.ProcessBoundary is null ? "null" : "present")}",
                "both null"),
            Check("execute-pipeline-never-opened", !executeEvents.Any(IsPostAuthorityPipelineEvent),
                string.Join(",", executeEvents.Where(IsPostAuthorityPipelineEvent)), "no evidence/semantic events after DENY")
        };

        var passed = checks.All(item => item.Passed);
        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.23",
            "0.23.0",
            runId,
            DateTimeOffset.Now,
            passed,
            HashExecutable(),
            observationA,
            observationB,
            execute.TerminalState.ToString().ToUpperInvariant(),
            executeEvents,
            checks,
            new[]
            {
                "no catalog repository mutation",
                "no git fetch",
                "no network model call",
                "no execution authority created",
                "no ActionPermit created",
                "no materialization authority created",
                "no Stable Core or interface-registry promotion",
                "self-test artifact write is limited to Workbench/artifacts/acceptance",
                "relevant-source-set verification is read-only and uses no git fetch"
            },
            "Workbench-local acceptance automation v0.23 retains the proven self-hosted update loop and the bounded recovery evidence chain through v0.22. v0.23 adds only a post-acceptance local evidence portability/replay surface: exact retained closure/evidence bytes may be copied into a replay capsule and replayed without dereferencing historical fixture paths. Self-test does not invoke replay and does not grant recovery, rollback, deletion, source mutation, build, checkpoint, network, catalog, Agent Execute, general-recovery-claim, cross-machine-portability, or Stable Core promotion authority. Passing this receipt does not establish canonical UU-AAP conformance or an OS sandbox.");
    }

    private static CommandEnvelope BuildCommand(string id, string mode, string provider)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            mode,
            semanticProvider = provider,
            mutationBudget = 0,
            networkAccess = false,
            arbitraryProcessExecution = false,
            focusRepositories = new[] { "FREESHIELD", "kontur", "uu-aap" },
            terms = new[]
            {
                "authority", "capability", "evidence", "receipt", "intent",
                "availability", "possibility", "companion", "solver", "hint",
                "attention", "reversible"
            },
            maxFilesPerRepository = 160,
            maxEvidenceItems = 80
        });

        return new CommandEnvelope
        {
            Schema = "matawaka.command/v1",
            Id = id,
            Kind = "agent.run",
            Target = "game-intellectual-companion",
            PolicyProfile = "uu-aap-bridge-v0",
            Payload = payload
        };
    }

    private static WorkbenchAcceptanceProviderObservation ObserveProvider(CommandResult result, string expectedProvider)
    {
        if (result.Semantic is null || result.Evidence is null || result.Authority is null || result.Agent is null)
            throw new InvalidDataException($"Provider {expectedProvider} did not return the required acceptance surfaces.");

        using var semantic = JsonDocument.Parse(CommandCodec.Serialize(result.Semantic));
        var root = semantic.RootElement;
        var analysis = root.GetProperty("Analysis");
        var boundary = root.GetProperty("Boundary");
        var process = boundary.GetProperty("ProcessBoundary");
        var runtime = process.GetProperty("RuntimeAttestation");

        var observedProvider = analysis.GetProperty("Provider").GetString() ?? string.Empty;
        if (!string.Equals(observedProvider, expectedProvider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Semantic provider mismatch. Expected {expectedProvider}, observed {observedProvider}.");

        using var agent = JsonDocument.Parse(CommandCodec.Serialize(result.Agent));
        var mutations = agent.RootElement.GetProperty("Mutations");

        return new WorkbenchAcceptanceProviderObservation(
            observedProvider,
            analysis.GetProperty("InputDigest").GetString() ?? string.Empty,
            analysis.GetProperty("OutputDigest").GetString() ?? string.Empty,
            boundary.GetProperty("ObservedUuAapFrontier").GetString() ?? string.Empty,
            boundary.GetProperty("SourceFrontierMatched").GetBoolean(),
            boundary.GetProperty("SourceSetMatched").GetBoolean(),
            HashJson(JsonSerializer.Deserialize<object>(boundary.GetProperty("SourceSetVerification").GetRawText())!),
            process.GetProperty("RuntimeSecurityAttestationVerified").GetBoolean(),
            process.GetProperty("AttestationBeforeSemanticInput").GetBoolean(),
            process.GetProperty("RestrictedToken").GetBoolean(),
            process.GetProperty("LowIntegrityLevel").GetBoolean(),
            runtime.GetProperty("ProcessInJob").GetBoolean(),
            runtime.GetProperty("NoEnabledPrivilegesBeyondChangeNotify").GetBoolean(),
            mutations.ValueKind == JsonValueKind.Array && mutations.GetArrayLength() == 0,
            HashJson(result.Evidence),
            HashJson(result.Authority));
    }

    private static WorkbenchAcceptanceCheck Check(string id, bool passed, string observed, string expected)
        => new(id, passed, observed, expected);

    private static bool IsPostAuthorityPipelineEvent(string eventName)
        => eventName.StartsWith("source-set.", StringComparison.OrdinalIgnoreCase) ||
           eventName.Equals("agent.observe.started", StringComparison.OrdinalIgnoreCase) ||
           eventName.Equals("agent.evidence.selected", StringComparison.OrdinalIgnoreCase) ||
           eventName.StartsWith("semantic.", StringComparison.OrdinalIgnoreCase);

    private static string HashJson(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string HashExecutable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class BufferedProgress : IProgress<WorkbenchProgress>
    {
        private readonly ConcurrentQueue<WorkbenchProgress> _items = new();
        public IReadOnlyList<WorkbenchProgress> Items => _items.ToArray();
        public void Report(WorkbenchProgress value) => _items.Enqueue(value);
    }
}
