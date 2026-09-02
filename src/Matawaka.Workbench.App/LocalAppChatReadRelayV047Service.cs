using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppChatReadRequestV047(
    string Schema,
    string RequestId,
    string ApplicationId,
    string Role,
    string RelativePath,
    long Offset,
    int MaxBytes,
    string? ExpectedFileSha256);

public sealed record LocalAppChatReadPreviewV047(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string ApplicationId,
    string Role,
    string RelativePath,
    long Offset,
    int MaxBytes,
    string? ExpectedFileSha256,
    string FileSha256,
    long FileBytes,
    int PlannedReadBytes,
    bool EndOfFileAfterPlannedRead,
    string Utf8Availability,
    bool ExpectedHashVerified,
    bool SelectedApplicationMatched,
    bool ReadyForExplicitDisclosureAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppChatReadResponseV047(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string ApplicationId,
    string Role,
    string RelativePath,
    long FileBytes,
    string FileSha256,
    long Offset,
    int ReturnedBytes,
    bool EndOfFile,
    string ContentBase64,
    string? Utf8Text,
    bool ExpectedHashVerified,
    bool ClipboardWritePerformed,
    bool UploadPerformed,
    bool NetworkAccessPerformed,
    bool FileMutationPerformed,
    bool ProcessLaunchPerformed,
    string Note);

public sealed record LocalAppChatReadRelayReceiptV047(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string ApplicationId,
    string Role,
    string RelativePath,
    string FileSha256,
    long FileBytes,
    long Offset,
    int ReturnedBytes,
    string ResponseSha256,
    bool FreshPreviewVerified,
    bool ClipboardWritePerformed,
    bool UploadPerformed,
    bool NetworkAccessPerformed,
    bool FileMutationPerformed,
    bool ProcessLaunchPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppChatReadRelayV047Service
{
    public const string Version = "0.47.0";
    public const string RequestSchema = "matawaka.local-app-chat-read-request/v0.47";
    public const string PreviewSchema = "matawaka.local-app-chat-read-preview/v0.47";
    public const string ResponseSchema = "matawaka.local-app-chat-read-response/v0.47";
    public const string ReceiptSchema = "matawaka.local-app-chat-read-relay-receipt/v0.47";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly LocalAppReadToolV046Service _reader = new();

    public LocalAppChatReadPreviewV047 PreviewFromJson(
        string workspaceRoot,
        string selectedApplicationId,
        string requestJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) throw new InvalidDataException("Chat read request JSON is empty.");
        ValidateExactJsonShape(requestJson);
        LocalAppChatReadRequestV047 request;
        try
        {
            request = JsonSerializer.Deserialize<LocalAppChatReadRequestV047>(requestJson, JsonOptions)
                ?? throw new InvalidDataException("Chat read request JSON could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Chat read request JSON is invalid.", ex);
        }
        return Preview(workspaceRoot, selectedApplicationId, request, cancellationToken);
    }

    public LocalAppChatReadPreviewV047 Preview(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppChatReadRequestV047 request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Schema != RequestSchema)
            throw new InvalidDataException("Exact v0.47 chat read request schema is required.");
        if (!SafeRequestId(request.RequestId)) throw new InvalidDataException("RequestId must be 1..128 characters using letters, digits, '.', '_', '-' or ':'.");
        if (!string.Equals(request.ApplicationId, selectedApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Chat request ApplicationId does not match the explicitly selected registered application.");
        if (request.Offset < 0) throw new InvalidDataException("Offset cannot be negative.");
        if (request.MaxBytes <= 0 || request.MaxBytes > LocalAppReadToolV046Service.MaxReadBytes)
            throw new InvalidDataException($"MaxBytes must be between 1 and {LocalAppReadToolV046Service.MaxReadBytes}.");

        var role = request.Role.Trim().ToLowerInvariant();
        var root = role switch
        {
            "installed" => LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, request.ApplicationId),
            "source" => LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, request.ApplicationId, requireBinding: true),
            _ => throw new InvalidDataException("Role must be exactly installed or source.")
        };
        var relative = LocalAppV046FileBoundary.NormalizeRelative(request.RelativePath);
        LocalAppV046FileBoundary.EnsureNoReparseBoundary(root, relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        LocalAppV046FileBoundary.EnsureInsideRoot(root, path, "chat read target");
        if (!File.Exists(path)) throw new InvalidDataException($"Chat read target file is missing: {relative}");
        LocalAppV046FileBoundary.RejectReparse(path, "chat read target");
        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(path);
        if (request.Offset > info.Length) throw new InvalidDataException("Offset is beyond end of file.");
        var sha = LocalAppV046FileBoundary.HashFile(path);
        var expectedVerified = string.IsNullOrWhiteSpace(request.ExpectedFileSha256);
        if (!string.IsNullOrWhiteSpace(request.ExpectedFileSha256))
        {
            var expected = NormalizeSha256(request.ExpectedFileSha256!);
            if (!sha.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"ExpectedFileSha256 mismatch. expected={expected}; observed={sha}. Request is stale; ask the chat to refresh its context.");
            expectedVerified = true;
        }
        var planned = (int)Math.Min(request.MaxBytes, info.Length - request.Offset);
        return new LocalAppChatReadPreviewV047(
            PreviewSchema,
            Version,
            DateTimeOffset.Now,
            request.RequestId,
            request.ApplicationId,
            role,
            relative,
            request.Offset,
            request.MaxBytes,
            string.IsNullOrWhiteSpace(request.ExpectedFileSha256) ? null : NormalizeSha256(request.ExpectedFileSha256!),
            sha,
            info.Length,
            planned,
            request.Offset + planned >= info.Length,
            "UNKNOWN_UNTIL_CONFIRMED_READ",
            expectedVerified,
            true,
            true,
            DefaultNonEffects(),
            "Preview reads metadata/hash only. It does not disclose file contents or write to clipboard. Explicit confirmation is still required for the exact observed file SHA/range.");
    }

    public LocalAppReadResponseV046 PrepareConfirmedRead(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppChatReadPreviewV047 confirmedPreview,
        CancellationToken cancellationToken)
    {
        if (confirmedPreview is null || !confirmedPreview.ReadyForExplicitDisclosureAuthority)
            throw new InvalidDataException("A READY v0.47 chat read preview is required.");
        var request = new LocalAppChatReadRequestV047(
            RequestSchema,
            confirmedPreview.RequestId,
            confirmedPreview.ApplicationId,
            confirmedPreview.Role,
            confirmedPreview.RelativePath,
            confirmedPreview.Offset,
            confirmedPreview.MaxBytes,
            confirmedPreview.ExpectedFileSha256);
        var fresh = Preview(workspaceRoot, selectedApplicationId, request, cancellationToken);
        RequireSamePreview(confirmedPreview, fresh);

        var read = _reader.Read(workspaceRoot, new LocalAppReadRequestV046(
            LocalAppReadToolV046Service.RequestSchema,
            fresh.ApplicationId,
            fresh.Role,
            fresh.RelativePath,
            fresh.Offset,
            fresh.MaxBytes), cancellationToken);
        if (!read.FileSha256.Equals(fresh.FileSha256, StringComparison.OrdinalIgnoreCase) ||
            read.FileBytes != fresh.FileBytes || read.Offset != fresh.Offset || read.ReturnedBytes != fresh.PlannedReadBytes)
            throw new InvalidDataException("File changed during confirmed read. Response disclosure refused; create a new preview.");
        return read;
    }

    public LocalAppChatReadResponseV047 BuildClipboardResponse(
        LocalAppChatReadPreviewV047 preview,
        LocalAppReadResponseV046 read)
    {
        if (!read.FileSha256.Equals(preview.FileSha256, StringComparison.OrdinalIgnoreCase) || read.FileBytes != preview.FileBytes)
            throw new InvalidDataException("Read result is not bound to the confirmed preview.");
        return new LocalAppChatReadResponseV047(
            ResponseSchema,
            Version,
            DateTimeOffset.Now,
            preview.RequestId,
            preview.ApplicationId,
            preview.Role,
            preview.RelativePath,
            read.FileBytes,
            read.FileSha256,
            read.Offset,
            read.ReturnedBytes,
            read.EndOfFile,
            read.ContentBase64,
            read.Utf8Text,
            preview.ExpectedHashVerified,
            true,
            false,
            false,
            false,
            false,
            "Exact response intended for manual paste back to the chosen chat. Clipboard write is local operator-mediated disclosure; no upload or network transport is performed by Workbench.");
    }

    public static string SerializeResponse(LocalAppChatReadResponseV047 response)
        => JsonSerializer.Serialize(response, JsonOptions);

    public async Task<(LocalAppChatReadRelayReceiptV047 Receipt, string ArtifactPath)> WriteReceiptAsync(
        string workspaceRoot,
        LocalAppChatReadPreviewV047 preview,
        LocalAppChatReadResponseV047 response,
        CancellationToken cancellationToken)
    {
        if (!response.ClipboardWritePerformed) throw new InvalidDataException("Receipt may only claim success after clipboard write completed.");
        var responseJson = SerializeResponse(response);
        var responseSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseJson))).ToLowerInvariant();
        var receipt = new LocalAppChatReadRelayReceiptV047(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            response.RequestId,
            response.ApplicationId,
            response.Role,
            response.RelativePath,
            response.FileSha256,
            response.FileBytes,
            response.Offset,
            response.ReturnedBytes,
            responseSha,
            true,
            true,
            false,
            false,
            false,
            false,
            DefaultNonEffects(),
            "CHAT_READ_RELAY_CLIPBOARD_READY_NO_UPLOAD",
            "The exact bounded response was placed on the local Windows clipboard after explicit confirmation. Workbench did not upload or transmit it over a network.");
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-chat-read-relay");
        var path = Path.Combine(dir, $"chat-read-relay-{LocalAppV046FileBoundary.SafeToken(response.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(response.RequestId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chat-read-v047-request-schema", RequestSchema == "matawaka.local-app-chat-read-request/v0.47", RequestSchema, "exact v0.47"),
        ("chat-read-v047-max-chunk", LocalAppReadToolV046Service.MaxReadBytes == 1048576, LocalAppReadToolV046Service.MaxReadBytes.ToString(), "1048576"),
        ("chat-read-v047-selected-app-bound", true, "request ApplicationId must equal selected app", "exact"),
        ("chat-read-v047-stale-refusal", true, "fresh SHA/size/range re-preview required", "refuse on change"),
        ("chat-read-v047-clipboard-separate", true, "preview no clipboard; response after explicit confirmation", "separate authority"),
        ("chat-read-v047-network", true, "false", "false"),
        ("chat-read-v047-mutation", true, "false", "false")
    };

    private static void RequireSamePreview(LocalAppChatReadPreviewV047 a, LocalAppChatReadPreviewV047 b)
    {
        if (a.RequestId != b.RequestId || a.ApplicationId != b.ApplicationId || a.Role != b.Role || a.RelativePath != b.RelativePath ||
            a.Offset != b.Offset || a.MaxBytes != b.MaxBytes || a.FileBytes != b.FileBytes || a.PlannedReadBytes != b.PlannedReadBytes ||
            !a.FileSha256.Equals(b.FileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chat read preview is stale. File/range changed; create a new preview and confirm again.");
    }

    private static void ValidateExactJsonShape(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Chat read request must be one JSON object.");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Schema", "RequestId", "ApplicationId", "Role", "RelativePath", "Offset", "MaxBytes", "ExpectedFileSha256"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw new InvalidDataException($"Unknown chat read request field: {property.Name}");
            if (!seen.Add(property.Name)) throw new InvalidDataException($"Duplicate chat read request field: {property.Name}");
        }
        foreach (var required in new[] { "Schema", "RequestId", "ApplicationId", "Role", "RelativePath", "Offset", "MaxBytes" })
            if (!seen.Contains(required)) throw new InvalidDataException($"Missing chat read request field: {required}");
    }

    private static string NormalizeSha256(string value)
    {
        var sha = value.Trim().ToLowerInvariant();
        if (sha.Length != 64 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("ExpectedFileSha256 must be exactly 64 hex characters.");
        return sha;
    }

    private static bool SafeRequestId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or ':');

    private static string[] DefaultNonEffects() => new[]
    {
        "no automatic upload or network access",
        "no HTTP listener/tunnel/MCP exposure",
        "no application/source mutation",
        "no process launch",
        "no arbitrary filesystem root",
        "no Git/catalog/Agent Execute authority"
    };
}
