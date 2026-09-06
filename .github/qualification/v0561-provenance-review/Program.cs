using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;
using Matawaka.Workbench.Protocol;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

static void ExpectReject(string name, Action action, ref int passed, ref int total)
{
    total++;
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        passed++;
        return;
    }
    throw new InvalidDataException($"Hostile presentation mutation unexpectedly accepted: {name}.");
}

var root = Directory.GetCurrentDirectory();
var fixturePath = Path.Combine(root, ".github", "qualification", "v056-provenance-admission", "fixtures", "uu-aap-955-qualification.json");
var fixture = File.ReadAllBytes(fixturePath);
var fixtureSha = Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant();
Require(fixture.Length == 2415, $"Fixture byte count drift: {fixture.Length}.");
Require(fixtureSha == "ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5", "Fixture SHA-256 drift.");

// This call proves the App assembly embedded the same closed evidence profile and
// that product presentation consumes the merged v0.56 admission boundary.
var admitted = MainWindow.AdmitEmbeddedProvenanceEvidenceV0561();
Require(admitted.Decision == ProvenanceReviewPresentationServiceV0561.ExpectedDecision, "Unexpected embedded admission decision.");
Require(admitted.ObservedEvidenceSha256 == fixtureSha, "Embedded evidence differs from exact qualification fixture.");
Require(admitted.ObservedEvidenceBytes == fixture.Length, "Embedded evidence byte count differs from exact qualification fixture.");
Require(!admitted.GitTagSignatureVerified, "Unsigned Git tag was promoted.");
Require(!admitted.AuthorityCreated && !admitted.NetworkAccessPerformed && !admitted.ProcessStarted &&
        !admitted.ModelInvocationPerformed && !admitted.RuntimeExecutionPerformed &&
        !admitted.ResponseAuthorityCreated && !admitted.DisplayPermitCreated &&
        !admitted.ActionPermitCreated && !admitted.SuccessorPermitCreated,
        "Admission unexpectedly contains authority/effects.");

var russian = ProvenanceReviewPresentationServiceV0561.Create(admitted, ProvenanceReviewLanguageV0561.Russian);
var english = ProvenanceReviewPresentationServiceV0561.Create(admitted, ProvenanceReviewLanguageV0561.English);

Require(russian.LanguageCode == "ru" && russian.TabHeader == "Русский", "Russian presentation identity is missing.");
Require(russian.Headline == ProvenanceReviewPresentationServiceV0561.RussianHeadline, "Russian headline does not preserve no-authority boundary.");
Require(russian.Headline.Contains("ПОЛНОМОЧИЙ НЕТ", StringComparison.Ordinal), "Russian headline does not foreground no-authority status.");
Require(russian.Summary.Contains("не подтверждает доверие, истинность, разрешение или полномочие", StringComparison.Ordinal), "Russian summary does not state non-escalation clearly.");
Require(russian.Boundaries.Any(x => x.Label == "Подпись Git-тега" && x.Value.Contains("НЕ ПОДПИСАН / НЕ ПРОВЕРЕН", StringComparison.Ordinal) && x.Value.Contains("UNSIGNED / NOT VERIFIED", StringComparison.Ordinal)), "Russian unsigned-tag boundary is missing.");
Require(russian.Boundaries.Any(x => x.Label == "Истинность" && x.Value.Contains("НЕ УСТАНОВЛЕНА", StringComparison.Ordinal)), "Russian truth boundary is missing.");
Require(russian.Boundaries.Any(x => x.Label == "Полномочие на публикацию" && x.Value.Contains("НЕ УСТАНОВЛЕНО", StringComparison.Ordinal)), "Russian publication-authority boundary is missing.");
Require(russian.EvidenceSource.Any(x => x.Label == "SHA-256 доказательства" && x.Value == fixtureSha), "Russian evidence digest is not visible in presentation model.");
Require(russian.CopyHint.Contains("Ctrl+C", StringComparison.Ordinal), "Russian copy affordance is missing.");
Require(russian.HumanReviewPrompt.Contains("пять секунд", StringComparison.OrdinalIgnoreCase), "Russian five-second semantic check is missing.");

