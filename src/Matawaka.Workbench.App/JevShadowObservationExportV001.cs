using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed record JevShadowAuthorityObservationV001(
    string RequestId,
    string Subject,
    string Capability,
    string Operation,
    string Target,
    string RequestedAuthority,
    int RequestedMutationBudget,
    bool RequestedNetworkAccess,
    bool RequestedArbitraryProcessExecution,
    string Decision,
    string Policy,
    string AuthorityGranted,
    int MutationBudgetGranted,
    bool NetworkAccessGranted,
    bool ArbitraryProcessExecutionGranted);

public sealed record JevShadowObservationEnvelopeV001(
    string Schema,
    string EvidenceKind,
    string NormativeEffect,
    string AuthorityIssuance,
    string Principle,
    DateTimeOffset ObservedAt,
    string CommandId,
    string CommandKind,
    string CommandTarget,
    string CommandTerminalState,
    string PayloadSha256,
    JevShadowAuthorityObservationV001? Authority,
    bool ContainsCommandPayload,
    bool ExternalizationAuthorized,
    bool ProviderInvocationAuthorized,
    bool DecisionReadbackSupported,
    bool AuthorityCreated,
    bool DisplayPermitCreated,
    bool ActionPermitCreated,
    IReadOnlyList<string> NonEffects);

public sealed record JevShadowObservationExportResultV001(
    string Schema,
    string Status,
    bool Exported,
    string? ArtifactPath,
    JevShadowObservationEnvelopeV001? Envelope);

/// <summary>
/// Post-terminal, local-only Jev research observation export.
///
/// This service never invokes Jev, never performs network I/O, and never returns
/// a value that can revise the already-produced CommandResult. Enabling the export
/// only creates a local inert evidence candidate for a separately started sidecar.
/// </summary>
public sealed class JevShadowObservationExportV001Service
{
    public const string EnvironmentVariable = "MATAWAKA_JEV_SHADOW_ROOT";
    public const string EnvelopeSchema = "matawaka.jev-workbench-shadow-observation/v0.1";
    public const string ResultSchema = "matawaka.jev-workbench-shadow-export-result/v0.1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<JevShadowObservationExportResultV001> TryExportAsync(
        CommandEnvelope command,
        CommandResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        var root = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            return new JevShadowObservationExportResultV001(
                ResultSchema,
                "SKIPPED_DISABLED",
                false,
                null,
                null);
        }

        return await ExportAsync(root, command, result, cancellationToken);
    }

    public async Task<JevShadowObservationExportResultV001> ExportAsync(
        string root,
        CommandEnvelope command,
        CommandResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        var envelope = Compose(command, result);
        var fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);

        var artifactPath = Path.Combine(
            fullRoot,
            $"jev-shadow-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Token(command.Id)}.json");

        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(envelope, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        return new JevShadowObservationExportResultV001(
            ResultSchema,
            "EXPORTED_LOCAL_ONLY_NO_AUTHORITY",
            true,
            artifactPath,
            envelope);
    }

    public JevShadowObservationEnvelopeV001 Compose(
        CommandEnvelope command,
        CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        var payloadText = command.Payload.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : command.Payload.GetRawText();

        JevShadowAuthorityObservationV001? authority = null;
        if (result.Authority is CapabilityReceipt receipt)
        {
            authority = new JevShadowAuthorityObservationV001(
                receipt.Request.Id,
                receipt.Request.Subject,
                receipt.Request.Capability,
                receipt.Request.Operation,
                receipt.Request.Target,
                receipt.Request.RequestedAuthority,
                receipt.Request.RequestedMutationBudget,
                receipt.Request.RequestedNetworkAccess,
                receipt.Request.RequestedArbitraryProcessExecution,
                receipt.Decision.Decision,
                receipt.Decision.Policy,
                receipt.Decision.AuthorityGranted,
                receipt.Decision.MutationBudgetGranted,
                receipt.Decision.NetworkAccessGranted,
                receipt.Decision.ArbitraryProcessExecutionGranted);
        }

        return new JevShadowObservationEnvelopeV001(
            EnvelopeSchema,
            "PROBABILISTIC_JUDGMENT_SHADOW_INPUT_CANDIDATE",
            "NONE",
            "OUT_OF_SCOPE",
            "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
            DateTimeOffset.UtcNow,
            command.Id,
            command.Kind,
            command.Target,
            result.TerminalState.ToString(),
            Sha256(payloadText),
            authority,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "command payload bytes are not included; only a SHA-256 digest is exported",
                "export is local-only and does not invoke TypeSafe or any model provider",
                "externalization requires a separately started sidecar and separate operator authority",
                "shadow output has no readback path into the current CommandResult",
                "shadow observation cannot create or revise CapabilityDecision",
                "shadow observation cannot create display/action/successor permits",
                "shadow export failure must not revise the already-applied terminal state"
            });
    }

    private static string Token(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant()[..16];

    private static string Sha256(string value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
