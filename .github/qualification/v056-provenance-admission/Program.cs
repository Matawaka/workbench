using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matawaka.Workbench.AgentHost;

const string FixtureSha256 = "ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5";
const int FixtureBytes = 2415;
const string ExpectedDecision = "PROVENANCE_OBSERVED_NO_AUTHORITY";
const string ExpectedRelease = "ea852feeb0e8d92a8977bb251693e7e977913dca";

var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "uu-aap-955-qualification.json");
var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixture-source.json");
if (!File.Exists(fixturePath)) throw new InvalidDataException($"Missing fixture: {fixturePath}");
if (!File.Exists(sourcePath)) throw new InvalidDataException($"Missing fixture source metadata: {sourcePath}");

var fixture = File.ReadAllBytes(fixturePath);
if (fixture.Length != FixtureBytes)
    throw new InvalidDataException($"Fixture byte count mismatch: {fixture.Length}");
var fixtureSha = Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant();
if (!string.Equals(fixtureSha, FixtureSha256, StringComparison.Ordinal))
    throw new InvalidDataException($"Fixture SHA-256 mismatch: {fixtureSha}");

using (var sourceDoc = JsonDocument.Parse(File.ReadAllBytes(sourcePath)))
{
    var source = sourceDoc.RootElement;
    if (source.GetProperty("source_sha256").GetString() != FixtureSha256)
        throw new InvalidDataException("Fixture source metadata SHA-256 mismatch.");
    if (source.GetProperty("source_bytes").GetInt32() != FixtureBytes)
        throw new InvalidDataException("Fixture source metadata byte count mismatch.");
    if (source.GetProperty("historical_operator_publication_receipt_reconstructed").GetBoolean())
        throw new InvalidDataException("Fixture source metadata must not reconstruct the historical operator receipt.");
}

var receipt = C2paProvenanceAdmissionService.AdmitAcceptedWorkbenchV0552(fixture);
if (receipt.Decision != ExpectedDecision) throw new InvalidDataException($"Unexpected admission decision: {receipt.Decision}");
if (receipt.ObservedWorkbenchReleaseCommit != ExpectedRelease) throw new InvalidDataException("Unexpected admitted Workbench release.");
if (!receipt.C2paExternalReferenceBindingEstablished) throw new InvalidDataException("C2PA binding fact missing.");
if (!receipt.ImmutableExternalResolutionExactBytesMatched) throw new InvalidDataException("Immutable byte-match fact missing.");
if (!receipt.LiveC2paValidationAccepted) throw new InvalidDataException("Live C2PA acceptance fact missing.");
if (receipt.GitTagSignatureVerified) throw new InvalidDataException("Unsigned Git tag was promoted to verified.");

var unsafeEffects = new Dictionary<string, bool>
{
    [nameof(receipt.AuthorityCreated)] = receipt.AuthorityCreated,
    [nameof(receipt.NetworkAccessPerformed)] = receipt.NetworkAccessPerformed,
    [nameof(receipt.ProcessStarted)] = receipt.ProcessStarted,
    [nameof(receipt.ModelInvocationPerformed)] = receipt.ModelInvocationPerformed,
    [nameof(receipt.RuntimeExecutionPerformed)] = receipt.RuntimeExecutionPerformed,
    [nameof(receipt.ResponseAuthorityCreated)] = receipt.ResponseAuthorityCreated,
    [nameof(receipt.DisplayPermitCreated)] = receipt.DisplayPermitCreated,
    [nameof(receipt.ActionPermitCreated)] = receipt.ActionPermitCreated,
    [nameof(receipt.SuccessorPermitCreated)] = receipt.SuccessorPermitCreated
};
foreach (var effect in unsafeEffects)
{
    if (effect.Value) throw new InvalidDataException($"Unsafe effect promoted by provenance admission: {effect.Key}");
}