Require(english.LanguageCode == "en" && english.TabHeader == "English", "English presentation identity is missing.");
Require(english.Headline == "PROVENANCE OBSERVED — NO AUTHORITY", "English headline does not preserve no-authority boundary.");
Require(english.Summary.Contains("does not grant trust, truth, permission, or authority", StringComparison.Ordinal), "English summary does not state non-escalation clearly.");
Require(english.Boundaries.Any(x => x.Label == "Git tag signature" && x.Value == "UNSIGNED / NOT VERIFIED"), "English unsigned-tag boundary is missing.");
Require(english.Boundaries.Any(x => x.Label == "Truth" && x.Value == "NOT ESTABLISHED"), "English truth boundary is missing.");
Require(english.Boundaries.Any(x => x.Label == "Publication authority" && x.Value == "NOT ESTABLISHED"), "English publication authority boundary is missing.");
Require(english.Boundaries.Any(x => x.Label == "Runtime / model authority" && x.Value == "NOT CREATED"), "English runtime/model boundary is missing.");
Require(english.Boundaries.Any(x => x.Label == "Response / display authority" && x.Value == "NOT CREATED"), "English response/display boundary is missing.");
Require(english.Boundaries.Any(x => x.Label == "Action / successor permits" && x.Value == "NOT CREATED"), "English action/successor boundary is missing.");
Require(english.EvidenceSource.Any(x => x.Label == "Evidence SHA-256" && x.Value == fixtureSha), "English evidence digest is not visible in presentation model.");
Require(english.CopyHint.Contains("Ctrl+C", StringComparison.Ordinal), "English copy affordance is missing.");
Require(english.HumanReviewPrompt.Contains("within five seconds", StringComparison.OrdinalIgnoreCase), "English five-second semantic check is missing.");

var hostilePassed = 0;
var hostileTotal = 0;
ExpectReject("decision promotion", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { Decision = "TRUSTED_AND_AUTHORIZED" }, ProvenanceReviewLanguageV0561.Russian), ref hostilePassed, ref hostileTotal);
ExpectReject("binding absent", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { C2paExternalReferenceBindingEstablished = false }, ProvenanceReviewLanguageV0561.English), ref hostilePassed, ref hostileTotal);
ExpectReject("resolution absent", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { ImmutableExternalResolutionExactBytesMatched = false }, ProvenanceReviewLanguageV0561.Russian), ref hostilePassed, ref hostileTotal);
ExpectReject("live validation absent", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { LiveC2paValidationAccepted = false }, ProvenanceReviewLanguageV0561.English), ref hostilePassed, ref hostileTotal);
ExpectReject("unsigned tag promotion", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { GitTagSignatureVerified = true }, ProvenanceReviewLanguageV0561.Russian), ref hostilePassed, ref hostileTotal);
ExpectReject("release substitution", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { ObservedWorkbenchReleaseCommit = new string('f', 40) }, ProvenanceReviewLanguageV0561.English), ref hostilePassed, ref hostileTotal);
ExpectReject("authority promotion", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { AuthorityCreated = true }, ProvenanceReviewLanguageV0561.Russian), ref hostilePassed, ref hostileTotal);
ExpectReject("model authority effect", () => ProvenanceReviewPresentationServiceV0561.Create(admitted with { ModelInvocationPerformed = true }, ProvenanceReviewLanguageV0561.English), ref hostilePassed, ref hostileTotal);
Require(hostilePassed == hostileTotal && hostileTotal == 8, $"Presentation hostile suite mismatch: {hostilePassed}/{hostileTotal}.");

var surfacePath = Path.Combine(root, "src", "Matawaka.Workbench.App", "MainWindow.V0561.cs");
var presentationPath = Path.Combine(root, "src", "Matawaka.Workbench.App", "ProvenanceReviewV0561.cs");
var startupPath = Path.Combine(root, "src", "Matawaka.Workbench.App", "App.xaml.cs");
var surfaceSource = File.ReadAllText(surfacePath, Encoding.UTF8);
var presentationSource = File.ReadAllText(presentationPath, Encoding.UTF8);
var startupSource = File.ReadAllText(startupPath, Encoding.UTF8);
var combined = surfaceSource + "\n" + presentationSource;

