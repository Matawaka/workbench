using System.Text.Json;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

var repoRoot = FindRepoRoot();
var tempCatalog = Path.Combine(Path.GetTempPath(), $"matawaka-v058-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempCatalog);

try
{
    var allowProvider = new RecordingProvider();
    var allowRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(allowProvider, new FreeShieldReadOnlyCapabilityPolicy()));
    var allowResult = await allowRouter.RunAsync(
        AgentCommand("v058-allow", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(allowResult.TerminalState == CommandTerminalState.Completed, "allow path did not complete");
    Require(allowProvider.Calls == 1, "allow path provider call count mismatch");
    var allowAgent = RequireType<DevelopmentAgentReceipt>(allowResult.Agent, "allow agent receipt");
    var allowAuthority = RequireType<CapabilityReceipt>(allowResult.Authority, "allow authority receipt");
    var allowAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(allowResult.CapabilityEvidence, "allow capability evidence audit");
    Require(allowAudit.Status == LiveCapabilityEvidenceAuditServiceV058.ComposedStatus, "allow audit was not composed");
    Require(allowAudit.EvidenceAvailable && allowAudit.Composition is not null, "allow audit evidence/composition missing");
    Require(ReferenceEquals(allowProvider.ReceivedDecision, allowAgent.CapabilityDecision), "provider did not receive the exact base decision object");
    Require(ReferenceEquals(allowAuthority.Decision, allowAgent.CapabilityDecision), "CommandResult authority receipt did not preserve the exact base decision object");
    RequireSameDecisionGrants(allowAgent.CapabilityDecision, allowAudit.Composition!.BaseDecision, "allow composition base");
    RequireNoIncrease(allowAgent.CapabilityDecision, allowAudit.Composition.ComposedDecision, "allow composition");
    RequireNoAuthorityEffects(allowAudit, "allow audit");

    var denyProvider = new RecordingProvider();
    var denyRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(denyProvider, new FreeShieldReadOnlyCapabilityPolicy()));
    var denyResult = await denyRouter.RunAsync(
        AgentCommand("v058-deny", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: false, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(denyResult.TerminalState == CommandTerminalState.Denied, "disabled-agent path did not deny");
    Require(denyProvider.Calls == 0, "provider was invoked after base deny");
    var denyAgent = RequireType<DevelopmentAgentReceipt>(denyResult.Agent, "deny agent receipt");
    var denyAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(denyResult.CapabilityEvidence, "deny capability evidence audit");
    Require(denyAgent.CapabilityDecision.Decision == "deny", "base deny decision drifted");
    Require(denyAudit.Status == LiveCapabilityEvidenceAuditServiceV058.ComposedStatus, "deny audit was not composed");
    Require(denyAudit.Composition is not null && denyAudit.Composition.ComposedDecision.Decision == "deny", "provenance audit upgraded base deny");
    RequireNoAuthorityEffects(denyAudit, "deny audit");

    var executeProvider = new RecordingProvider();
    var executeRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(executeProvider, new FreeShieldReadOnlyCapabilityPolicy()));
    var executeResult = await executeRouter.RunAsync(
        AgentCommand("v058-execute", "execute"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(executeResult.TerminalState == CommandTerminalState.Denied, "execute path did not deny");
    Require(executeProvider.Calls == 0, "provider was invoked for denied execute request");
    var executeAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(executeResult.CapabilityEvidence, "execute audit");
    Require(executeAudit.Composition is not null && executeAudit.Composition.ComposedDecision.Decision == "deny", "execute provenance audit upgraded deny");
    RequireNoAuthorityEffects(executeAudit, "execute audit");

    var forgedProvider = new RecordingProvider();
    var forgedAuditService = new LiveCapabilityEvidenceAuditServiceV058(new ForgedAuthoritySource());
    var forgedRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(forgedProvider, new FreeShieldReadOnlyCapabilityPolicy()),
        capabilityEvidenceAudit: forgedAuditService);
    var forgedResult = await forgedRouter.RunAsync(
        AgentCommand("v058-forged", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(forgedResult.TerminalState == CommandTerminalState.Completed, "rejected supplemental evidence changed base allow terminal state");
    Require(forgedProvider.Calls == 1, "rejected supplemental evidence changed provider execution selected by base policy");
    var forgedAgent = RequireType<DevelopmentAgentReceipt>(forgedResult.Agent, "forged-evidence agent receipt");
    var forgedAuthority = RequireType<CapabilityReceipt>(forgedResult.Authority, "forged-evidence authority receipt");
    var forgedAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(forgedResult.CapabilityEvidence, "forged-evidence audit");
    Require(forgedAudit.Status == LiveCapabilityEvidenceAuditServiceV058.RejectedStatus, "forged provenance was not rejected as supplemental audit evidence");
    Require(!forgedAudit.EvidenceAvailable && forgedAudit.Composition is null, "forged provenance produced a composition");
    Require(ReferenceEquals(forgedProvider.ReceivedDecision, forgedAgent.CapabilityDecision), "forged audit replaced provider base decision");
    Require(ReferenceEquals(forgedAuthority.Decision, forgedAgent.CapabilityDecision), "forged audit replaced CommandResult authority decision");
    Require(forgedAgent.CapabilityDecision.Decision == "allow" && forgedAgent.CapabilityDecision.AuthorityGranted == "read-only", "forged audit changed base policy result");
    RequireNoAuthorityEffects(forgedAudit, "forged audit");

    var missingProvider = new RecordingProvider();
    var missingRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(missingProvider, new FreeShieldReadOnlyCapabilityPolicy()),
        capabilityEvidenceAudit: new LiveCapabilityEvidenceAuditServiceV058(new MissingEvidenceSource()));
    var missingResult = await missingRouter.RunAsync(
        AgentCommand("v058-missing", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);
    var missingAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(missingResult.CapabilityEvidence, "missing-evidence audit");
    Require(missingResult.TerminalState == CommandTerminalState.Completed && missingProvider.Calls == 1, "missing supplemental evidence became an allow prerequisite");
    Require(missingAudit.Status == LiveCapabilityEvidenceAuditServiceV058.RejectedStatus, "missing supplemental evidence did not fail safe as an audit observation");
    RequireNoAuthorityEffects(missingAudit, "missing-evidence audit");

    VerifyStaticBoundary(repoRoot);

    var output = new Dictionary<string, object?>
    {
        ["schema"] = "matawaka.workbench-v058-live-capability-evidence-audit-qualification/v0.58",
        ["status"] = "LIVE_CAPABILITY_EVIDENCE_AUDIT_QUALIFIED_NO_AUTHORITY_PROMOTION",
        ["allow_provider_received_exact_base_decision"] = true,
        ["allow_authority_receipt_preserved_base_decision"] = true,
        ["deny_provider_not_invoked"] = true,
        ["execute_provider_not_invoked"] = true,
        ["forged_evidence_rejected_without_authority_change"] = true,
        ["missing_evidence_not_required_for_base_allow"] = true,
        ["semantic_authority_contract_changed"] = false,
        ["development_agent_host_policy_changed"] = false,
        ["network_authority_created"] = false,
        ["arbitrary_process_authority_created"] = false,
        ["model_runtime_response_display_action_successor_authority_created"] = false,
        ["external_effect"] = false
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    try { Directory.Delete(tempCatalog, recursive: true); } catch { }
}

static CommandEnvelope AgentCommand(string id, string mode)
{
    using var document = JsonDocument.Parse($$"""{"mode":"{{mode}}","mutationBudget":{{(mode == "execute" ? 1 : 0)}},"networkAccess":false,"arbitraryProcessExecution":false}""");
    return new CommandEnvelope
    {
        Schema = "matawaka.command/v1",
        Id = id,
        Kind = "agent.run",
        Target = "qualification-only",
        PolicyProfile = "",
        Payload = document.RootElement.Clone()
    };
}

static void RequireNoAuthorityEffects(LiveCapabilityEvidenceAuditReceiptV058 audit, string label)
{
    Require(!audit.BaseDecisionChanged, $"{label}: base decision changed");
    Require(!audit.ProviderAuthorityChanged, $"{label}: provider authority changed");
    Require(!audit.SemanticAuthorityContractChanged, $"{label}: semantic authority contract changed");
    Require(!audit.EvidenceRequiredForAllow, $"{label}: evidence became required for allow");
    Require(!audit.AuthorityCreated && !audit.ModelInvocationAuthorityCreated && !audit.RuntimeExecutionAuthorityCreated &&
            !audit.ResponseAuthorityCreated && !audit.DisplayPermitCreated && !audit.ActionPermitCreated && !audit.SuccessorPermitCreated,
        $"{label}: authority/effect flag was promoted");
}

static void RequireNoIncrease(CapabilityDecision baseline, CapabilityDecision observed, string label)
{
    if (baseline.Decision == "deny") Require(observed.Decision == "deny", $"{label}: deny upgraded");
    Require(observed.MutationBudgetGranted <= baseline.MutationBudgetGranted, $"{label}: mutation grant increased");
    Require(baseline.NetworkAccessGranted || !observed.NetworkAccessGranted, $"{label}: network grant increased");
    Require(baseline.ArbitraryProcessExecutionGranted || !observed.ArbitraryProcessExecutionGranted, $"{label}: process grant increased");
    Require(observed.AuthorityGranted == baseline.AuthorityGranted, $"{label}: authority string changed");
}

static void RequireSameDecisionGrants(CapabilityDecision expected, CapabilityDecision observed, string label)
{
    Require(expected.Schema == observed.Schema && expected.RequestId == observed.RequestId &&
            expected.Decision == observed.Decision && expected.Policy == observed.Policy &&
            expected.AuthorityGranted == observed.AuthorityGranted &&
            expected.MutationBudgetGranted == observed.MutationBudgetGranted &&
            expected.NetworkAccessGranted == observed.NetworkAccessGranted &&
            expected.ArbitraryProcessExecutionGranted == observed.ArbitraryProcessExecutionGranted,
        $"{label}: decision/grants mismatch");
}

static T RequireType<T>(object? value, string label) where T : class
    => value as T ?? throw new InvalidDataException($"Missing or unexpected {label}: {value?.GetType().FullName ?? "null"}.");

static void VerifyStaticBoundary(string repoRoot)
{
    var routerPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.Runtime", "CommandRouter.cs");
    var auditPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.Runtime", "LiveCapabilityEvidenceAuditV058.cs");
    var agentHostPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.AgentHost", "DevelopmentAgentHost.cs");
    var semanticPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.AgentHost", "SemanticProvider.cs");

    var router = File.ReadAllText(routerPath);
    var audit = File.ReadAllText(auditPath);
    var agentHost = File.ReadAllText(agentHostPath);
    var semantic = File.ReadAllText(semanticPath);

    var hostRun = router.IndexOf("await _agent.RunAsync", StringComparison.Ordinal);
    var auditObserve = router.IndexOf("_capabilityEvidenceAudit.Observe", StringComparison.Ordinal);
    Require(hostRun >= 0 && auditObserve > hostRun, "capability evidence audit is not causally after DevelopmentAgentHost.RunAsync");
    Require(!agentHost.Contains("LiveCapabilityEvidenceAudit", StringComparison.Ordinal) &&
            !agentHost.Contains("CapabilityEvidenceCompositionReceipt", StringComparison.Ordinal),
        "DevelopmentAgentHost policy/provider path was coupled to v0.58 evidence audit");
    Require(!semantic.Contains("CapabilityEvidenceCompositionReceipt", StringComparison.Ordinal) &&
            !semantic.Contains("LiveCapabilityEvidenceAudit", StringComparison.Ordinal),
        "SemanticHost authority contract was coupled to v0.58 evidence audit");

    string[] forbidden =
    [
        "HttpClient", "System.Net", "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process",
        "File.Write", "File.Delete", "Directory.CreateDirectory", "Directory.Delete",
        "git push", "git fetch", "powershell", "cmd.exe"
    ];
    foreach (var token in forbidden)
        Require(!audit.Contains(token, StringComparison.OrdinalIgnoreCase), $"new audit service contains forbidden capability token: {token}");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Matawaka.Workbench.sln")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Unable to locate Workbench repository root.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

sealed class RecordingProvider : IDevelopmentAgentProvider
{
    public int Calls { get; private set; }
    public CapabilityRequest? ReceivedRequest { get; private set; }
    public CapabilityDecision? ReceivedDecision { get; private set; }

    public Task<DevelopmentAgentReceipt> ObserveProposeAsync(
        CommandEnvelope command,
        IReadOnlyList<CatalogRepository> catalog,
        CapabilityRequest capabilityRequest,
        CapabilityDecision capabilityDecision,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        ReceivedRequest = capabilityRequest;
        ReceivedDecision = capabilityDecision;

        return Task.FromResult(new DevelopmentAgentReceipt(
            "qualification-recording-provider-v0.58",
            "completed",
            capabilityRequest.Operation,
            capabilityDecision.AuthorityGranted,
            capabilityRequest,
            capabilityDecision,
            null,
            catalog,
            Array.Empty<AgentRepositoryFinding>(),
            new AgentEvidenceCoverage(
                "qualification-recording-provider",
                0,
                0,
                0,
                0,
                Array.Empty<AgentRepositoryCoverage>()),
            Array.Empty<AgentEvidence>(),
            null,
            null,
            null,
            null,
            Array.Empty<string>(),
            "Qualification provider records the exact base decision and performs no semantic, mutation, network, model, runtime, response, display, action or successor effect."));
    }
}

sealed class ForgedAuthoritySource : ILiveCapabilityEvidenceAuditSourceV058
{
    public ProvenanceAdmissionReceipt Read()
    {
        var valid = new EmbeddedAcceptedProvenanceAuditSourceV058().Read();
        return valid with { AuthorityCreated = true };
    }
}

sealed class MissingEvidenceSource : ILiveCapabilityEvidenceAuditSourceV058
{
    public ProvenanceAdmissionReceipt Read()
        => throw new InvalidDataException("qualification missing supplemental evidence");
}
