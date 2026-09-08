using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

var root = Directory.GetCurrentDirectory();
var contract = QualifiedV0561EvidenceV0562.RunOfflineContractChecks();
Require(contract.All(x => x.Passed), "One or more v0.56.2 exact evidence checks failed.");

var appPath = Path.Combine(root, "src", "Matawaka.Workbench.App", "App.xaml.cs");
var routingPath = Path.Combine(root, "src", "Matawaka.Workbench.App", "MainWindow.V0562.cs");
var evidencePath = Path.Combine(root, "src", "Matawaka.Workbench.App", "QualifiedV0561EvidenceV0562.cs");
var qualificationWorkflowPath = Path.Combine(root, ".github", "workflows", "workbench-v0562-acceptance.yml");
var publicationWorkflowPath = Path.Combine(root, ".github", "workflows", "workbench-v0562-publish-accepted.yml");

var app = File.ReadAllText(appPath, Encoding.UTF8);
var routing = File.ReadAllText(routingPath, Encoding.UTF8);
var evidence = File.ReadAllText(evidencePath, Encoding.UTF8);
var qualificationWorkflow = File.ReadAllText(qualificationWorkflowPath, Encoding.UTF8);
var publicationWorkflow = File.ReadAllText(publicationWorkflowPath, Encoding.UTF8);

Require(app.Contains("window.ConfigureV0562Routing();", StringComparison.Ordinal), "Normal startup is not routed through v0.56.2.");
Require(!app.Contains("window.ConfigureV0552AcceptanceRouting();", StringComparison.Ordinal), "Historical v0.55.2 acceptance/publication route remains active in successor startup.");
Require(app.Contains("window.ConfigureV0561ProvenanceReviewRouting(reviewOnly);", StringComparison.Ordinal), "v0.56.1 provenance review surface is no longer preserved.");

Require(routing.Contains("PublishAcceptedButton.IsEnabled = false;", StringComparison.Ordinal), "Historical Publish Accepted control is not disabled.");
Require(routing.Contains("PublishAcceptedButton.IsHitTestVisible = false;", StringComparison.Ordinal), "Historical Publish Accepted control remains interactive.");
Require(routing.Contains("Matawaka Workbench v0.56.2", StringComparison.Ordinal), "v0.56.2 normal-mode identity is missing.");

Require(evidence.Contains(QualifiedV0561EvidenceV0562.QualifiedMain, StringComparison.Ordinal), "Exact qualified v0.56.1 main is not source-bound.");
Require(evidence.Contains(QualifiedV0561EvidenceV0562.QualifiedMainTree, StringComparison.Ordinal), "Exact qualified v0.56.1 tree is not source-bound.");
Require(evidence.Contains(QualifiedV0561EvidenceV0562.PostMergeArtifactSha256, StringComparison.Ordinal), "Exact post-merge artifact digest is not source-bound.");
Require(evidence.Contains(QualifiedV0561EvidenceV0562.RussianHeadline, StringComparison.Ordinal), "Human-accepted Russian wording is not source-bound.");

Require(qualificationWorkflow.Contains("contents: read", StringComparison.Ordinal), "Qualification workflow does not remain contents-read-only.");
Require(qualificationWorkflow.Contains("actions: read", StringComparison.Ordinal), "Qualification workflow cannot independently read exact Actions evidence.");
Require(!qualificationWorkflow.Contains("contents: write", StringComparison.Ordinal), "Qualification workflow unexpectedly has contents write authority.");
Require(!qualificationWorkflow.Contains("git push", StringComparison.OrdinalIgnoreCase), "Qualification workflow unexpectedly contains a Git push.");

Require(publicationWorkflow.Contains("workflow_dispatch:", StringComparison.Ordinal), "Publication workflow is not human-dispatched.");
Require(!publicationWorkflow.Contains("pull_request:", StringComparison.Ordinal), "Publication workflow must not run automatically from a PR.");
Require(!publicationWorkflow.Contains("push:", StringComparison.Ordinal), "Publication workflow must not run automatically from a Git push.");
Require(publicationWorkflow.Contains("contents: write", StringComparison.Ordinal), "Publication workflow lacks its narrowly required tag-write authority.");
Require(publicationWorkflow.Contains("workbench-v0.56.2-accepted", StringComparison.Ordinal), "Fixed target accepted tag is missing from publication workflow.");
Require(publicationWorkflow.Contains("PUBLISH_WORKBENCH_V0.56.2_ACCEPTED", StringComparison.Ordinal), "Exact human confirmation phrase is missing.");
Require(publicationWorkflow.Contains("refs/heads/main", StringComparison.Ordinal), "Publication workflow does not mechanically bind canonical main.");
Require(publicationWorkflow.Contains("refs/tags/workbench-v0.56.2-accepted", StringComparison.Ordinal), "Publication workflow does not mechanically bind the fixed target tag ref.");

foreach (var forbidden in new[]
{
    "--force", "--force-with-lease", "refs/heads/main:refs/heads/main", ":refs/heads/main",
    "gh release", "/releases", "git push --all", "git push --tags", "git remote add",
    "inputs.tag", "inputs.ref", "inputs.remote", "workflow_run:"
})
{
    Require(!publicationWorkflow.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden publication capability token present: {forbidden}.");
}

var receipt = new
{
    schema = "matawaka.workbench-v0562-acceptance-qualification/v0.1",
    tracking_issue = 88,
    version = QualifiedV0561EvidenceV0562.Version,
    qualified_v0561 = new
    {
        main = QualifiedV0561EvidenceV0562.QualifiedMain,
        tree = QualifiedV0561EvidenceV0562.QualifiedMainTree,
        post_merge_run = QualifiedV0561EvidenceV0562.PostMergeRunId,
        post_merge_artifact = QualifiedV0561EvidenceV0562.PostMergeArtifactId,
        post_merge_artifact_sha256 = QualifiedV0561EvidenceV0562.PostMergeArtifactSha256
    },
    accepted_predecessor = new
    {
        tag = QualifiedV0561EvidenceV0562.AcceptedPredecessorTag,
        tag_object = QualifiedV0561EvidenceV0562.AcceptedPredecessorTagObject,
        commit = QualifiedV0561EvidenceV0562.AcceptedPredecessorCommit
    },
    target_tag = QualifiedV0561EvidenceV0562.TargetAcceptedTag,
    exact_contract_checks = $"{contract.Count(x => x.Passed)}/{contract.Count} PASS",
    normal_ui_historical_publisher_disabled = true,
    qualification_contents_write = false,
    publication_automatic_trigger = false,
    publication_main_push_allowed = false,
    publication_force_allowed = false,
    publication_arbitrary_ref_or_remote_allowed = false,
    publication_release_creation_allowed = false,
    publication_requires_exact_human_confirmation = true,
    status = "READY_FOR_POST_MERGE_QUALIFICATION_BEFORE_PUBLICATION"
};

Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
