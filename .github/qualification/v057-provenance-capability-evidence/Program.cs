using System.Text.Json;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Protocol;

const string FixturePath = ".github/qualification/v056-provenance-admission/fixtures/uu-aap-955-qualification.json";
const string ServicePath = "src/Matawaka.Workbench.AgentHost/ProvenanceCapabilityEvidenceComposer.cs";

var fixture = File.ReadAllBytes(FixturePath);
var provenance = C2paProvenanceAdmissionService.AdmitAcceptedWorkbenchV0552(fixture);
var policy = new FreeShieldReadOnlyCapabilityPolicy();

var proposeCommand = Command("v057-propose", "propose", 0, false, false);
var proposeRequest = policy.CreateRequest(proposeCommand);
var proposeDecision = policy.Decide(proposeRequest, agentEnabled: true);
var positive = ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance);

Require(positive.BaseDecisionPreserved, "positive composition did not preserve base decision");
Require(!positive.AuthorityIncreased, "positive composition increased authority");
Require(!positive.MutationBudgetIncreased, "positive composition increased mutation budget");
Require(!positive.NetworkGrantIncreased, "positive composition increased network grant");
Require(!positive.ArbitraryProcessGrantIncreased, "positive composition increased process grant");
Require(string.Equals(positive.EffectiveDecision.Decision, proposeDecision.Decision, StringComparison.Ordinal), "effective decision changed");
Require(string.Equals(positive.EffectiveDecision.AuthorityGranted, proposeDecision.AuthorityGranted, StringComparison.Ordinal), "effective authority changed");
Require(positive.EffectiveDecision.MutationBudgetGranted == proposeDecision.MutationBudgetGranted, "effective mutation budget changed");
Require(positive.EffectiveDecision.NetworkAccessGranted == proposeDecision.NetworkAccessGranted, "effective network grant changed");
Require(positive.EffectiveDecision.ArbitraryProcessExecutionGranted == proposeDecision.ArbitraryProcessExecutionGranted, "effective process grant changed");
Require(!positive.EvidenceContext.CanIncreaseAuthority && !positive.EvidenceContext.CanOverrideDeny && !positive.EvidenceContext.CanCreatePermission,
    "evidence context advertises authority capability");
Require(AllEffectFlagsFalse(positive), "positive composition created an effect/authority flag");

var deniedRequest = policy.CreateRequest(Command("v057-disabled", "propose", 0, false, false));
var deniedDecision = policy.Decide(deniedRequest, agentEnabled: false);
var denied = ProvenanceCapabilityEvidenceComposer.Compose(deniedRequest, deniedDecision, provenance);
Require(string.Equals(denied.EffectiveDecision.Decision, "deny", StringComparison.OrdinalIgnoreCase), "base deny was upgraded");
Require(string.Equals(denied.EffectiveDecision.AuthorityGranted, "none", StringComparison.OrdinalIgnoreCase), "base deny gained authority");

var executeRequest = policy.CreateRequest(Command("v057-execute", "execute", 1, false, false));
var executeDecision = policy.Decide(executeRequest, agentEnabled: true);
var execute = ProvenanceCapabilityEvidenceComposer.Compose(executeRequest, executeDecision, provenance);
Require(string.Equals(execute.EffectiveDecision.Decision, "deny", StringComparison.OrdinalIgnoreCase), "execute deny was upgraded");

