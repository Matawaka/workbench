using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matawaka.Workbench.Protocol;

public sealed class CommandEnvelope
{
    public string Schema { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string PolicyProfile { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed record WorkbenchProgress(
    string CommandId,
    string Event,
    int Percent,
    string Message,
    DateTimeOffset Timestamp,
    string? Phase = null,
    string? ProgressKind = null,
    string? WaitingOn = null,
    string? NextObservableEvent = null,
    string? CheckpointRef = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandTerminalState
{
    Completed,
    Denied,
    Invalid,
    Failed,
    Cancelled
}

public sealed record DecisionScore(string Id, double Score);

public sealed record DecisionEnvelope(
    string Profile,
    IReadOnlyList<DecisionScore> Ranked,
    string Note);

// Workbench-local typed authority bridge. This is deliberately a small protocol
// surface so AgentHost, future HTTP/named-pipe interfaces, and later providers
// can all reason about the same request/decision rather than UI booleans.
public sealed record CapabilityRequest(
    string Schema,
    string Id,
    string Subject,
    string Capability,
    string Operation,
    string Target,
    string RequestedAuthority,
    int RequestedMutationBudget,
    bool RequestedNetworkAccess,
    bool RequestedArbitraryProcessExecution);

public sealed record CapabilityDecision(
    string Schema,
    string RequestId,
    string Decision,
    string Policy,
    string AuthorityGranted,
    int MutationBudgetGranted,
    bool NetworkAccessGranted,
    bool ArbitraryProcessExecutionGranted,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> NonEffects);

public sealed record CapabilityReceipt(
    string Schema,
    CapabilityRequest Request,
    CapabilityDecision Decision);

public sealed record ProtocolSourceBinding(
    string Repository,
    string Frontier,
    string Path,
    string BlobSha,
    string AdapterMode);

public sealed record WorkbenchProgressReceipt(
    string Schema,
    ProtocolSourceBinding SourceBinding,
    string RunId,
    int RunEpoch,
    DateTimeOffset ObservedAt,
    string? CurrentPhase,
    string? ProgressKind,
    string? WaitingOn,
    string? NextObservableEvent,
    string? CheckpointRef,
    bool MeaningfulProgress,
    bool ExtendsLivenessLease,
    bool HiddenReasoningDisclosed,
    bool ExternalEffectAuthorityCreated,
    string ReceiptDigest);

public sealed record WorkbenchHumanLivenessView(
    string Schema,
    ProtocolSourceBinding SourceBinding,
    string RunId,
    int RunEpoch,
    string State,
    DateTimeOffset? LastConfirmedProgressAt,
    string? CurrentPhase,
    string? WaitingOn,
    string? NextObservableEvent,
    bool TerminalStateCertain,
    string NextSafeAction,
    bool HiddenReasoningDisclosed,
    bool ExternalEffectAuthorityCreated);

public static class CommandCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis.run", "catalog.inspect", "catalog.fetch", "agent.run"
    };

    public static CommandEnvelope Parse(string json)
    {
        var command = JsonSerializer.Deserialize<CommandEnvelope>(json, Options)
            ?? throw new InvalidDataException("JSON command is empty.");

        if (!string.Equals(command.Schema, "matawaka.command/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported schema. Expected matawaka.command/v1.");
        if (string.IsNullOrWhiteSpace(command.Id))
            throw new InvalidDataException("Command id is required.");
        if (!AllowedKinds.Contains(command.Kind))
            throw new InvalidDataException($"Unsupported command kind: {command.Kind}");

        return command;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
