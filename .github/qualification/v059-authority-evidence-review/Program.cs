using System.Text.Json;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.App;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

var repoRoot = FindRepoRoot();
var (allowAuthority, allowAudit) = MainWindow.CreateCapabilityEvidenceReviewFixtureV059();
var ru = CapabilityEvidenceReviewPresentationServiceV059.Create(
    allowAuthority, allowAudit, CapabilityEvidenceReviewLanguageV059.Russian);
var en = CapabilityEvidenceReviewPresentationServiceV059.Create(
    allowAuthority, allowAudit, CapabilityEvidenceReviewLanguageV059.English);

Require(allowAuthority.Decision.Decision == "allow", "review fixture base policy decision is not allow");
Require(allowAuthority.Decision.AuthorityGranted == "read-only", "review fixture authority is not read-only");
Require(allowAuthority.Decision.MutationBudgetGranted == 0, "review fixture mutation budget is not zero");
Require(!allowAuthority.Decision.NetworkAccessGranted, "review fixture network authority is true");
Require(!allowAuthority.Decision.ArbitraryProcessExecutionGranted, "review fixture arbitrary process authority is true");
Require(allowAudit.Status == LiveCapabilityEvidenceAuditServiceV058.ComposedStatus, "review fixture audit was not composed");
Require(allowAudit.Composition is not null, "review fixture composition missing");
Require(allowAudit.Composition!.EffectiveDecision.Decision == "allow", "review fixture effective decision drifted");
Require(allowAudit.Composition.EffectiveDecision.AuthorityGranted == "read-only", "review fixture effective authority drifted");
RequireNoPromotion(allowAudit, "allow audit");

Require(ru.Headline == "ПОЛИТИКА РАЗРЕШИЛА ТОЛЬКО ЧТЕНИЕ — ПРОВЕРКА СВЕДЕНИЙ ПОЛНОМОЧИЙ НЕ ДОБАВИЛА",
    "Russian headline drifted");
Require(ru.Headline.Contains("СВЕДЕНИ", StringComparison.OrdinalIgnoreCase), "Russian headline lacks plain evidence wording");
Require(!ru.Headline.Contains("ПРОВЕНАНС", StringComparison.OrdinalIgnoreCase), "Russian headline contains unexplained provenance jargon");
Require(ru.Summary.Contains("что разрешено политикой", StringComparison.OrdinalIgnoreCase), "Russian summary does not separate policy authority");
Require(ru.Summary.Contains("не выдают разрешение", StringComparison.OrdinalIgnoreCase), "Russian summary does not state evidence non-authority");
Require(en.Headline.Contains("POLICY ALLOWED READ-ONLY", StringComparison.Ordinal), "English headline lacks policy authority statement");
Require(en.Headline.Contains("ADDED NO AUTHORITY", StringComparison.Ordinal), "English headline lacks audit non-authority statement");
Require(en.Summary.Contains("does not grant permission", StringComparison.OrdinalIgnoreCase), "English summary does not state evidence non-authority");

RequireFact(ru.PolicyFacts, "Решение политики", "РАЗРЕШЕНО");
RequireFact(ru.PolicyFacts, "Полномочие", "ТОЛЬКО ЧТЕНИЕ");
RequireFact(ru.PolicyFacts, "Бюджет изменений", "0");
RequireFact(ru.PolicyFacts, "Сеть", "НЕТ");
RequireFact(ru.PolicyFacts, "Произвольный процесс", "НЕТ");
RequireFact(ru.AuditFacts, "Статус проверки", "СВЕДЕНИЯ ПРИЛОЖЕНЫ — ПОЛНОМОЧИЯ НЕ ИЗМЕНЕНЫ");
RequireFact(ru.AuditFacts, "Решение по сведениям", C2paProvenanceAdmissionService.Decision);
RequireFact(ru.AuditFacts, "Изменено базовое решение", "НЕТ");
RequireFact(ru.AuditFacts, "Изменены полномочия provider", "НЕТ");
RequireFact(ru.AuditFacts, "Изменён semantic authority contract", "НЕТ");
foreach (var label in new[] { "Полномочие модели", "Полномочие runtime", "ResponseAuthority", "DisplayPermit", "ActionPermit", "SuccessorPermit" })
    RequireFact(ru.NonEffects, label, "НЕТ");