var hostile = 0;
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { RequestId = "other-request" }, provenance), "request binding drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest with { RequestedMutationBudget = 1 }, proposeDecision, provenance), "requested mutation budget drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest with { RequestedNetworkAccess = true }, proposeDecision, provenance), "requested network drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest with { RequestedArbitraryProcessExecution = true }, proposeDecision, provenance), "requested process drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { AuthorityGranted = "repository-mutation" }, provenance), "authority promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { MutationBudgetGranted = 1 }, provenance), "mutation grant promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { NetworkAccessGranted = true }, provenance), "network grant promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { ArbitraryProcessExecutionGranted = true }, provenance), "process grant promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision with { Policy = "other-policy" }, provenance), "policy drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { Decision = "PROVENANCE_AUTHORIZED" }), "provenance decision promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { AuthorityCreated = true }), "provenance authority effect");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { GitTagSignatureVerified = true }), "unsigned tag promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { ModelInvocationPerformed = true }), "model effect promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { RuntimeExecutionPerformed = true }), "runtime effect promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { ResponseAuthorityCreated = true }), "response authority promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { DisplayPermitCreated = true }), "display permit promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { ActionPermitCreated = true }), "action permit promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { SuccessorPermitCreated = true }), "successor permit promotion");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with { ObservedEvidenceSha256 = new string('0', 64) }), "evidence digest drift");
ExpectReject(() => ProvenanceCapabilityEvidenceComposer.Compose(proposeRequest, proposeDecision, provenance with
{
    EvidenceBinding = provenance.EvidenceBinding with { Frontier = new string('0', 40) }
}), "evidence frontier drift");

var serviceSource = File.ReadAllText(ServicePath);
foreach (var forbidden in new[]
{
    "HttpClient", "System.Net", "System.Diagnostics.Process", "ProcessStartInfo",
    "File.Write", "File.Delete", "Directory.Create", "Directory.Delete",
    "git push", "git.exe", "cmd.exe", "powershell", "TcpClient", "UdpClient", "Socket"
})
{
    Require(!serviceSource.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"forbidden service capability token found: {forbidden}");
}

var output = new
{
    schema = "matawaka.workbench-v057-provenance-capability-evidence-qualification/v0.57",
    status = "PROVENANCE_CONTEXT_COMPOSED_NO_AUTHORITY_PROMOTION",
    positive_base_decision = proposeDecision.Decision,
    positive_effective_decision = positive.EffectiveDecision.Decision,
    positive_authority = positive.EffectiveDecision.AuthorityGranted,
    deny_preserved = string.Equals(denied.EffectiveDecision.Decision, "deny", StringComparison.OrdinalIgnoreCase),
    execute_deny_preserved = string.Equals(execute.EffectiveDecision.Decision, "deny", StringComparison.OrdinalIgnoreCase),
    hostile_rejections = hostile,
    evidence_decision = provenance.Decision,
    authority_increased = false,
    mutation_budget_increased = false,
    network_grant_increased = false,
    arbitrary_process_grant_increased = false,
    model_authority_created = false,
    runtime_authority_created = false,
    response_authority_created = false,
    display_permit_created = false,
    action_permit_created = false,
    successor_permit_created = false,
    external_effect = false,
    automatic_action = false
};

Console.WriteLine(JsonSerializer.Serialize(output));
return;

void ExpectReject(Action action, string label)
{
    try
    {
        action();
        throw new InvalidOperationException($"Hostile case was accepted: {label}");
    }
    catch (InvalidDataException)
    {
        hostile++;
    }
}

static CommandEnvelope Command(string id, string mode, int mutationBudget, bool network, bool process)
    => new()
    {
        Schema = "matawaka.command/v1",
        Id = id,
        Kind = "agent.run",
        Target = "Matawaka/workbench",
        PolicyProfile = "freeshield-read-only-bridge/v0.7",
        Payload = JsonSerializer.SerializeToElement(new
        {
            mode,
            mutationBudget,
            networkAccess = network,
            arbitraryProcessExecution = process
        })
    };

static bool AllEffectFlagsFalse(CapabilityEvidenceCompositionReceipt receipt)
    => !receipt.ModelAuthorityCreated &&
       !receipt.RuntimeAuthorityCreated &&
       !receipt.ResponseAuthorityCreated &&
       !receipt.DisplayPermitCreated &&
       !receipt.ActionPermitCreated &&
       !receipt.SuccessorPermitCreated;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
