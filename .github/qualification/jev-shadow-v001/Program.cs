using System.Text.Json;
using Matawaka.Workbench.App;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

var service = new JevShadowObservationExportV001Service();
var temp = Path.Combine(Path.GetTempPath(), "matawaka-jev-shadow-v001-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var payload = JsonDocument.Parse("{\"secret\":\"TOP_SECRET_PAYLOAD\",\"task\":\"synthetic\"}").RootElement.Clone();
    var command = new CommandEnvelope
    {
        Schema = "matawaka.command/v1",
        Id = "shadow-fixture-001",
        Kind = "agent.run",
        Target = "synthetic-target",
        PolicyProfile = "qualification",
        Payload = payload
    };

    var request = new CapabilityRequest(
        "matawaka.capability-request/v1",
        "cap-shadow-001",
        "qualification-subject",
        "agent.propose",
        "inspect synthetic fixture",
        "synthetic-target",
        "read-only",
        0,
        false,
        false);

    var decision = new CapabilityDecision(
        "matawaka.capability-decision/v1",
        request.Id,
        "allow",
        "qualification-policy",
        "read-only",
        0,
        false,
        false,
        new[] { "synthetic fixture" },
        new[] { "no mutation", "no network", "no process" });

    var authority = new CapabilityReceipt(
        "matawaka.capability-receipt/v1",
        request,
        decision);

    var result = new CommandResult(
        "agent.run",
        CommandTerminalState.Completed,
        "synthetic qualification result",
        Authority: authority);

    Environment.SetEnvironmentVariable(JevShadowObservationExportV001Service.EnvironmentVariable, null);
    var disabled = await service.TryExportAsync(command, result);
    Require(!disabled.Exported && disabled.Status == "SKIPPED_DISABLED", "disabled shadow export must write nothing");
    Require(!Directory.EnumerateFiles(temp).Any(), "disabled shadow export created an artifact");

    Environment.SetEnvironmentVariable(JevShadowObservationExportV001Service.EnvironmentVariable, temp);
    var exported = await service.TryExportAsync(command, result);
    Require(exported.Exported, "enabled shadow export did not export");
    Require(exported.Envelope is not null, "enabled shadow export did not return envelope");
    Require(File.Exists(exported.ArtifactPath), "shadow artifact does not exist");

    var text = await File.ReadAllTextAsync(exported.ArtifactPath!);
    Require(!text.Contains("TOP_SECRET_PAYLOAD", StringComparison.Ordinal), "command payload bytes leaked into shadow artifact");
    Require(text.Contains("sha256:", StringComparison.Ordinal), "payload digest missing");

    var envelope = exported.Envelope!;
    Require(envelope.NormativeEffect == "NONE", "shadow envelope gained normative effect");
    Require(envelope.AuthorityIssuance == "OUT_OF_SCOPE", "shadow envelope gained authority issuance");
    Require(!envelope.ContainsCommandPayload, "shadow envelope claims command payload inclusion");
    Require(!envelope.ExternalizationAuthorized, "shadow envelope authorized externalization");
    Require(!envelope.ProviderInvocationAuthorized, "shadow envelope authorized provider invocation");
    Require(!envelope.DecisionReadbackSupported, "shadow envelope enabled decision readback");
    Require(!envelope.AuthorityCreated, "shadow envelope created authority");
    Require(!envelope.DisplayPermitCreated, "shadow envelope created display permit");
    Require(!envelope.ActionPermitCreated, "shadow envelope created action permit");

    Require(result.TerminalState == CommandTerminalState.Completed, "shadow export changed terminal state");
    Require(ReferenceEquals(result.Authority, authority), "shadow export replaced authority receipt");
    Require(decision.Decision == "allow", "shadow export mutated capability decision");
    Require(!decision.NetworkAccessGranted, "shadow export mutated network grant");
    Require(!decision.ArbitraryProcessExecutionGranted, "shadow export mutated process grant");

    Console.WriteLine("PASS jev-shadow-v0.1");
    Console.WriteLine($"artifact={Path.GetFileName(exported.ArtifactPath)}");
    Console.WriteLine("payloadBytesIncluded=false");
    Console.WriteLine("providerInvocation=false");
    Console.WriteLine("authorityReadback=false");
    Console.WriteLine("terminalStateUnchanged=true");
}
finally
{
    Environment.SetEnvironmentVariable(JevShadowObservationExportV001Service.EnvironmentVariable, null);
    try { Directory.Delete(temp, true); } catch { }
}