var evidence = allowAudit.Composition.EvidenceContext;
RequireCopyableExact(ru.TechnicalFacts, "Policy", allowAuthority.Decision.Policy);
RequireCopyableExact(ru.TechnicalFacts, "Request ID", allowAuthority.Request.Id);
RequireCopyableExact(ru.TechnicalFacts, "Роль evidence", evidence.EvidenceRole);
RequireCopyableExact(ru.TechnicalFacts, "Repository", evidence.EvidenceRepository);
RequireCopyableExact(ru.TechnicalFacts, "Frontier", evidence.EvidenceFrontier);
RequireCopyableExact(ru.TechnicalFacts, "Path", evidence.EvidencePath);
RequireCopyableExact(ru.TechnicalFacts, "SHA-256", evidence.EvidenceSha256);
Require(evidence.EvidenceSha256 == "ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5",
    "exact accepted evidence SHA-256 drifted");

var denyRequest = allowAuthority.Request with { Id = "v059-human-review-deny:capability" };
var denyDecision = new FreeShieldReadOnlyCapabilityPolicy().Decide(denyRequest, agentEnabled: false);
var denyAuthority = new CapabilityReceipt("matawaka.capability-receipt/v1", denyRequest, denyDecision);
var denyAudit = new LiveCapabilityEvidenceAuditServiceV058().Observe(denyRequest, denyDecision);
var denyRu = CapabilityEvidenceReviewPresentationServiceV059.Create(
    denyAuthority, denyAudit, CapabilityEvidenceReviewLanguageV059.Russian);
Require(denyDecision.Decision == "deny", "deny fixture base policy did not deny");
Require(denyAudit.Composition is not null && denyAudit.Composition.EffectiveDecision.Decision == "deny",
    "evidence audit upgraded base deny");
Require(denyRu.Headline == "ПОЛИТИКА ОТКАЗАЛА — ПРОВЕРКА СВЕДЕНИЙ НЕ МОЖЕТ ИЗМЕНИТЬ ОТКАЗ",
    "Russian deny headline drifted");
RequireNoPromotion(denyAudit, "deny audit");

var rejectedAudit = new LiveCapabilityEvidenceAuditServiceV058(new MissingEvidenceSource()).Observe(
    allowAuthority.Request, allowAuthority.Decision);
var rejectedRu = CapabilityEvidenceReviewPresentationServiceV059.Create(
    allowAuthority, rejectedAudit, CapabilityEvidenceReviewLanguageV059.Russian);
Require(rejectedAudit.Status == LiveCapabilityEvidenceAuditServiceV058.RejectedStatus, "missing supplemental evidence did not reject safely");
Require(rejectedAudit.Composition is null, "rejected supplemental evidence produced a composition");
RequireFact(rejectedRu.AuditFacts, "Статус проверки", "СВЕДЕНИЯ ОТКЛОНЕНЫ — ПОЛНОМОЧИЯ НЕ ИЗМЕНЕНЫ");
RequireFact(rejectedRu.PolicyFacts, "Решение политики", "РАЗРЕШЕНО");
RequireNoPromotion(rejectedAudit, "rejected audit");

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
Require(promotedRejected, "presentation accepted an audit receipt that reported authority promotion");

VerifyStaticReviewBoundary(repoRoot);

var output = new Dictionary<string, object?>
{
    ["schema"] = "matawaka.workbench-v059-authority-evidence-human-review-qualification/v0.59",
    ["status"] = "AUTHORITY_EVIDENCE_PRESENTATION_QUALIFIED_PENDING_HUMAN_UI_REVIEW",
    ["russian_plain_language"] = true,
    ["russian_provenance_jargon_in_headline"] = false,
    ["policy_and_audit_visually_separate_model"] = true,
    ["exact_values_copyable_and_untruncated"] = true,
    ["allow_authority_preserved"] = true,
    ["deny_preserved"] = true,
    ["rejected_evidence_does_not_change_base_allow"] = true,
    ["presentation_rejects_authority_promotion"] = true,
    ["review_only_agent_provider_execution"] = false,
    ["review_only_semantic_host_execution"] = false,
    ["review_only_network_process_git_file_mutation"] = false,
    ["accepted_release_identity_changed"] = false,
    ["human_ui_review_required_before_merge"] = true
};
Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));

