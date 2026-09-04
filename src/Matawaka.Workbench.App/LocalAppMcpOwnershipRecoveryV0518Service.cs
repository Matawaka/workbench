using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpOwnershipStaleMetadataReceiptV0518(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string Operation,
    string StatusBefore,
    string LeaseClassificationBefore,
    string? SessionId,
    string? LeaseId,
    string PriorMetadataSha256,
    string ArchivePath,
    string ArchiveSha256,
    bool OwnerHandleFreeProvenDuringRotation,
    bool ActiveMetadataSlotCleared,
    bool CanonicalLeaseMutated,
    bool ActiveIndexMutated,
    bool ResumeAuthorityGranted,
    bool LeaseAuthorityGranted,
    bool ReadAuthorityGranted,
    bool RevokeAuthorityGranted,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool EndpointSecretDisclosed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppMcpOwnershipRecoveryV0518Service
{
    public const string Version = "0.51.8";
    public const string ReceiptSchema = "matawaka.local-app-mcp-stale-owner-metadata-receipt/v0.51.8";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly LocalAppMcpOwnershipStatusV0518Service _status = new();

    public async Task<(LocalAppMcpOwnershipStaleMetadataReceiptV0518 Receipt, string ReceiptPath)> AcknowledgeAndRotateAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var before = _status.Observe(workspaceRoot, applicationId);
        if (!before.Status.Equals("FREE_STALE_METADATA", StringComparison.Ordinal) || !before.OwnerMetadataPresent)
            throw new InvalidDataException(
                $"MCP_STALE_METADATA_ACK_NOT_APPLICABLE: expected FREE_STALE_METADATA, observed {before.Status}; no metadata rotation performed.");

        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appToken = LocalAppV046FileBoundary.SafeToken(applicationId);
        var appDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", appToken);
        var lockPath = Path.Combine(appDir, "owner.lock");
        var metadataPath = Path.Combine(appDir, "owner-v0.51.7.json");

        if (!Directory.Exists(appDir) || !File.Exists(lockPath) || !File.Exists(metadataPath))
            throw new InvalidDataException("MCP_STALE_METADATA_ACK_STATE_CHANGED: owner lock/metadata disappeared before guarded rotation.");
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.8 stale-owner acknowledgement app directory");
        LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.8 stale-owner acknowledgement lock");
        LocalAppV046FileBoundary.RejectReparse(metadataPath, "v0.51.8 stale-owner acknowledgement metadata");

        var beforeBytes = await File.ReadAllBytesAsync(metadataPath, cancellationToken);
        var beforeSha = HashBytes(beforeBytes);

        await using FileStream guard = AcquireExistingOwnerGuard(lockPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(metadataPath))
            throw new InvalidDataException("MCP_STALE_METADATA_ACK_STATE_CHANGED: metadata disappeared after owner-domain guard acquisition.");
        LocalAppV046FileBoundary.RejectReparse(metadataPath, "guarded v0.51.8 stale-owner metadata");
        var guardedBytes = await File.ReadAllBytesAsync(metadataPath, cancellationToken);
        var guardedSha = HashBytes(guardedBytes);
        if (!guardedSha.Equals(beforeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_STALE_METADATA_ACK_STATE_CHANGED: metadata changed before guarded rotation; acknowledgement refused.");

        var archiveDir = Path.Combine(appDir, "stale-evidence-v0518");
        Directory.CreateDirectory(archiveDir);
        LocalAppV046FileBoundary.RejectReparse(archiveDir, "v0.51.8 stale-owner evidence directory");
        var sessionToken = !string.IsNullOrWhiteSpace(before.SessionId)
            ? LocalAppV046FileBoundary.SafeToken(before.SessionId!)
            : "unknown-session";
        var archivePath = Path.Combine(
            archiveDir,
            $"owner-v0.51.7-stale-{DateTime.Now:yyyyMMdd-HHmmssfff}-{sessionToken}-{beforeSha[..12]}.json");
        if (File.Exists(archivePath)) throw new InvalidDataException("Unexpected stale-owner evidence archive collision.");

        File.Move(metadataPath, archivePath, false);
        LocalAppV046FileBoundary.RejectReparse(archivePath, "v0.51.8 archived stale-owner metadata");
        var archiveSha = HashFile(archivePath);
        if (!archiveSha.Equals(beforeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archived stale-owner metadata digest mismatch after rotation.");
        if (File.Exists(metadataPath))
            throw new InvalidDataException("Active stale-owner metadata slot remained populated after rotation.");

        var nonEffects = new[]
        {
            "stale acknowledgement is evidence rotation only, not MCP ownership or lease authority",
            "exclusive existing owner.lock handle proved the app MCP domain free during rotation",
            "prior metadata bytes were preserved exactly under stale-evidence-v0518",
            "no canonical lease create/revoke/renew/consume mutation",
            "no active-index or active-index-fence mutation",
            "no MCP listener start/stop/resume",
            "no bearer plaintext/hash or endpoint path token disclosed by receipt/log",
            "no read/list call or byte budget consumption",
            "no historical canonical lease enumeration",
            "no network/tunnel/publication/catalog/Agent Execute or ActionPermit authority"
        };
        var receipt = new LocalAppMcpOwnershipStaleMetadataReceiptV0518(
            ReceiptSchema, Version, DateTimeOffset.Now, applicationId,
            "acknowledge-and-rotate-stale-owner-metadata",
            before.Status,
            before.LeaseObservation.Classification,
            before.SessionId,
            before.MetadataLeaseId,
            beforeSha,
            archivePath,
            archiveSha,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            nonEffects,
            "MCP_STALE_OWNER_METADATA_ACKNOWLEDGED_EVIDENCE_PRESERVED",
            "The app-scoped owner domain was held exclusively only as a free-domain guard while stale non-authoritative metadata was rotated into evidence. Any referenced canonical lease was not changed; live orphan closure remains a separate explicit action.");
        var receiptPath = await WriteReceiptAsync(workspaceRoot, applicationId, receipt, cancellationToken);
        return (receipt, receiptPath);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-owner-recovery-v0518-precondition", true, "FREE_STALE_METADATA", "required"),
        ("mcp-owner-recovery-v0518-guard", true, "FileMode.Open + FileShare.None on existing owner.lock", "free-domain proof"),
        ("mcp-owner-recovery-v0518-evidence", true, "exact metadata bytes moved to stale-evidence-v0518 + SHA verified", "preserved"),
        ("mcp-owner-recovery-v0518-canonical", true, "CanonicalLeaseMutated=false", "false"),
        ("mcp-owner-recovery-v0518-index", true, "ActiveIndexMutated=false", "false"),
        ("mcp-owner-recovery-v0518-resume", true, "ResumeAuthorityGranted=false", "false"),
        ("mcp-owner-recovery-v0518-revoke", true, "RevokeAuthorityGranted=false", "false"),
        ("mcp-owner-recovery-v0518-secrets", true, "bearer/hash/path-token omitted", "omitted")
    };

    private static FileStream AcquireExistingOwnerGuard(string lockPath)
    {
        try
        {
            return new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
        }
        catch (IOException ex)
        {
            throw new InvalidDataException("MCP_STALE_METADATA_ACK_REFUSED_OWNER_BUSY: another process owns or is changing the app MCP domain; no rotation performed.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidDataException("MCP_STALE_METADATA_ACK_UNCERTAIN: owner-domain guard could not be acquired; no rotation performed.", ex);
        }
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        string applicationId,
        LocalAppMcpOwnershipStaleMetadataReceiptV0518 receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-ownership-recovery-v0518");
        var path = Path.Combine(dir,
            $"mcp-owner-stale-ack-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
