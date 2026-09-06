using System.Text.Json;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.App;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

var repoRoot = FindRepoRoot();
var tempCatalog = Path.Combine(Path.GetTempPath(), $"matawaka-v060-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempCatalog);

try
{
    var allowProvider = new RecordingProvider();
    var allowRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(allowProvider, new FreeShieldReadOnlyCapabilityPolicy()));
    var allowResult = await allowRouter.RunAsync(
        AgentCommand("v060-live-allow", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(allowResult.TerminalState == CommandTerminalState.Completed, "live allow result did not complete");
    Require(allowProvider.Calls == 1, "live allow provider call count mismatch");
    var allowAgent = RequireType<DevelopmentAgentReceipt>(allowResult.Agent, "live allow agent receipt");
    var allowAuthority = RequireType<CapabilityReceipt>(allowResult.Authority, "live allow authority receipt");
    var allowAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(allowResult.CapabilityEvidence, "live allow capability evidence");
    Require(ReferenceEquals(allowProvider.ReceivedDecision, allowAgent.CapabilityDecision), "provider did not receive exact base decision object");
    Require(ReferenceEquals(allowAuthority.Decision, allowAgent.CapabilityDecision), "live result authority did not preserve exact base decision object");
    Require(allowAudit.Status == LiveCapabilityEvidenceAuditServiceV058.ComposedStatus, "live allow audit was not composed");
    RequireNoPromotion(allowAudit, "live allow audit");

    var allowRu = CapabilityEvidenceReviewPresentationServiceV059.Create(
        allowAuthority, allowAudit, CapabilityEvidenceReviewLanguageV059.Russian);
    var allowEn = CapabilityEvidenceReviewPresentationServiceV059.Create(
        allowAuthority, allowAudit, CapabilityEvidenceReviewLanguageV059.English);
    Require(allowRu.Headline == "ПОЛИТИКА РАЗРЕШИЛА ТОЛЬКО ЧТЕНИЕ — ПРОВЕРКА СВЕДЕНИЙ ПОЛНОМОЧИЙ НЕ ДОБАВИЛА",
        "live allow Russian presentation drifted from human-reviewed v0.59 semantics");
    Require(allowEn.Headline == "POLICY ALLOWED READ-ONLY — EVIDENCE REVIEW ADDED NO AUTHORITY",
        "live allow English presentation drifted from human-reviewed v0.59 semantics");
    RequireFact(allowRu.PolicyFacts, "Решение политики", "РАЗРЕШЕНО");
    RequireFact(allowRu.PolicyFacts, "Полномочие", "ТОЛЬКО ЧТЕНИЕ");
    RequireFact(allowRu.PolicyFacts, "Бюджет изменений", "0");
    RequireFact(allowRu.PolicyFacts, "Сеть", "НЕТ");
    RequireFact(allowRu.PolicyFacts, "Произвольный процесс", "НЕТ");
    RequireFact(allowRu.AuditFacts, "Изменено базовое решение", "НЕТ");
    RequireFact(allowRu.AuditFacts, "Изменены полномочия provider", "НЕТ");
    RequireFact(allowRu.AuditFacts, "Изменён semantic authority contract", "НЕТ");
    RequireExactEvidenceValues(allowRu, allowAudit);

    var denyProvider = new RecordingProvider();
    var denyRouter = new CommandRouter(
        agent: new DevelopmentAgentHost(denyProvider, new FreeShieldReadOnlyCapabilityPolicy()));
    var denyResult = await denyRouter.RunAsync(
        AgentCommand("v060-live-deny", "observe"),
        new RuntimeContext(tempCatalog, AgentEnabled: false, AllowGitFetch: false),
        null,
        CancellationToken.None);

    Require(denyResult.TerminalState == CommandTerminalState.Denied, "live deny result did not deny");
    Require(denyProvider.Calls == 0, "live deny invoked provider");
    var denyAuthority = RequireType<CapabilityReceipt>(denyResult.Authority, "live deny authority receipt");
    var denyAudit = RequireType<LiveCapabilityEvidenceAuditReceiptV058>(denyResult.CapabilityEvidence, "live deny capability evidence");
    Require(denyAuthority.Decision.Decision == "deny", "live deny base decision drifted");
    Require(denyAudit.Composition is not null && denyAudit.Composition.EffectiveDecision.Decision == "deny",
        "live deny audit upgraded denial");
    RequireNoPromotion(denyAudit, "live deny audit");
    var denyRu = CapabilityEvidenceReviewPresentationServiceV059.Create(
        denyAuthority, denyAudit, CapabilityEvidenceReviewLanguageV059.Russian);
    Require(denyRu.Headline == "ПОЛИТИКА ОТКАЗАЛА — ПРОВЕРКА СВЕДЕНИЙ НЕ МОЖЕТ ИЗМЕНИТЬ ОТКАЗ",
        "live deny presentation drifted from human-reviewed v0.59 semantics");

    var catalogResult = await allowRouter.RunAsync(
        CatalogCommand("v060-non-agent"),
        new RuntimeContext(tempCatalog, AgentEnabled: true, AllowGitFetch: false),
        null,
        CancellationToken.None);
    Require(catalogResult.TerminalState == CommandTerminalState.Completed, "non-agent control did not complete");
    Require(catalogResult.Authority is null, "non-agent control unexpectedly produced capability authority");
    Require(catalogResult.CapabilityEvidence is null, "non-agent control unexpectedly produced capability evidence audit");
    Require(allowProvider.Calls == 1, "non-agent control invoked agent provider");

    var promotedRejected = false;
    try
    {
        _ = CapabilityEvidenceReviewPresentationServiceV059.Create(
            allowAuthority,
            allowAudit with { AuthorityCreated = true },
            CapabilityEvidenceReviewLanguageV059.Russian);
    }
    catch (InvalidDataException)
    {
        promotedRejected = true;
    }
    Require(promotedRejected, "human-reviewed presentation accepted authority-promoted audit");

    var mismatchRejected = false;
    try
    {
        var mismatch = allowAudit with
        {
            Composition = allowAudit.Composition! with
            {
                Request = allowAudit.Composition!.Request with { Id = "mismatched-live-request" }
            }
        };
        _ = CapabilityEvidenceReviewPresentationServiceV059.Create(
            allowAuthority,
            mismatch,
            CapabilityEvidenceReviewLanguageV059.Russian);
    }
    catch (InvalidDataException)
    {
        mismatchRejected = true;
    }
    Require(mismatchRejected, "human-reviewed presentation accepted mismatched live audit/request binding");

    VerifyStaticLiveWiring(repoRoot);

    var output = new Dictionary<string, object?>
    {
        ["schema"] = "matawaka.workbench-v060-live-authority-evidence-surface-qualification/v0.60",
        ["status"] = "LIVE_AUTHORITY_EVIDENCE_SURFACE_QUALIFIED_NO_AUTHORITY_CHANGE",
        ["live_allow_real_command_result"] = true,
        ["live_allow_provider_received_exact_base_decision"] = true,
        ["live_allow_human_reviewed_projection_reused"] = true,
        ["live_deny_preserved"] = true,
        ["live_deny_provider_calls"] = 0,
        ["non_agent_result_invents_authority_evidence_surface"] = false,
        ["promoted_audit_rejected"] = true,
        ["mismatched_audit_rejected"] = true,
        ["display_occurs_after_runner_return"] = true,
        ["display_failure_retries_command"] = false,
        ["authority_runtime_sources_changed"] = false,
        ["accepted_release_identity_changed"] = false,
        ["external_effect_authority_created"] = false
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    try { Directory.Delete(tempCatalog, recursive: true); } catch { }
}

static CommandEnvelope AgentCommand(string id, string mode)
{
    using var document = JsonDocument.Parse($$"""{"mode":"{{mode}}","mutationBudget":0,"networkAccess":false,"arbitraryProcessExecution":false}""");
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

static CommandEnvelope CatalogCommand(string id)
    => new()
    {
        Schema = "matawaka.command/v1",
        Id = id,
        Kind = "catalog.inspect",
        Target = "qualification-only"
    };

static void RequireExactEvidenceValues(
    CapabilityEvidenceReviewPresentationV059 view,
    LiveCapabilityEvidenceAuditReceiptV058 audit)
{
    var context = audit.Composition?.EvidenceContext
        ?? throw new InvalidDataException("Expected composed evidence context for live allow presentation.");
    RequireCopyable(view.TechnicalFacts, "Repository", context.EvidenceRepository);
    RequireCopyable(view.TechnicalFacts, "Frontier", context.EvidenceFrontier);
    RequireCopyable(view.TechnicalFacts, "Path", context.EvidencePath);
    RequireCopyable(view.TechnicalFacts, "SHA-256", context.EvidenceSha256);
}

static void RequireCopyable(IReadOnlyList<CapabilityEvidenceReviewFactV059> facts, string label, string value)
{
    var fact = facts.SingleOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
    Require(fact is not null, $"missing live presentation exact value: {label}");
    Require(fact!.Copyable, $"live presentation value is not copyable: {label}");
    Require(string.Equals(fact.Value, value, StringComparison.Ordinal), $"live presentation value mismatch: {label}");
    Require(!fact.Value.Contains("...", StringComparison.Ordinal), $"live presentation value is truncated: {label}");
}

static void RequireFact(IReadOnlyList<CapabilityEvidenceReviewFactV059> facts, string label, string value)
{
    var fact = facts.SingleOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
    Require(fact is not null && string.Equals(fact.Value, value, StringComparison.Ordinal),
        $"live presentation fact mismatch: {label}");
}

static void RequireNoPromotion(LiveCapabilityEvidenceAuditReceiptV058 audit, string label)
{
    Require(!audit.BaseDecisionChanged, $"{label}: base decision changed");
    Require(!audit.ProviderAuthorityChanged, $"{label}: provider authority changed");
    Require(!audit.SemanticAuthorityContractChanged, $"{label}: semantic authority contract changed");
    Require(!audit.EvidenceRequiredForAllow, $"{label}: evidence became an allow prerequisite");
    Require(!audit.AuthorityCreated, $"{label}: authority created");
    Require(!audit.ModelInvocationAuthorityCreated, $"{label}: model authority created");
    Require(!audit.RuntimeExecutionAuthorityCreated, $"{label}: runtime authority created");
    Require(!audit.ResponseAuthorityCreated, $"{label}: response authority created");
    Require(!audit.DisplayPermitCreated, $"{label}: display permit created");
    Require(!audit.ActionPermitCreated, $"{label}: action permit created");
    Require(!audit.SuccessorPermitCreated, $"{label}: successor permit created");
}

static void VerifyStaticLiveWiring(string repoRoot)
{
    var mainPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.App", "MainWindow.xaml.cs");
    var livePath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.App", "MainWindow.V060.cs");
    var main = File.ReadAllText(mainPath);
    var live = File.ReadAllText(livePath);

    var runner = main.IndexOf("var result = await _router.RunAsync", StringComparison.Ordinal);
    var renderCall = main.IndexOf("RenderResult(result);", runner, StringComparison.Ordinal);
    Require(runner >= 0 && renderCall > runner, "live UI render is not causally after ICommandRunner.RunAsync return");

    var renderMethod = main.IndexOf("private void RenderResult(CommandResult result)", StringComparison.Ordinal);
    var rawAuthority = main.IndexOf("AuthorityTextBox.Text =", renderMethod, StringComparison.Ordinal);
    var liveWiring = main.IndexOf("TryShowLiveCapabilityEvidenceV060(result)", renderMethod, StringComparison.Ordinal);
    var fallback = main.IndexOf("if (!liveCapabilityEvidenceRendered)", liveWiring, StringComparison.Ordinal);
    Require(renderMethod >= 0 && rawAuthority > renderMethod && liveWiring > rawAuthority && fallback > liveWiring,
        "RenderResult live authority/evidence wiring order is not display-only after raw receipts");

    Require(main.Contains("Source tree, build, git commit/tag, сеть, каталог Matawaka и Agent Execute не разрешаются.", StringComparison.Ordinal),
        "historical materialization preview drifted during v0.60 wiring");
    Require(main.Contains("\"Transport negatives v0.27\", MessageBoxButton.YesNo, MessageBoxImage.Question", StringComparison.Ordinal),
        "historical transport negative-control dialog drifted during v0.60 wiring");

    Require(live.Contains("result.Authority is not CapabilityReceipt", StringComparison.Ordinal),
        "live wiring lacks typed base authority gate");
    Require(live.Contains("result.CapabilityEvidence is not LiveCapabilityEvidenceAuditReceiptV058", StringComparison.Ordinal),
        "live wiring lacks typed supplemental audit gate");
    Require(live.Contains("ShowCapabilityEvidenceReviewV059(authority, audit)", StringComparison.Ordinal),
        "live wiring does not reuse exact human-reviewed v0.59 renderer");
    Require(live.Contains("OutputTabs.SelectedItem = _capabilityEvidenceReviewTabV059", StringComparison.Ordinal),
        "live wiring does not select the reviewed authority/evidence surface");
    Require(live.Contains("terminalStateUnchanged=true; retry=false", StringComparison.Ordinal),
        "live presentation failure does not explicitly preserve terminal state/no-retry semantics");

    string[] forbidden =
    [
        "_router.RunAsync", "RunCommandAsync(", "ApplyTerminalState(", "ShowFailure(",
        "new DevelopmentAgentHost", "SemanticProvider", "AnalyzeAsync(",
        "HttpClient", "System.Net", "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process",
        "File.Write", "File.Delete", "Directory.CreateDirectory", "Directory.Delete",
        "git push", "git fetch", "cmd.exe", "powershell.exe"
    ];
    foreach (var token in forbidden)
        Require(!live.Contains(token, StringComparison.OrdinalIgnoreCase), $"v0.60 live display wiring contains forbidden execution/effect token: {token}");
}

static T RequireType<T>(object? value, string label) where T : class
    => value as T ?? throw new InvalidDataException($"Missing or unexpected {label}: {value?.GetType().FullName ?? "null"}.");

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
        ReceivedDecision = capabilityDecision;

        return Task.FromResult(new DevelopmentAgentReceipt(
            "qualification-recording-provider-v0.60",
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
            "Qualification provider records exact base authority and performs no semantic, mutation, network, model, runtime, response, display, action or successor effect."));
    }
}