foreach (var forbidden in new[]
{
    "new Button", "Click +=", "Command =", "MessageBox.Show", "HttpClient", "System.Net", "Socket(",
    "Process.Start", "System.Diagnostics.Process", "File.Write", "File.Append", "File.Delete",
    "Directory.Create", "Directory.Delete", "Clipboard.SetText", "Clipboard.SetData", "git.exe", "c2patool"
})
{
    Require(!combined.Contains(forbidden, StringComparison.Ordinal), $"Forbidden review-surface capability token present: {forbidden}.");
}

foreach (var required in new[]
{
    "Loaded -= Window_LoadedV040;",
    "Loaded -= Window_LoadedV055;",
    "Loaded -= Window_LoadedV0552;",
    "Closing -= Window_Closing;",
    "PrimaryMaintenanceSurface.IsEnabled = false;",
    "InstalledAppsList.IsEnabled = false;",
    "OutputTabs.SelectedItem = _provenanceReviewTabV0561;"
})
{
    Require(surfaceSource.Contains(required, StringComparison.Ordinal), $"Review-only fail-closed routing token missing: {required}.");
}

foreach (var requiredUx in new[]
{
    "Background = Brushes.LightGoldenrodYellow,",
    "SelectedIndex = 0,",
    "Header = presentation.TabHeader,",
    "IsReadOnly = true,",
    "IsReadOnlyCaretVisible = true,",
    "TextWrapping = TextWrapping.NoWrap,",
    "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,",
    "Text = copyHint,"
})
{
    Require(surfaceSource.Contains(requiredUx, StringComparison.Ordinal), $"Required human-review UX token missing: {requiredUx}.");
}

Require(presentationSource.Contains("\"Русский\"", StringComparison.Ordinal), "Russian localized surface is not source-bound.");
Require(presentationSource.Contains("\"English\"", StringComparison.Ordinal), "English localized surface is not source-bound.");
Require(presentationSource.Contains("Ctrl+C", StringComparison.Ordinal), "Copy guidance is not source-bound.");

const string startupBoundary = "if (!reviewOnly)\n        {\n            window.ConfigureV0552Routing();\n            window.ConfigureV0552AcceptanceRouting();\n        }";
Require(startupSource.Contains(startupBoundary, StringComparison.Ordinal), "v0.55.2 routing/acceptance is not mechanically fenced outside review-only mode.");
Require(startupSource.Contains("window.ConfigureV0561ProvenanceReviewRouting(reviewOnly);", StringComparison.Ordinal), "v0.56.1 review routing is missing from startup.");
Require(!combined.Contains("workbench-v0.56.1-accepted", StringComparison.OrdinalIgnoreCase), "Candidate surface must not promote an accepted v0.56.1 tag.");

var qualification = new
{
    schema = "matawaka.workbench-v0561-provenance-human-review-qualification/v0.2",
    tracking_issue = 86,
    predecessor = "4d954bf5dd77ec732efc7c6d8e05b7c2ab7d161a",
    predecessor_tree = "7c72e5d2ce3792be6c623056c09337f444cc5d41",
    evidence = new { bytes = fixture.Length, sha256 = fixtureSha, decision = admitted.Decision },
    presentation = new
    {
        russian_default = true,
        english_available = true,
        yellow_no_authority_banner = true,
        evidence_values_single_line_copyable = true,
        unsigned_tag_visible_in_both_languages = true,
        truth_not_established_visible_in_both_languages = true,
        publication_authority_not_established_visible_in_both_languages = true,
        execution_authority_not_created_visible_in_both_languages = true,
        human_review_prompt_present_in_both_languages = true
    },
    hostile_presentation = $"{hostilePassed}/{hostileTotal} PASS",
    review_only_bootstrap_detached = true,
    authority_controls_present = false,
    network_process_git_mutation_surface_present = false,
    accepted_version_promoted = false,
    automatic_action = false,
    external_effect = false,
    status = "READY_FOR_HUMAN_TEST_AFTER_WINDOWS_BUILD"
};

Console.WriteLine(JsonSerializer.Serialize(qualification, new JsonSerializerOptions { WriteIndented = true }));