static void RequireFact(IReadOnlyList<CapabilityEvidenceReviewFactV059> facts, string label, string expected)
{
    var fact = facts.SingleOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
    Require(fact is not null, $"missing presentation fact: {label}");
    Require(string.Equals(fact!.Value, expected, StringComparison.Ordinal), $"presentation fact mismatch: {label}={fact.Value}; expected={expected}");
}

static void RequireCopyableExact(IReadOnlyList<CapabilityEvidenceReviewFactV059> facts, string label, string expected)
{
    var fact = facts.SingleOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
    Require(fact is not null, $"missing technical fact: {label}");
    Require(fact!.Copyable, $"technical fact is not copyable: {label}");
    Require(string.Equals(fact.Value, expected, StringComparison.Ordinal), $"technical fact is not exact: {label}");
    Require(!fact.Value.Contains("...", StringComparison.Ordinal), $"technical fact is truncated: {label}");
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

static void VerifyStaticReviewBoundary(string repoRoot)
{
    var reviewPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.App", "CapabilityEvidenceReviewV059.cs");
    var appPath = Path.Combine(repoRoot, "src", "Matawaka.Workbench.App", "App.xaml.cs");
    var review = File.ReadAllText(reviewPath);
    var app = File.ReadAllText(appPath);

    string[] forbidden =
    [
        "HttpClient", "System.Net", "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process",
        "File.Write", "File.Delete", "Directory.CreateDirectory", "Directory.Delete",
        "git push", "git fetch", "cmd.exe", "powershell.exe"
    ];
    foreach (var token in forbidden)
        Require(!review.Contains(token, StringComparison.OrdinalIgnoreCase), $"review-only source contains forbidden capability token: {token}");

    Require(!review.Contains("_router.RunAsync", StringComparison.Ordinal), "review-only source invokes CommandRouter");
    Require(!review.Contains("new DevelopmentAgentHost", StringComparison.Ordinal), "review-only source instantiates DevelopmentAgentHost");
    Require(!review.Contains("AnalyzeAsync(", StringComparison.Ordinal), "review-only source invokes semantic analysis");
    Require(review.Contains("Loaded -= Window_LoadedV040", StringComparison.Ordinal), "review-only source does not detach historical loaded route");
    Require(review.Contains("PrimaryMaintenanceSurface.IsEnabled = false", StringComparison.Ordinal), "review-only source does not disable maintenance surface");
    Require(review.Contains("InstalledAppsList.IsEnabled = false", StringComparison.Ordinal), "review-only source does not disable local-app interaction");
    Require(review.Contains("POLИТИКА РАЗРЕШИЛА ТОЛЬКО ЧТЕНИЕ", StringComparison.Ordinal), "review source lacks required Russian policy headline");
    Require(!review.Contains("ПРОВЕНАНС", StringComparison.OrdinalIgnoreCase), "review source contains prohibited unexplained Russian provenance jargon");

    Require(app.Contains("var anyReviewOnly = provenanceReviewOnly || capabilityEvidenceReviewOnly", StringComparison.Ordinal), "startup does not compute exclusive review-only routing");
    Require(app.Contains("if (!anyReviewOnly)", StringComparison.Ordinal) && app.Contains("window.ConfigureV0562Routing();", StringComparison.Ordinal),
        "startup does not fence normal v0.56.2 route during review-only mode");
    Require(app.Contains("if (!capabilityEvidenceReviewOnly)", StringComparison.Ordinal) && app.Contains("ConfigureV0561ProvenanceReviewRouting", StringComparison.Ordinal),
        "startup mixes provenance review surface into capability-evidence review-only mode");
    Require(app.Contains("ConfigureV059CapabilityEvidenceReviewRouting(capabilityEvidenceReviewOnly)", StringComparison.Ordinal),
        "startup does not route explicit v0.59 review-only mode");
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

sealed class MissingEvidenceSource : ILiveCapabilityEvidenceAuditSourceV058
{
    public ProvenanceAdmissionReceipt Read()
        => throw new InvalidDataException("qualification missing supplemental evidence");
}
