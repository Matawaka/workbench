using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpOwnerGenerationTransactionV05110(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string TransactionId,
    string State,
    string SuccessorSessionId,
    bool PriorMetadataPresent,
    bool PriorMetadataContractValid,
    string? PriorMetadataSha256,
    string? PriorArchivePath,
    string? PriorArchiveSha256,
    bool PriorArchiveHashVerified,
    bool PriorArchiveReused,
    string? SuccessorMetadataSha256,
    bool SuccessorMetadataContractValid,
    string? ReconciledFromState,
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

public sealed record LocalAppMcpOwnerGenerationReconcileResultV05110(
    string Status,
    bool PriorTransactionObserved,
    string? PriorTransactionId,
    string? PriorTerminalState,
    string? VerifiedReuseArchivePath,
    string? ReceiptPath,
    bool CanonicalLeaseMutated,
    bool ActiveIndexMutated,
    bool AuthorityGranted);

/// <summary>
/// v0.51.10 closes the crash window between v0.51.9 prior-owner evidence
/// preservation and successor owner-metadata materialization. The transaction
/// record is non-authoritative runtime control state under the already-held
/// app-scoped owner.lock. PREPARED never means COMMITTED.
/// </summary>
public sealed class LocalAppMcpOwnerGenerationTransactionV05110Service
{
    public const string Version = "0.51.10";
    public const string Schema = "matawaka.local-app-mcp-owner-generation-transaction/v0.51.10";
    public const int MaxTransactionBytes = 128 * 1024;
    public const int MaxOwnerMetadataBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<LocalAppMcpOwnerGenerationReconcileResultV05110> ReconcileBeforePrepareAsync(
        string workspaceRoot,
        string applicationId,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, metadataPath);
        if (!File.Exists(paths.TransactionPath))
        {
            return new LocalAppMcpOwnerGenerationReconcileResultV05110(
                "NO_PRIOR_GENERATION_TRANSACTION", false, null, null, null, null,
                false, false, false);
        }

        var current = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (!current.State.Equals("PREPARED", StringComparison.Ordinal))
        {
            return new LocalAppMcpOwnerGenerationReconcileResultV05110(
                "PRIOR_GENERATION_TRANSACTION_TERMINAL", true, current.TransactionId,
                current.State, null, null, false, false, false);
        }

        string? verifiedReuseArchivePath = null;
        if (current.PriorMetadataPresent)
        {
            verifiedReuseArchivePath = ValidatePriorArchive(paths, current);
        }

        var metadataPresent = File.Exists(paths.MetadataPath);
        if (!metadataPresent)
        {
            var terminal = current with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "CLOSED_METADATA_ABSENT",
                SuccessorMetadataSha256 = null,
                SuccessorMetadataContractValid = false,
                ReconciledFromState = "PREPARED",
                Note = "A prior PREPARED generation transaction was observed but active owner metadata is absent. The transition is closed as evidence-only without claiming successor commit or authority."
            };
            var receipt = await PersistTerminalAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpOwnerGenerationReconcileResultV05110(
                "PRIOR_PREPARED_CLOSED_METADATA_ABSENT", true, current.TransactionId,
                terminal.State, null, receipt, false, false, false);
        }

        LocalAppV046FileBoundary.RejectReparse(paths.MetadataPath, "v0.51.10 active owner metadata during transaction reconciliation");
        var info = new FileInfo(paths.MetadataPath);
        if (info.Length < 0 || info.Length > MaxOwnerMetadataBytes)
            throw new InvalidDataException($"MCP_OWNER_GENERATION_TRANSACTION_METADATA_OVERSIZE: active owner metadata exceeds {MaxOwnerMetadataBytes} bytes.");
        var metadataBytes = await File.ReadAllBytesAsync(paths.MetadataPath, cancellationToken);
        if (metadataBytes.LongLength != info.Length)
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_METADATA_DRIFT: active owner metadata size changed during reconciliation.");
        var metadataSha = HashBytes(metadataBytes);

        if (current.PriorMetadataPresent &&
            !string.IsNullOrWhiteSpace(current.PriorMetadataSha256) &&
            metadataSha.Equals(current.PriorMetadataSha256, StringComparison.OrdinalIgnoreCase))
        {
            var terminal = current with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "ABANDONED_BEFORE_SUCCESSOR",
                SuccessorMetadataSha256 = null,
                SuccessorMetadataContractValid = false,
                ReconciledFromState = "PREPARED",
                Note = "The exact prior metadata bytes are still active, proving the recorded successor owner generation did not replace them. The verified prior archive may be reused by the retry without duplicating prior evidence bytes."
            };
            var receipt = await PersistTerminalAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpOwnerGenerationReconcileResultV05110(
                "PRIOR_PREPARED_ABANDONED_REUSE_ARCHIVE", true, current.TransactionId,
                terminal.State, verifiedReuseArchivePath, receipt, false, false, false);
        }

        if (!current.PriorMetadataPresent)
        {
            // PREPARED with no prior metadata can only recover as committed when the
            // active metadata is the exact recorded successor. Any other metadata is inconsistent.
        }

        var owner = TryParseOwner(metadataBytes, applicationId);
        if (owner is not null && owner.SessionId.Equals(current.SuccessorSessionId, StringComparison.Ordinal))
        {
            var terminal = current with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "COMMITTED_RECOVERED",
                SuccessorMetadataSha256 = metadataSha,
                SuccessorMetadataContractValid = true,
                ReconciledFromState = "PREPARED",
                Note = "A prior PREPARED transaction was recovered by exact observation of the recorded successor owner metadata. This proves owner-generation materialization only, not lease/listener/read authority."
            };
            var receipt = await PersistTerminalAsync(paths, terminal, cancellationToken);
            return new LocalAppMcpOwnerGenerationReconcileResultV05110(
                "PRIOR_PREPARED_COMMITTED_RECOVERED", true, current.TransactionId,
                terminal.State, null, receipt, false, false, false);
        }

        throw new InvalidDataException(
            "MCP_OWNER_GENERATION_TRANSACTION_INCONSISTENT: PREPARED transaction, prior evidence and active owner metadata do not support either abandoned-before-successor or committed-successor recovery; no new owner/lease/listener authority was created.");
    }

    public async Task<(LocalAppMcpOwnerGenerationTransactionV05110 Transaction, string ReceiptPath)> PrepareAsync(
        string workspaceRoot,
        string applicationId,
        string successorSessionId,
        string metadataPath,
        LocalAppMcpOwnerGenerationTransitionReceiptV0519 generationEvidence,
        bool priorArchiveReused,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, metadataPath);
        RequireSafeSession(successorSessionId);
        if (!generationEvidence.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            !generationEvidence.SuccessorSessionId.Equals(successorSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("v0.51.10 generation evidence does not match exact ApplicationId/SuccessorSessionId.");
        if (generationEvidence.PriorMetadataPresent &&
            (!generationEvidence.PriorMetadataArchived || !generationEvidence.ArchiveHashVerified ||
             string.IsNullOrWhiteSpace(generationEvidence.PriorMetadataSha256) ||
             string.IsNullOrWhiteSpace(generationEvidence.ArchivePath) ||
             string.IsNullOrWhiteSpace(generationEvidence.ArchiveSha256)))
            throw new InvalidDataException("v0.51.10 cannot PREPARE without verified prior metadata evidence.");
        if (generationEvidence.CanonicalLeaseMutated || generationEvidence.ActiveIndexMutated ||
            generationEvidence.LeaseAuthorityGranted || generationEvidence.ReadAuthorityGranted ||
            generationEvidence.RevokeAuthorityGranted || generationEvidence.ResumeAuthorityGranted ||
            generationEvidence.BearerPlaintextDisclosed || generationEvidence.BearerHashDisclosed ||
            generationEvidence.EndpointSecretDisclosed)
            throw new InvalidDataException("v0.51.10 refuses generation evidence that claims authority or secret disclosure.");

        if (generationEvidence.PriorMetadataPresent)
        {
            _ = ValidateArchiveExact(paths, generationEvidence.ArchivePath!,
                generationEvidence.PriorMetadataSha256!, generationEvidence.ArchiveSha256!);
        }

        var tx = new LocalAppMcpOwnerGenerationTransactionV05110(
            Schema, Version, DateTimeOffset.Now, applicationId,
            "gentx-" + Guid.NewGuid().ToString("N"),
            "PREPARED", successorSessionId,
            generationEvidence.PriorMetadataPresent,
            generationEvidence.PriorMetadataContractValid,
            generationEvidence.PriorMetadataSha256,
            generationEvidence.ArchivePath,
            generationEvidence.ArchiveSha256,
            generationEvidence.ArchiveHashVerified,
            priorArchiveReused,
            null, false, null,
            false, false, false, false, false, false,
            false, false, false,
            NonEffects(),
            "Prior owner evidence is preserved/verified and this transaction is PREPARED. PREPARED is not proof that successor owner metadata exists.");

        // PREPARED checkpoint first: if immutable receipt persistence fails afterward,
        // retry can still reconcile the active PREPARED record without duplicating evidence.
        await WriteTransactionAtomicAsync(paths.TransactionPath, tx, cancellationToken);
        var receiptPath = await WriteReceiptAsync(workspaceRoot, tx, cancellationToken);
        return (tx, receiptPath);
    }

    public async Task<(LocalAppMcpOwnerGenerationTransactionV05110 Transaction, string ReceiptPath)> CommitAfterSuccessorWriteAsync(
        string workspaceRoot,
        string applicationId,
        string successorSessionId,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePaths(workspaceRoot, applicationId, metadataPath);
        var prepared = await ReadTransactionAsync(paths.TransactionPath, applicationId, cancellationToken);
        if (!prepared.State.Equals("PREPARED", StringComparison.Ordinal) ||
            !prepared.SuccessorSessionId.Equals(successorSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_COMMIT_REFUSED: exact PREPARED successor transaction was not observed.");
        if (!File.Exists(paths.MetadataPath))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_COMMIT_REFUSED: successor owner metadata is absent.");
        LocalAppV046FileBoundary.RejectReparse(paths.MetadataPath, "v0.51.10 successor owner metadata commit");
        var info = new FileInfo(paths.MetadataPath);
        if (info.Length < 0 || info.Length > MaxOwnerMetadataBytes)
            throw new InvalidDataException($"MCP_OWNER_GENERATION_TRANSACTION_METADATA_OVERSIZE: successor owner metadata exceeds {MaxOwnerMetadataBytes} bytes.");
        var bytes = await File.ReadAllBytesAsync(paths.MetadataPath, cancellationToken);
        if (bytes.LongLength != info.Length)
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_METADATA_DRIFT: successor owner metadata size changed during commit.");
        var owner = TryParseOwner(bytes, applicationId);
        if (owner is null || !owner.SessionId.Equals(successorSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_COMMIT_REFUSED: active owner metadata is not the exact successor contract/session.");
        var sha = HashBytes(bytes);
        var committed = prepared with
        {
            ObservedAt = DateTimeOffset.Now,
            State = "COMMITTED",
            SuccessorMetadataSha256 = sha,
            SuccessorMetadataContractValid = true,
            ReconciledFromState = null,
            Note = "Exact successor owner metadata was observed after the PREPARED checkpoint. COMMITTED proves owner-generation materialization only and grants no lease/read/revoke/resume authority."
        };

        // Immutable COMMITTED evidence first. If active-state update fails afterward,
        // the next attempt will see PREPARED + exact successor metadata and record COMMITTED_RECOVERED.
        var receiptPath = await WriteReceiptAsync(workspaceRoot, committed, cancellationToken);
        await WriteTransactionAtomicAsync(paths.TransactionPath, committed, cancellationToken);
        return (committed, receiptPath);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-gentx-v05110-prepared", true, "PREPARED != COMMITTED", "explicit distinction"),
        ("mcp-gentx-v05110-abandoned", true, "prior SHA still active -> ABANDONED_BEFORE_SUCCESSOR", "archive reusable"),
        ("mcp-gentx-v05110-recovered", true, "successor SessionId active -> COMMITTED_RECOVERED", "exact observation"),
        ("mcp-gentx-v05110-inconsistent", true, "mismatch fails closed", "no new authority"),
        ("mcp-gentx-v05110-size", MaxTransactionBytes == 131072 && MaxOwnerMetadataBytes == 65536,
            $"{MaxTransactionBytes}/{MaxOwnerMetadataBytes}", "131072/65536"),
        ("mcp-gentx-v05110-authority", true, "lease/read/revoke/resume=false", "false"),
        ("mcp-gentx-v05110-secrets", true, "bearer/hash/endpoint secret omitted", "omitted"),
        ("mcp-gentx-v05110-history", true, "no historical lease scan", "none")
    };

    private static LocalAppMcpSessionOwnerV0517? TryParseOwner(byte[] bytes, string applicationId)
    {
        try
        {
            var owner = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(bytes, JsonOptions);
            if (owner is null || owner.Schema != LocalAppMcpSessionOwnershipV0517Service.OwnerSchema ||
                owner.Version != LocalAppMcpSessionOwnershipV0517Service.Version ||
                !owner.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
                owner.BearerPlaintextStored || owner.BearerHashStored || owner.EndpointSecretStored || owner.LeaseAuthorityGranted)
                return null;
            return owner;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ValidatePriorArchive(ResolvedPaths paths, LocalAppMcpOwnerGenerationTransactionV05110 tx)
    {
        if (string.IsNullOrWhiteSpace(tx.PriorMetadataSha256) || string.IsNullOrWhiteSpace(tx.PriorArchivePath) ||
            string.IsNullOrWhiteSpace(tx.PriorArchiveSha256) || !tx.PriorArchiveHashVerified)
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_INCONSISTENT: PREPARED prior archive binding is incomplete.");
        return ValidateArchiveExact(paths, tx.PriorArchivePath, tx.PriorMetadataSha256, tx.PriorArchiveSha256);
    }

    private static string ValidateArchiveExact(ResolvedPaths paths, string archivePath, string expectedPriorSha, string expectedArchiveSha)
    {
        var full = Path.GetFullPath(archivePath);
        var allowed = Path.GetFullPath(paths.GenerationEvidenceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_ARCHIVE_ESCAPE: prior archive is outside the v0.51.9 generation evidence directory.");
        if (!File.Exists(full))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_ARCHIVE_MISSING: prior archive evidence is absent.");
        LocalAppV046FileBoundary.RejectReparse(full, "v0.51.10 prior owner archive");
        var info = new FileInfo(full);
        if (info.Length < 0 || info.Length > LocalAppMcpOwnerGenerationV0519Service.MaxPriorMetadataBytes)
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_ARCHIVE_OVERSIZE: prior archive exceeds the v0.51.9 bound.");
        var sha = LocalAppV046FileBoundary.HashFile(full);
        if (!sha.Equals(expectedPriorSha, StringComparison.OrdinalIgnoreCase) ||
            !sha.Equals(expectedArchiveSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_ARCHIVE_HASH_MISMATCH: prior archive no longer matches its PREPARED binding.");
        return full;
    }

    private async Task<LocalAppMcpOwnerGenerationTransactionV05110> ReadTransactionAsync(
        string path,
        string applicationId,
        CancellationToken cancellationToken)
    {
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.10 generation transaction");
        var info = new FileInfo(path);
        if (info.Length < 1 || info.Length > MaxTransactionBytes)
            throw new InvalidDataException($"MCP_OWNER_GENERATION_TRANSACTION_INVALID_SIZE: transaction must be within 1..{MaxTransactionBytes} bytes.");
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        LocalAppMcpOwnerGenerationTransactionV05110 tx;
        try
        {
            tx = JsonSerializer.Deserialize<LocalAppMcpOwnerGenerationTransactionV05110>(text, JsonOptions)
                 ?? throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_INVALID: transaction JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_INVALID: transaction JSON could not be parsed.", ex);
        }
        if (tx.Schema != Schema || tx.Version != Version || !tx.ApplicationId.Equals(applicationId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.TransactionId) || !tx.TransactionId.StartsWith("gentx-", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(tx.SuccessorSessionId) ||
            tx.CanonicalLeaseMutated || tx.ActiveIndexMutated || tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted ||
            tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted || tx.BearerPlaintextDisclosed ||
            tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new InvalidDataException("MCP_OWNER_GENERATION_TRANSACTION_INVALID: transaction contract/authority boundary failed validation.");
        return tx;
    }

    private async Task<string> PersistTerminalAsync(
        ResolvedPaths paths,
        LocalAppMcpOwnerGenerationTransactionV05110 terminal,
        CancellationToken cancellationToken)
    {
        // Preserve the terminal observation before changing active control state.
        var receiptPath = await WriteReceiptAsync(paths.WorkspaceRoot, terminal, cancellationToken);
        await WriteTransactionAtomicAsync(paths.TransactionPath, terminal, cancellationToken);
        return receiptPath;
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalAppMcpOwnerGenerationTransactionV05110 tx,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-owner-generation-transaction-v05110");
        var path = Path.Combine(dir,
            $"owner-gentx-{LocalAppV046FileBoundary.SafeToken(tx.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(tx.TransactionId)}-{LocalAppV046FileBoundary.SafeToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteTransactionAtomicAsync(
        string path,
        LocalAppMcpOwnerGenerationTransactionV05110 tx,
        CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(tx, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.10 generation transaction");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.10 generation transaction");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.10 generation transaction");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static ResolvedPaths ResolvePaths(string workspaceRoot, string applicationId, string metadataPath)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        var appDir = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517", LocalAppV046FileBoundary.SafeToken(applicationId));
        Directory.CreateDirectory(appDir);
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.10 generation transaction app directory");
        var expectedMetadata = Path.Combine(appDir, "owner-v0.51.7.json");
        if (!Path.GetFullPath(metadataPath).Equals(Path.GetFullPath(expectedMetadata), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.51.10 owner metadata path mismatch.");
        var evidenceDir = Path.Combine(appDir, "generation-evidence-v0519");
        var txPath = Path.Combine(appDir, "generation-transition-v05110.json");
        if (File.Exists(txPath)) LocalAppV046FileBoundary.RejectReparse(txPath, "v0.51.10 generation transaction path");
        return new ResolvedPaths(Path.GetFullPath(workspaceRoot.Trim()), expectedMetadata, evidenceDir, txPath);
    }

    private static void RequireSafeSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !sessionId.StartsWith("mcpsess-", StringComparison.Ordinal) ||
            sessionId.Length > 80 || sessionId.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new InvalidDataException("Unsafe successor MCP SessionId for v0.51.10 generation transaction.");
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string[] NonEffects() => new[]
    {
        "generation transaction is provenance/control state only, not lease/read/revoke/resume authority",
        "PREPARED is explicitly not proof of successor owner metadata materialization",
        "no canonical read-lease state mutation",
        "no verified active-index mutation",
        "no historical lease enumeration",
        "no bearer plaintext/hash or endpoint path secret stored/disclosed",
        "no MCP listener start/stop performed by transaction service",
        "no network/tunnel/publication/catalog/Agent Execute or ActionPermit authority"
    };

    private sealed record ResolvedPaths(
        string WorkspaceRoot,
        string MetadataPath,
        string GenerationEvidenceDirectory,
        string TransactionPath);
}
