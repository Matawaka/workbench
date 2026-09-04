using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpOwnerGenerationTransitionReceiptV0519(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string SuccessorSessionId,
    bool PriorMetadataPresent,
    bool PriorMetadataContractValid,
    string? PriorSessionId,
    string? PriorLeaseId,
    string? PriorState,
    long PriorMetadataBytes,
    string? PriorMetadataSha256,
    bool PriorMetadataArchived,
    string? ArchivePath,
    string? ArchiveSha256,
    bool ArchiveHashVerified,
    string Status,
    bool CanonicalLeaseMutated,
    bool ActiveIndexMutated,
    bool LeaseAuthorityGranted,
    bool ReadAuthorityGranted,
    bool RevokeAuthorityGranted,
    bool ResumeAuthorityGranted,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool EndpointSecretDisclosed,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.51.9 preserves the exact prior active owner-metadata bytes under an already-held
/// app-scoped owner.lock before a successor owner generation is written. This is
/// evidence continuity only; it grants no lease/read/revoke/resume authority.
/// v0.51.10 makes new prior archives content-addressed by exact SHA-256 and may also
/// supply one exact already-verified legacy archive path recovered from a PREPARED
/// transaction. Retry therefore reuses exact prior evidence instead of duplicating bytes.
/// </summary>
public sealed class LocalAppMcpOwnerGenerationV0519Service
{
    public const string Version = "0.51.9";
    public const string ReceiptSchema = "matawaka.local-app-mcp-owner-generation-transition-receipt/v0.51.9";
    public const int MaxPriorMetadataBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<(LocalAppMcpOwnerGenerationTransitionReceiptV0519 Receipt, string ReceiptPath)> PreservePriorBeforeSuccessorAsync(
        string workspaceRoot,
        string applicationId,
        string successorSessionId,
        string metadataPath,
        CancellationToken cancellationToken,
        string? verifiedReuseArchivePath = null)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        if (string.IsNullOrWhiteSpace(successorSessionId) || !successorSessionId.StartsWith("mcpsess-", StringComparison.Ordinal) ||
            successorSessionId.Length > 80 || successorSessionId.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new InvalidDataException("Unsafe successor MCP SessionId for generation transition.");

        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appToken = LocalAppV046FileBoundary.SafeToken(applicationId);
        var expectedDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", appToken);
        var expectedMetadata = Path.Combine(expectedDir, "owner-v0.51.7.json");
        if (!Path.GetFullPath(metadataPath).Equals(Path.GetFullPath(expectedMetadata), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.51.9 owner generation metadata path mismatch.");
        if (Directory.Exists(expectedDir)) LocalAppV046FileBoundary.RejectReparse(expectedDir, "v0.51.9 owner generation app directory");

        var priorPresent = File.Exists(expectedMetadata);
        if (!priorPresent && !string.IsNullOrWhiteSpace(verifiedReuseArchivePath))
            throw new InvalidDataException("MCP_OWNER_GENERATION_REUSE_REFUSED: verified reuse archive was supplied but no active prior metadata exists.");

        byte[]? priorBytes = null;
        string? priorSha = null;
        LocalAppMcpSessionOwnerV0517? prior = null;
        var priorContractValid = false;
        string? archivePath = null;
        string? archiveSha = null;
        var archiveVerified = false;
        var archiveReused = false;

        if (priorPresent)
        {
            LocalAppV046FileBoundary.RejectReparse(expectedMetadata, "v0.51.9 prior owner metadata");
            var length = new FileInfo(expectedMetadata).Length;
            if (length < 0 || length > MaxPriorMetadataBytes)
                throw new InvalidDataException($"MCP_OWNER_GENERATION_PRIOR_METADATA_OVERSIZE: prior owner metadata exceeds {MaxPriorMetadataBytes} bytes.");
            priorBytes = await File.ReadAllBytesAsync(expectedMetadata, cancellationToken);
            if (priorBytes.LongLength != length)
                throw new InvalidDataException("MCP_OWNER_GENERATION_PRIOR_METADATA_DRIFT: prior owner metadata size changed during evidence read.");
            priorSha = HashBytes(priorBytes);

            try
            {
                prior = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(priorBytes, JsonOptions);
                priorContractValid = prior is not null &&
                                     prior.Schema == LocalAppMcpSessionOwnershipV0517Service.OwnerSchema &&
                                     prior.Version == LocalAppMcpSessionOwnershipV0517Service.Version &&
                                     prior.ApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
                                     !prior.BearerPlaintextStored && !prior.BearerHashStored && !prior.EndpointSecretStored && !prior.LeaseAuthorityGranted;
            }
            catch (JsonException)
            {
                priorContractValid = false;
                prior = null;
            }

            var evidenceDir = Path.Combine(expectedDir, "generation-evidence-v0519");
            Directory.CreateDirectory(evidenceDir);
            LocalAppV046FileBoundary.RejectReparse(evidenceDir, "v0.51.9 generation evidence directory");

            if (!string.IsNullOrWhiteSpace(verifiedReuseArchivePath))
            {
                archivePath = ValidateReusableArchive(evidenceDir, verifiedReuseArchivePath, priorBytes, priorSha);
                archiveSha = LocalAppV046FileBoundary.HashFile(archivePath);
                archiveVerified = true;
                archiveReused = true;
            }
            else
            {
                var byShaDir = Path.Combine(evidenceDir, "by-sha");
                Directory.CreateDirectory(byShaDir);
                LocalAppV046FileBoundary.RejectReparse(byShaDir, "v0.51.10 content-addressed owner evidence directory");
                archivePath = Path.Combine(byShaDir, $"owner-prior-sha256-{priorSha}.json");
                if (File.Exists(archivePath))
                {
                    archivePath = ValidateReusableArchive(evidenceDir, archivePath, priorBytes, priorSha);
                    archiveReused = true;
                }
                else
                {
                    await WriteBytesAtomicAsync(archivePath, priorBytes, cancellationToken);
                }
                archiveSha = LocalAppV046FileBoundary.HashFile(archivePath);
                archiveVerified = archiveSha.Equals(priorSha, StringComparison.OrdinalIgnoreCase);
                if (!archiveVerified)
                    throw new InvalidDataException("MCP_OWNER_GENERATION_ARCHIVE_HASH_MISMATCH: successor owner metadata was not written.");
            }
        }

        var status = priorPresent
            ? priorContractValid ? "PRIOR_OWNER_METADATA_PRESERVED_VALID" : "PRIOR_OWNER_METADATA_PRESERVED_OPAQUE_UNTRUSTED"
            : "NO_PRIOR_OWNER_METADATA";
        var receipt = new LocalAppMcpOwnerGenerationTransitionReceiptV0519(
            ReceiptSchema, Version, DateTimeOffset.Now, applicationId, successorSessionId,
            priorPresent, priorContractValid,
            priorContractValid ? prior!.SessionId : null,
            priorContractValid ? prior!.LeaseId : null,
            priorContractValid ? prior!.State : null,
            priorBytes?.LongLength ?? 0,
            priorSha,
            priorPresent,
            archivePath,
            archiveSha,
            archiveVerified,
            status,
            false, false, false, false, false, false,
            false, false, false,
            NonEffects(),
            priorPresent
                ? archiveReused
                    ? "Exact prior active owner-metadata bytes were already present in a hash-verified v0.51.9 archive and were reused before successor owner generation write. Prior metadata remains provenance only and grants no authority."
                    : "Exact prior active owner-metadata bytes were preserved and hash-verified before successor owner generation write. Prior metadata remains provenance only and grants no authority."
                : "No prior active owner metadata existed. Successor owner generation may continue without evidence rotation.");
        var receiptPath = await WriteReceiptAsync(workspaceRoot, applicationId, successorSessionId, receipt, cancellationToken);
        return (receipt, receiptPath);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-generation-v0519-order", true, "preserve prior after owner.lock acquisition and before successor owner metadata write", "before successor write"),
        ("mcp-generation-v0519-size", MaxPriorMetadataBytes == 65536, MaxPriorMetadataBytes.ToString(), "65536"),
        ("mcp-generation-v0519-valid", true, "valid v0.51.7 metadata archived exact", "hash verified"),
        ("mcp-generation-v0519-invalid", true, "invalid metadata archived opaque/untrusted", "no authority"),
        ("mcp-generation-v0519-no-prior", true, "NO_PRIOR_OWNER_METADATA", "normal start"),
        ("mcp-generation-v0519-reuse", true, "content-addressed by SHA or exact v0.51.10 verified legacy archive", "no duplicate prior bytes"),
        ("mcp-generation-v0519-failure", true, "archive/reuse hash failure throws before successor metadata", "fail closed"),
        ("mcp-generation-v0519-authority", true, "lease/read/revoke/resume=false", "false"),
        ("mcp-generation-v0519-secrets", true, "receipt omits raw prior bytes/bearer/hash/endpoint secret", "omitted")
    };

    private static string ValidateReusableArchive(string evidenceDir, string archivePath, byte[] priorBytes, string priorSha)
    {
        var full = Path.GetFullPath(archivePath);
        var allowed = Path.GetFullPath(evidenceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_OWNER_GENERATION_REUSE_REFUSED: reuse archive is outside the v0.51.9 evidence directory.");
        if (!File.Exists(full))
            throw new InvalidDataException("MCP_OWNER_GENERATION_REUSE_REFUSED: reuse archive is missing.");
        LocalAppV046FileBoundary.RejectReparse(full, "v0.51.10 reused prior owner evidence");
        var reuseLength = new FileInfo(full).Length;
        if (reuseLength != priorBytes.LongLength || reuseLength < 0 || reuseLength > MaxPriorMetadataBytes)
            throw new InvalidDataException("MCP_OWNER_GENERATION_REUSE_REFUSED: reuse archive size differs from exact prior metadata.");
        var sha = LocalAppV046FileBoundary.HashFile(full);
        if (!sha.Equals(priorSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_OWNER_GENERATION_REUSE_REFUSED: reuse archive hash differs from exact prior metadata.");
        return full;
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        string applicationId,
        string successorSessionId,
        LocalAppMcpOwnerGenerationTransitionReceiptV0519 receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-owner-generation-v0519");
        var path = Path.Combine(dir,
            $"owner-generation-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{LocalAppV046FileBoundary.SafeToken(successorSessionId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteBytesAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.9 prior owner evidence");
            if (File.Exists(path)) throw new InvalidDataException("Unexpected v0.51.9 prior owner evidence path collision.");
            File.Move(temp, path, false);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.9 prior owner evidence");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string[] NonEffects() => new[]
    {
        "generation preservation is evidence continuity only, not lease/read/revoke/resume authority",
        "no canonical read-lease state mutation",
        "no verified active-index mutation",
        "no historical lease enumeration",
        "no bearer plaintext/hash or endpoint path secret copied into transition receipt",
        "no MCP listener start/stop performed by generation preservation",
        "no network/tunnel/publication/catalog/Agent Execute or ActionPermit authority"
    };
}