var hostilePassed = 0;
var hostileTotal = 0;
void ExpectReject(string name, byte[] candidate)
{
    hostileTotal++;
    try
    {
        _ = C2paProvenanceAdmissionService.AdmitAcceptedWorkbenchV0552(candidate);
    }
    catch (Exception error) when (error is InvalidDataException or JsonException)
    {
        hostilePassed++;
        return;
    }

    throw new InvalidDataException($"Hostile provenance mutation unexpectedly admitted: {name}");
}

byte[] Mutate(Action<JsonObject> mutation)
{
    var node = JsonNode.Parse(fixture) as JsonObject
        ?? throw new InvalidDataException("Fixture root is not a JSON object.");
    mutation(node);
    return Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

ExpectReject("one-byte append", fixture.Concat(new byte[] { 0x20 }).ToArray());
ExpectReject("schema drift", Mutate(root => root["schema"] = "urn:hostile:schema"));
ExpectReject("verdict drift", Mutate(root => root["verdict"] = "HOSTILE_VERDICT"));
ExpectReject("binding missing", Mutate(root => root["claims"]!["c2pa_external_reference_binding_established"] = false));
ExpectReject("immutable resolution missing", Mutate(root => root["c2pa_binding"]!["immutable_external_resolution_exact_bytes_match"] = false));
ExpectReject("live C2PA acceptance missing", Mutate(root => root["c2pa_binding"]!["live_c2pa_validation_accepted"] = false));
ExpectReject("custom namespace promotion", Mutate(root => root["c2pa_binding"]!["custom_assertion_namespace_registered"] = true));
ExpectReject("source rewrite promotion", Mutate(root => root["claims"]!["source_observation_rewritten"] = true));
ExpectReject("historical operator receipt reconstruction", Mutate(root => root["claims"]!["historical_operator_publication_receipt_reconstructed"] = true));
ExpectReject("publication authority promotion", Mutate(root => root["claims"]!["publication_authority_proven"] = true));
ExpectReject("truth promotion", Mutate(root => root["claims"]!["truth_certified"] = true));
ExpectReject("Git tag signature promotion", Mutate(root => root["claims"]!["git_tag_signature_verified"] = true));
ExpectReject("retroactive original-release C2PA promotion", Mutate(root => root["claims"]!["c2pa_inclusion_in_original_workbench_release_proven"] = true));
ExpectReject("model authority promotion", Mutate(root => root["claims"]!["model_invocation_authority_created"] = true));
ExpectReject("runtime authority promotion", Mutate(root => root["claims"]!["runtime_execution_authority_created"] = true));
ExpectReject("response authority promotion", Mutate(root => root["claims"]!["response_authority_created"] = true));
ExpectReject("action permit promotion", Mutate(root => root["claims"]!["action_permit_created"] = true));
ExpectReject("successor permit promotion", Mutate(root => root["claims"]!["successor_permit_created"] = true));
ExpectReject("unknown top-level semantic", Mutate(root => root["authority"] = "GRANTED"));
ExpectReject("missing claims object", Mutate(root => root.Remove("claims")));

if (hostilePassed != hostileTotal)
    throw new InvalidDataException($"Hostile suite mismatch: {hostilePassed}/{hostileTotal}");

var servicePath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "src", "Matawaka.Workbench.AgentHost", "C2paProvenanceAdmissionService.cs");
var serviceSource = File.ReadAllText(servicePath);
var forbiddenApis = new[]
{
    "HttpClient", "System.Net", "System.Diagnostics.Process", "ProcessStartInfo",
    "Socket", "TcpClient", "UdpClient", "WebRequest", "File.Write", "File.Delete",
    "Directory.Create", "Directory.Delete", "git.exe", "cmd.exe", "powershell"
};
foreach (var token in forbiddenApis)
{
    if (serviceSource.Contains(token, StringComparison.Ordinal))
        throw new InvalidDataException($"Forbidden side-effect API/token found in provenance admission service: {token}");
}

Console.Error.WriteLine($"WORKBENCH_V056_PROVENANCE_ADMISSION_HOSTILE: {hostilePassed}/{hostileTotal} PASS");
Console.Error.WriteLine("WORKBENCH_V056_PROVENANCE_ADMISSION_EFFECTS: NONE");
Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
