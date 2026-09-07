using System.Security.Cryptography;
using System.Text.Json;

namespace Matawaka.Workbench.App;

internal sealed record PublicMainImportEvidenceV0601(
    string ReceiptPath,
    string ReceiptSha256,
    string ImportedCommit,
    string RefsDigestBefore,
    string RefsDigestAfter,
    bool ObjectDatabaseChanged);

/// <summary>
/// Verifies the standalone fixed no-ref public-main object import receipt.
/// The import is evidence for a second local checkpoint parent only; it is not
/// publication authority and does not reinterpret the public commit's semantics.
/// </summary>
internal static class PublicMainImportReceiptVerifierV0601
{
    internal const string Schema = "matawaka.workbench-v0601-public-main-import-receipt/v0.1";
    internal const string Status = "EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION";
    internal const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    internal const string PublicMainCommit = "6541dc32182c970c8e1a6ade426a6cee7086511b";
    internal const string InstalledHead = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    internal const string InstalledTag = "workbench-v0.55.2-accepted";
    internal const string ReceiptFileName = "public-main-6541dc32182c970c8e1a6ade426a6cee7086511b.json";

    internal static PublicMainImportEvidenceV0601 FindExact(string workspaceRoot)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var path = Path.Combine(repositoryRoot, "artifacts", "convergence-v0601", ReceiptFileName);
        if (!File.Exists(path))
            throw new InvalidDataException($"Exact v0.60.1 public-main import receipt missing: {path}");

        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Public-main import receipt must be a JSON object.");

        RequireString(root, "Schema", Schema);
        RequireString(root, "Status", Status);
        RequireString(root, "FixedRemote", RemoteUrl);
        RequireString(root, "ExpectedRemoteMain", PublicMainCommit);
        RequireString(root, "RemoteMainObserved", PublicMainCommit);
        RequireString(root, "ImportedCommit", PublicMainCommit);
        RequireString(root, "LocalHeadBefore", InstalledHead);
        RequireString(root, "LocalHeadAfter", InstalledHead);
        RequireString(root, "LocalAcceptedTag", InstalledTag);
        RequireString(root, "LocalAcceptedTagCommit", InstalledHead);

        RequireBoolean(root, "GitRefsUnchanged", true);
        RequireBoolean(root, "RefMutationPerformed", false);
        RequireBoolean(root, "RemoteWritePerformed", false);
        RequireBoolean(root, "SourceMutationPerformed", false);
        RequireBoolean(root, "AutomaticRetryPerformed", false);
        RequireBoolean(root, "FetchUsedNoRef", true);
        RequireBoolean(root, "ObjectPresentAfter", true);

        var before = RequireString(root, "RefsDigestBefore");
        var after = RequireString(root, "RefsDigestAfter");
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Public-main import receipt shows a Git refs digest change.");

        var objectDatabaseChanged = RequireBoolean(root, "ObjectDatabaseChanged");
        var receiptSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new PublicMainImportEvidenceV0601(path, receiptSha, PublicMainCommit, before, after, objectDatabaseChanged);
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required for v0.60.1 import evidence.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string RequireString(JsonElement root, string propertyName, string? exact = null)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Public-main import receipt field {propertyName} is missing or not a string.");
        var observed = value.GetString() ?? string.Empty;
        if (exact is not null && !string.Equals(observed, exact, StringComparison.Ordinal))
            throw new InvalidDataException($"Public-main import receipt field {propertyName} mismatch: observed={observed}; expected={exact}");
        return observed;
    }

    private static bool RequireBoolean(JsonElement root, string propertyName, bool? exact = null)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Public-main import receipt field {propertyName} is missing or not a boolean.");
        var observed = value.GetBoolean();
        if (exact is not null && observed != exact.Value)
            throw new InvalidDataException($"Public-main import receipt field {propertyName} mismatch: observed={observed}; expected={exact.Value}");
        return observed;
    }
}
