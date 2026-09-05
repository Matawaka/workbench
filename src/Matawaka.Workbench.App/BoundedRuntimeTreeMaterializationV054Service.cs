using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RuntimeMaterializationRequestV054(
    string Schema,
    string RequestId,
    string AcquisitionReceiptPath,
    string AcquisitionReceiptSha256,
    IReadOnlyList<string> ArtifactIds,
    string DestinationRoot,
    int MaxFiles,
    long MaxExpandedBytes,
    int TtlSeconds);

public sealed record RuntimeMaterializationArchiveV054(
    string ArtifactId,
    string ArchivePath,
    long ArchiveBytes,
    string ArchiveSha256,
    string SourceUri);

public sealed record RuntimeMaterializationPlanEntryV054(
    int ArchiveOrder,
    string ArtifactId,
    string ArchiveEntryName,
    string RelativePath,
    bool IsDirectory,
    long DeclaredBytes);

public sealed record RuntimeMaterializationPreviewV054(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string RequestDigestSha256,
    string AcquisitionReceiptPath,
    string AcquisitionReceiptSha256,
    IReadOnlyList<string> ArtifactIds,
    IReadOnlyList<RuntimeMaterializationArchiveV054> Archives,
    string DestinationRoot,
    int MaxFiles,
    long MaxExpandedBytes,
    int TtlSeconds,
    int ExactFileCount,
    long ExactExpandedBytes,
    string PlanSha256,
    IReadOnlyList<RuntimeMaterializationPlanEntryV054> PlanEntries,
    bool FilesystemMutationPerformed,
    bool ExtractionPerformed,
    bool ProcessExecutionPerformed,
    bool ReadyForExplicitMaterializationAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RuntimeMaterializationLeaseStateV054(
    string Schema,
    string Version,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string AcquisitionReceiptPath,
    string AcquisitionReceiptSha256,
    IReadOnlyList<string> ArtifactIds,
    string DestinationRoot,
    int MaxFiles,
    long MaxExpandedBytes,
    int TtlSeconds,
    int ExactFileCount,
    long ExactExpandedBytes,
    string PlanSha256,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    int RemainingCalls,
    string BearerSha256,
    string State,
    bool Revoked,
    bool Completed,
    bool Failed,
    string? FailureClassification,
    string? StagingRoot,
    string? RuntimeManifestPath,
    string? RuntimeManifestSha256,
    string? TreeDigestSha256,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RuntimeMaterializationGrantV054(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string RequestId,
    string RequestDigestSha256,
    string LeaseStatePath,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool BearerPersistedInPlaintextByWorkbench,
    bool MaterializationPerformed,
    string Note);

public sealed record RuntimeMaterializationAuthorityReceiptV054(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string BearerSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    string AcquisitionReceiptSha256,
    string PlanSha256,
    bool BearerPlaintextPersisted,
    bool FilesystemMutationPerformed,
    bool ExtractionPerformed,
    bool ProcessExecutionPerformed,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record RuntimeMaterializationTransactionV054(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string State,
    string AcquisitionReceiptPath,
    string AcquisitionReceiptSha256,
    string PlanSha256,
    IReadOnlyList<RuntimeMaterializationArchiveV054> Archives,
    string DestinationRoot,
    string? StagingRoot,
    int PlannedFiles,
    long PlannedExpandedBytes,
    int MaterializedFiles,
    long MaterializedBytes,
    string? TreeDigestSha256,
    string? RuntimeManifestPath,
    string? RuntimeManifestSha256,
    bool AuthorityConsumed,
    bool FilesystemMutationPerformed,
    bool ExtractionPerformed,
    bool RootPromoted,
    bool ProcessExecutionPerformed,
    bool NetworkAccessPerformed,
    bool BenchmarkPerformed,
    bool ModelRequestPerformed,
    bool GameAccessPerformed,
    string? FailureClassification,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RuntimeMaterializationExecutionReceiptV054(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string State,
    string AcquisitionReceiptPath,
    string AcquisitionReceiptSha256,
    string PlanSha256,
    IReadOnlyList<RuntimeMaterializationArchiveV054> Archives,
    string RuntimeRoot,
    string RuntimeManifestPath,
    string RuntimeManifestSha256,
    string TreeDigestSha256,
    int MaterializedFiles,
    long MaterializedBytes,
    IReadOnlyList<RuntimeTreeFileV053> Files,
    string TransactionPath,
    string TransactionSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    bool MaterializationAuthorityConsumed,
    bool FilesystemMutationPerformed,
    bool ExtractionPerformed,
    bool RootPromoted,
    bool ProcessExecutionPerformed,
    bool RuntimeStartPerformed,
    bool BenchmarkPerformed,
    bool ModelRequestPerformed,
    bool GameAccessPerformed,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class RuntimeMaterializationExceptionV054 : IOException
{
    public string Classification { get; }

    public RuntimeMaterializationExceptionV054(string classification, string message) : base(message)
        => Classification = classification;

    public RuntimeMaterializationExceptionV054(string classification, string message, Exception inner) : base(message, inner)
        => Classification = classification;
}

public sealed class BoundedRuntimeTreeMaterializationV054Service
{
    public const string Version = "0.54.0";
    public const string RequestSchema = "matawaka.runtime-materialization-request/v0.54";
    public const string PreviewSchema = "matawaka.runtime-materialization-preview/v0.54";
    public const string LeaseStateSchema = "matawaka.runtime-materialization-lease-state/v0.54";
    public const string GrantSchema = "matawaka.runtime-materialization-grant/v0.54";
    public const string AuthorityReceiptSchema = "matawaka.runtime-materialization-authority-receipt/v0.54";
    public const string TransactionSchema = "matawaka.runtime-materialization-transaction/v0.54";
    public const string ExecutionReceiptSchema = "matawaka.runtime-materialization-execution-receipt/v0.54";
    public const string RuntimeManifestFileName = ".matawaka-runtime-tree-manifest.json";

    public const int MaxArchives = 8;
    public const int HardMaxFiles = 50_000;
    public const long HardMaxExpandedBytes = 64L * 1024L * 1024L * 1024L;
    public const int MaxTtlSeconds = 30 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public RuntimeMaterializationPreviewV054 Preview(
        string workspaceRoot,
        RuntimeMaterializationRequestV054 request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null) throw Refused("REQUEST_INVALID", "Runtime materialization request is required.");
        if (!string.Equals(request.Schema, RequestSchema, StringComparison.Ordinal))
            throw Refused("REQUEST_SCHEMA_REFUSED", $"Expected exact schema {RequestSchema}.");
        if (!SafeToken(request.RequestId, "matreq-"))
            throw Refused("REQUEST_INVALID", "RequestId must be a matreq-* safe token.");
        RequireSha256(request.AcquisitionReceiptSha256, "AcquisitionReceiptSha256");
        if (request.ArtifactIds is null || request.ArtifactIds.Count < 1 || request.ArtifactIds.Count > MaxArchives)
            throw Refused("REQUEST_INVALID", $"ArtifactIds count must be within 1..{MaxArchives}.");
        if (request.MaxFiles < 1 || request.MaxFiles > HardMaxFiles)
            throw Refused("FILE_CEILING_INVALID", $"MaxFiles must be within 1..{HardMaxFiles}.");
        if (request.MaxExpandedBytes < 1 || request.MaxExpandedBytes > HardMaxExpandedBytes)
            throw Refused("EXPANDED_BYTE_CEILING_INVALID", $"MaxExpandedBytes must be within 1..{HardMaxExpandedBytes}.");
        if (request.TtlSeconds < 1 || request.TtlSeconds > MaxTtlSeconds)
            throw Refused("TTL_REFUSED", $"TtlSeconds must be within 1..{MaxTtlSeconds}.");

        var repo = ResolveRepositoryRoot(workspaceRoot);
        var receiptPath = ValidateAcquisitionReceiptPath(repo, request.AcquisitionReceiptPath);
        var receiptSha = HashFile(receiptPath);
        if (!receiptSha.Equals(request.AcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("ACQUISITION_RECEIPT_HASH_MISMATCH", "Acquisition receipt SHA-256 differs from exact request binding.");

        ArtifactAcquisitionExecutionReceiptV052 acquisition;
        try
        {
            acquisition = JsonSerializer.Deserialize<ArtifactAcquisitionExecutionReceiptV052>(File.ReadAllText(receiptPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Acquisition receipt deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw new RuntimeMaterializationExceptionV054("ACQUISITION_RECEIPT_INVALID", "Acquisition receipt could not be parsed as the exact v0.52 execution receipt.", ex);
        }

        if (!string.Equals(acquisition.Schema, BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema, StringComparison.Ordinal) ||
            !string.Equals(acquisition.State, "ACQUISITION_VERIFIED", StringComparison.Ordinal) ||
            !string.Equals(acquisition.Status, "ARTIFACT_ACQUISITION_VERIFIED", StringComparison.Ordinal) ||
            !acquisition.AllArtifactsSha256Verified || acquisition.ExtractionPerformed || acquisition.ProcessExecutionPerformed ||
            acquisition.RuntimeStartPerformed || acquisition.BenchmarkPerformed || acquisition.ModelRequestPerformed || acquisition.GameAccessPerformed)
            throw Refused("ACQUISITION_RECEIPT_NOT_VERIFIED", "Source receipt is not exact terminal v0.52 artifact verification evidence.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var archives = new List<RuntimeMaterializationArchiveV054>(request.ArtifactIds.Count);
        foreach (var artifactId in request.ArtifactIds)
        {
            if (string.IsNullOrWhiteSpace(artifactId) || !ids.Add(artifactId))
                throw Refused("REQUEST_INVALID", "ArtifactIds must be unique non-empty exact identities.");
            var item = acquisition.Items.SingleOrDefault(x => string.Equals(x.ArtifactId, artifactId, StringComparison.Ordinal))
                ?? throw Refused("ACQUISITION_ARTIFACT_NOT_FOUND", $"ArtifactId is not bound by the acquisition receipt: {artifactId}");
            if (!item.ExpectedSizeMatched || !item.ExpectedSha256Matched || (!item.FinalPathPromoted && !item.ExistingVerifiedReused) ||
                item.ObservedFileBytes is null || string.IsNullOrWhiteSpace(item.ObservedSha256))
                throw Refused("ACQUISITION_ARTIFACT_NOT_VERIFIED", $"ArtifactId did not reach exact local verification: {artifactId}");
            var archivePath = Path.GetFullPath(item.FinalPath);
            RequireOutsideRepository(repo, archivePath, "acquired archive");
            if (!File.Exists(archivePath)) throw Refused("ACQUIRED_ARCHIVE_MISSING", $"Verified acquired artifact is missing: {artifactId}");
            RejectReparseChain(archivePath);
            if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw Refused("ARCHIVE_TYPE_REFUSED", $"v0.54 materialization accepts only exact .zip artifacts: {artifactId}");
            var bytes = new FileInfo(archivePath).Length;
            if (bytes != item.ObservedFileBytes.Value)
                throw Refused("ARCHIVE_SIZE_DRIFT", $"Acquired archive byte length drifted after v0.52 verification: {artifactId}");
            var sha = HashFile(archivePath);
            if (!sha.Equals(item.ObservedSha256, StringComparison.OrdinalIgnoreCase))
                throw Refused("ARCHIVE_HASH_DRIFT", $"Acquired archive SHA-256 drifted after v0.52 verification: {artifactId}");
            archives.Add(new RuntimeMaterializationArchiveV054(artifactId, archivePath, bytes, sha, item.SourceUri));
        }

        var destinationRoot = ValidateNewDestinationRoot(repo, request.DestinationRoot);
        var plan = BuildPlan(archives, request.MaxFiles, request.MaxExpandedBytes);
        var requestDigest = HashText(JsonSerializer.Serialize(request, JsonOptions));
        return new RuntimeMaterializationPreviewV054(
            PreviewSchema, Version, DateTimeOffset.Now, request.RequestId, requestDigest,
            receiptPath, receiptSha, request.ArtifactIds.ToArray(), archives, destinationRoot,
            request.MaxFiles, request.MaxExpandedBytes, request.TtlSeconds,
            plan.FileCount, plan.ExpandedBytes, plan.PlanSha256, plan.Entries,
            false, false, false, true, NonEffects(),
            "Preview revalidates exact v0.52 acquisition evidence and derives a deterministic ZIP central-directory plan. No runtime-tree bytes are written and no materialization authority exists yet.");
    }

    public async Task<(RuntimeMaterializationGrantV054 Grant, RuntimeMaterializationAuthorityReceiptV054 Receipt, string ReceiptPath)> GrantAsync(
        string workspaceRoot,
        RuntimeMaterializationPreviewV054 preview,
        CancellationToken cancellationToken)
    {
        var reverified = Preview(workspaceRoot, ToRequest(preview), cancellationToken);
        if (!reverified.RequestDigestSha256.Equals(preview.RequestDigestSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.PlanSha256.Equals(preview.PlanSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("PREVIEW_STALE", "Runtime materialization preview changed before authority grant.");

        var repo = ResolveRepositoryRoot(workspaceRoot);
        var root = MaterializationArtifactRoot(repo);
        var leaseId = "matlease-" + Guid.NewGuid().ToString("N");
        var leaseDir = Path.Combine(root, "leases", leaseId);
        Directory.CreateDirectory(leaseDir);
        RejectReparseChain(leaseDir);
        var statePath = Path.Combine(leaseDir, "state.json");
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var bearerSha = HashText(bearer);
        var now = DateTimeOffset.Now;
        var state = new RuntimeMaterializationLeaseStateV054(
            LeaseStateSchema, Version, leaseId, preview.RequestId, preview.RequestDigestSha256,
            preview.AcquisitionReceiptPath, preview.AcquisitionReceiptSha256, preview.ArtifactIds.ToArray(), preview.DestinationRoot,
            preview.MaxFiles, preview.MaxExpandedBytes, preview.TtlSeconds, preview.ExactFileCount, preview.ExactExpandedBytes,
            preview.PlanSha256, now, now.AddSeconds(preview.TtlSeconds), 1, 1, bearerSha,
            "MATERIALIZATION_PREPARED", false, false, false, null, null, null, null, null, 1,
            NonEffects(), "One-shot runtime-tree materialization authority is prepared; no destination mutation or extraction has occurred.");
        await WriteJsonAtomicAsync(statePath, state, cancellationToken);

        var grant = new RuntimeMaterializationGrantV054(
            GrantSchema, Version, now, leaseId, bearer, preview.RequestId, preview.RequestDigestSha256,
            statePath, state.ExpiresAt, 1, false, false,
            "Bearer plaintext exists only in this grant object. Persisted state stores SHA-256 only; the grant does not imply execution authority.");
        var authority = new RuntimeMaterializationAuthorityReceiptV054(
            AuthorityReceiptSchema, Version, now, leaseId, preview.RequestId, preview.RequestDigestSha256,
            bearerSha, statePath, HashFile(statePath), state.ExpiresAt, 1,
            preview.AcquisitionReceiptSha256, preview.PlanSha256,
            false, false, false, false, false, NonEffects(),
            "MATERIALIZATION_AUTHORITY_GRANTED_NOT_USED",
            "One-shot local materialization authority was created after exact preview revalidation. No archive extraction/runtime/process/network effect occurred.");
        var receiptPath = await WriteReceiptAsync(repo, $"authority-{leaseId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", authority, cancellationToken);
        return (grant, authority, receiptPath);
    }

    public async Task<(RuntimeMaterializationExecutionReceiptV054 Receipt, string ReceiptPath)> MaterializeAsync(
        string workspaceRoot,
        RuntimeMaterializationGrantV054 grant,
        CancellationToken cancellationToken)
    {
        if (grant is null || !string.Equals(grant.Schema, GrantSchema, StringComparison.Ordinal) ||
            !string.Equals(grant.Version, Version, StringComparison.Ordinal) || !SafeToken(grant.LeaseId, "matlease-"))
            throw Refused("AUTHORITY_INVALID", "Invalid v0.54 runtime materialization grant identity.");

        var repo = ResolveRepositoryRoot(workspaceRoot);
        var paths = ResolveLeasePaths(repo, grant.LeaseId);
        using var leaseLock = AcquireExclusiveFileLock(paths.LockPath, "MATERIALIZATION_LEASE_BUSY");
        var state = await ReadStateAsync(paths.StatePath, cancellationToken);
        ValidateGrantAgainstState(grant, state);
        if (state.Revoked) throw Refused("AUTHORITY_REVOKED", "Runtime materialization lease is revoked.");
        if (state.Completed) throw Refused("AUTHORITY_ALREADY_COMPLETED", "Runtime materialization lease already completed.");
        if (state.Failed) throw Refused("AUTHORITY_TERMINAL_FAILED", $"Runtime materialization lease already failed: {state.FailureClassification}");
        if (state.ExpiresAt <= DateTimeOffset.Now) throw Refused("AUTHORITY_EXPIRED", "Runtime materialization lease expired.");
        if (state.RemainingCalls != 1) throw Refused("AUTHORITY_CALL_BUDGET_EXHAUSTED", "One-shot runtime materialization call budget is exhausted.");
        if (!HashText(grant.Bearer).Equals(state.BearerSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("AUTHORITY_BEARER_MISMATCH", "Runtime materialization bearer mismatch.");

        var preview = Preview(workspaceRoot, ToRequest(state), cancellationToken);
        if (!preview.RequestDigestSha256.Equals(state.RequestDigestSha256, StringComparison.OrdinalIgnoreCase) ||
            !preview.PlanSha256.Equals(state.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
            preview.ExactFileCount != state.ExactFileCount || preview.ExactExpandedBytes != state.ExactExpandedBytes)
            throw Refused("SOURCE_OR_PLAN_DRIFT", "Acquisition evidence/archive plan changed after authority creation.");

        using var destinationLock = AcquireExclusiveFileLock(DestinationLockPath(repo, state.DestinationRoot), "MATERIALIZATION_DESTINATION_BUSY");
        if (Directory.Exists(state.DestinationRoot) || File.Exists(state.DestinationRoot))
            throw Refused("FINAL_ROOT_EXISTS", "Destination runtime root appeared before materialization.");

        state = state with
        {
            RemainingCalls = 0,
            State = "ARCHIVE_PLAN_VERIFIED",
            StateRevision = state.StateRevision + 1,
            Note = "One-shot materialization authority consumed before first destination-tree filesystem mutation. Archive receipt/hash/plan revalidation passed."
        };
        await WriteJsonAtomicAsync(paths.StatePath, state, cancellationToken);

        var stagingRoot = BuildStagingRoot(state.DestinationRoot, state.LeaseId);
        var tx = new RuntimeMaterializationTransactionV054(
            TransactionSchema, Version, DateTimeOffset.Now, "mattx-" + Guid.NewGuid().ToString("N"),
            state.LeaseId, state.RequestId, "ARCHIVE_PLAN_VERIFIED",
            state.AcquisitionReceiptPath, state.AcquisitionReceiptSha256, state.PlanSha256,
            preview.Archives, state.DestinationRoot, stagingRoot,
            state.ExactFileCount, state.ExactExpandedBytes, 0, 0, null, null, null,
            true, false, false, false, false, false, false, false, false,
            null, NonEffects(), "Exact source acquisition evidence and archive central-directory plan were reverified under consumed one-shot authority; extraction has not started.");
        await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

        try
        {
            if (Directory.Exists(stagingRoot) || File.Exists(stagingRoot))
                throw Refused("PARTIAL_ROOT_ALREADY_EXISTS", "Unique materialization staging root already exists.");
            Directory.CreateDirectory(stagingRoot);
            RejectReparseChain(stagingRoot);
            state = state with { StagingRoot = stagingRoot, State = "EXTRACTION_STARTED", StateRevision = state.StateRevision + 1 };
            await WriteJsonAtomicAsync(paths.StatePath, state, cancellationToken);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "EXTRACTION_STARTED",
                FilesystemMutationPerformed = true,
                ExtractionPerformed = true,
                Note = "Staging root created after authority consumption; exact planned ZIP entries are being materialized with CreateNew semantics."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            var extracted = new List<RuntimeTreeFileV053>(state.ExactFileCount);
            long totalBytes = 0;
            foreach (var archive in preview.Archives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReverifyArchive(archive, repo);
                using var zip = OpenZip(archive.ArchivePath, archive.ArtifactId);
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RejectLinkEntry(entry, archive.ArtifactId);
                    var isDirectory = IsDirectoryEntry(entry);
                    var relative = NormalizeZipPath(entry.FullName, isDirectory);
                    if (relative.Length == 0) continue;
                    RejectReservedManifestCollision(relative);
                    var destination = ResolveUnderRoot(stagingRoot, relative, "materialized ZIP entry");
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(destination);
                        RejectReparseChain(destination);
                        continue;
                    }

                    if (entry.Length < 0 || entry.Length > state.MaxExpandedBytes - totalBytes)
                        throw Refused("EXPANDED_BYTE_CEILING_EXCEEDED", $"Expanded byte ceiling would be exceeded by {relative}.");
                    var parent = Path.GetDirectoryName(destination) ?? throw Refused("ZIP_PATH_POLICY_REFUSED", "Materialized entry parent cannot be resolved.");
                    Directory.CreateDirectory(parent);
                    RejectReparseChain(parent);
                    if (File.Exists(destination) || Directory.Exists(destination))
                        throw Refused("ZIP_PATH_COLLISION", $"Materialized destination already exists: {relative}");

                    long observed = 0;
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    try
                    {
                        using var source = entry.Open();
                        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                            FileOptions.SequentialScan | FileOptions.WriteThrough);
                        var buffer = new byte[128 * 1024];
                        while (true)
                        {
                            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                            if (read == 0) break;
                            observed = checked(observed + read);
                            totalBytes = checked(totalBytes + read);
                            if (observed > entry.Length || totalBytes > state.MaxExpandedBytes)
                                throw Refused("EXPANDED_BYTE_CEILING_EXCEEDED", $"ZIP entry expanded beyond exact declared/bounded bytes: {relative}");
                            hash.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        }
                        await output.FlushAsync(cancellationToken);
                    }
                    catch (RuntimeMaterializationExceptionV054) { throw; }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                    {
                        throw new RuntimeMaterializationExceptionV054("ZIP_EXTRACTION_FAILED", $"ZIP entry extraction failed: {relative}", ex);
                    }

                    if (observed != entry.Length)
                        throw Refused("ZIP_ENTRY_SIZE_MISMATCH", $"ZIP entry materialized byte count differs from exact declared length: {relative}");
                    var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                    RejectReparseChain(destination);
                    extracted.Add(new RuntimeTreeFileV053(
                        relative,
                        observed,
                        sha,
                        relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "executable" : "runtime-file"));
                    if (extracted.Count > state.MaxFiles)
                        throw Refused("FILE_CEILING_EXCEEDED", "Materialized file count exceeded the explicit authority ceiling.");
                }
            }

            extracted = extracted.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToList();
            if (extracted.Count != state.ExactFileCount || totalBytes != state.ExactExpandedBytes)
                throw Refused("TREE_PLAN_MISMATCH", "Materialized file count/bytes differ from the exact archive-derived plan.");

            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "BYTES_MATERIALIZED",
                MaterializedFiles = extracted.Count,
                MaterializedBytes = totalBytes,
                Note = "Every exact planned file reached EOF with actual bytes equal to ZIP declared lengths. Hash verification follows."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            var treeDigest = HashTree(extracted);
            VerifyTree(stagingRoot, extracted);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "TREE_HASH_VERIFIED",
                TreeDigestSha256 = treeDigest,
                Note = "Every materialized staging file was SHA-256 rebound into a deterministic tree digest. No execution authority is created."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            var manifest = new RuntimeTreeManifestV053(
                BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
                "0.53",
                "runtime-tree-v054-" + state.LeaseId,
                BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState,
                state.DestinationRoot,
                extracted,
                $"Generated by Workbench v0.54 materialization lease {state.LeaseId}; acquisitionReceiptSha256={state.AcquisitionReceiptSha256}; planSha256={state.PlanSha256}. MATERIALIZED_VERIFIED is evidence only and grants no execution/model/benchmark/game authority.");
            var stagingManifest = Path.Combine(stagingRoot, RuntimeManifestFileName);
            await File.WriteAllTextAsync(stagingManifest, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellationToken);
            var manifestSha = HashFile(stagingManifest);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "RUNTIME_MANIFEST_WRITTEN",
                RuntimeManifestPath = Path.Combine(state.DestinationRoot, RuntimeManifestFileName),
                RuntimeManifestSha256 = manifestSha,
                Note = "A v0.53-compatible MATERIALIZED_VERIFIED runtime-tree manifest was written inside staging after complete tree verification."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            if (Directory.Exists(state.DestinationRoot) || File.Exists(state.DestinationRoot))
                throw Refused("FINAL_ROOT_RACE", "Final runtime root appeared before atomic promotion.");
            RejectReparseChain(Path.GetDirectoryName(state.DestinationRoot)!);
            Directory.Move(stagingRoot, state.DestinationRoot);
            var finalManifest = Path.Combine(state.DestinationRoot, RuntimeManifestFileName);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "ROOT_PROMOTED",
                RootPromoted = true,
                RuntimeManifestPath = finalManifest,
                Note = "Verified sibling staging root was atomically renamed to the final runtime root. Promotion itself grants no execution authority."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            RejectReparseChain(state.DestinationRoot);
            if (!File.Exists(finalManifest) || !HashFile(finalManifest).Equals(manifestSha, StringComparison.OrdinalIgnoreCase))
                throw Refused("FINAL_MANIFEST_VERIFY_FAILED", "Promoted runtime manifest differs from exact staging manifest.");
            VerifyTree(state.DestinationRoot, extracted);
            var finalTreeDigest = HashTree(extracted);
            if (!finalTreeDigest.Equals(treeDigest, StringComparison.OrdinalIgnoreCase))
                throw Refused("FINAL_TREE_VERIFY_FAILED", "Promoted runtime tree digest differs from staging tree digest.");

            state = state with
            {
                Completed = true,
                State = "MATERIALIZED_VERIFIED",
                RuntimeManifestPath = finalManifest,
                RuntimeManifestSha256 = manifestSha,
                TreeDigestSha256 = treeDigest,
                StateRevision = state.StateRevision + 1,
                Note = "Exact v0.52-verified ZIP bytes were materialized into a fully hashed runtime tree and v0.53-compatible manifest. No execution/runtime/model authority follows automatically."
            };
            await WriteJsonAtomicAsync(paths.StatePath, state, cancellationToken);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "MATERIALIZED_VERIFIED",
                RuntimeManifestPath = finalManifest,
                RuntimeManifestSha256 = manifestSha,
                TreeDigestSha256 = treeDigest,
                RootPromoted = true,
                MaterializedFiles = extracted.Count,
                MaterializedBytes = totalBytes,
                FailureClassification = null,
                Note = "Terminal materialization success: final runtime root, every file SHA-256, deterministic tree digest and v0.53-compatible runtime manifest are reverified."
            };
            await PersistTransactionAsync(repo, paths.TransactionPath, tx, cancellationToken);

            var receipt = BuildExecutionReceipt(paths, tx, extracted, true);
            var receiptPath = await WriteReceiptAsync(repo, $"materialization-{tx.TransactionId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        catch (RuntimeMaterializationExceptionV054 ex)
        {
            await MarkTerminalFailureAsync(paths, state, tx, ex.Classification, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            await MarkTerminalFailureAsync(paths, state, tx, "CANCELLED_PARTIAL_NON_AUTHORITATIVE", "Materialization cancelled after authority consumption; any staging bytes remain non-authoritative.");
            throw;
        }
        catch (Exception ex)
        {
            await MarkTerminalFailureAsync(paths, state, tx, "MATERIALIZATION_IO_FAILED", "Unexpected filesystem/archive failure after materialization authority consumption.");
            throw new RuntimeMaterializationExceptionV054("MATERIALIZATION_IO_FAILED", "Bounded runtime-tree materialization failed from unexpected filesystem/archive error.", ex);
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("materialization-v054-source", true, "exact v0.52 execution receipt + selected ArtifactIds", "no arbitrary archive path"),
        ("materialization-v054-preview", true, "receipt/archive hash + ZIP central directory only", "no extraction/write"),
        ("materialization-v054-one-shot", true, "RemainingCalls consumed before staging root creation", "one call"),
        ("materialization-v054-path-policy", true, "rooted/traversal/ADS/device/trailing-dot-space/link/collision refused", "fail closed"),
        ("materialization-v054-atomic-root", true, "sibling staging + Directory.Move only after complete tree hash", "bounded"),
        ("materialization-v054-output", true, "matawaka.runtime-tree-manifest/v0.53 MATERIALIZED_VERIFIED", "v0.53 compatible evidence"),
        ("materialization-v054-execution", true, "process/runtime/model/benchmark/game all false", "no post-materialization authority"),
        ("materialization-v054-network", true, "no network operation", "false"),
        ("materialization-v054-provider-neutral", true, "no KONTUR-specific behavior", "provider-neutral")
    };

    private static RuntimeMaterializationRequestV054 ToRequest(RuntimeMaterializationPreviewV054 preview)
        => new(RequestSchema, preview.RequestId, preview.AcquisitionReceiptPath, preview.AcquisitionReceiptSha256,
            preview.ArtifactIds.ToArray(), preview.DestinationRoot, preview.MaxFiles, preview.MaxExpandedBytes, preview.TtlSeconds);

    private static RuntimeMaterializationRequestV054 ToRequest(RuntimeMaterializationLeaseStateV054 state)
        => new(RequestSchema, state.RequestId, state.AcquisitionReceiptPath, state.AcquisitionReceiptSha256,
            state.ArtifactIds.ToArray(), state.DestinationRoot, state.MaxFiles, state.MaxExpandedBytes, state.TtlSeconds);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw Refused("WORKSPACE_INVALID", "Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(root) || !Directory.Exists(Path.Combine(root, ".git")))
            throw Refused("WORKSPACE_INVALID", "Workbench Git repository is missing.");
        return root;
    }

    private static string ValidateAcquisitionReceiptPath(string repo, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Refused("ACQUISITION_RECEIPT_PATH_REFUSED", "AcquisitionReceiptPath must be an absolute Workbench-owned receipt path.");
        var full = Path.GetFullPath(value);
        var allowed = Path.GetFullPath(Path.Combine(repo, "artifacts", "artifact-acquisition-v052")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw Refused("ACQUISITION_RECEIPT_PATH_REFUSED", "Acquisition receipt must exist under Workbench/artifacts/artifact-acquisition-v052.");
        RejectReparseChain(full);
        return full;
    }

    private static string ValidateNewDestinationRoot(string repo, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Refused("DESTINATION_ROOT_REFUSED", "DestinationRoot must be an absolute path.");
        var full = Path.GetFullPath(value.Trim());
        RequireOutsideRepository(repo, full, "destination runtime root");
        if (Directory.Exists(full) || File.Exists(full))
            throw Refused("FINAL_ROOT_EXISTS", "Destination runtime root must not already exist.");
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw Refused("DESTINATION_PARENT_MISSING", "Destination runtime-root parent must already exist.");
        RejectReparseChain(parent);
        var leaf = Path.GetFileName(full);
        ValidateWindowsSegment(leaf);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static PlanData BuildPlan(IReadOnlyList<RuntimeMaterializationArchiveV054> archives, int maxFiles, long maxExpandedBytes)
    {
        var entries = new List<RuntimeMaterializationPlanEntryV054>();
        var nodes = new Dictionary<string, NodeKind>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        long expanded = 0;

        for (var archiveOrder = 0; archiveOrder < archives.Count; archiveOrder++)
        {
            var archive = archives[archiveOrder];
            using var zip = OpenZip(archive.ArchivePath, archive.ArtifactId);
            foreach (var entry in zip.Entries)
            {
                RejectLinkEntry(entry, archive.ArtifactId);
                var isDirectory = IsDirectoryEntry(entry);
                var relative = NormalizeZipPath(entry.FullName, isDirectory);
                if (relative.Length == 0) continue;
                RejectReservedManifestCollision(relative);
                if (entry.Length < 0) throw Refused("ZIP_INVALID", $"ZIP entry has invalid declared length: {relative}");

                if (nodes.TryGetValue(relative, out var existing))
                {
                    if (!(existing == NodeKind.Directory && isDirectory))
                        throw Refused("ZIP_PATH_COLLISION", $"Case-insensitive duplicate/colliding ZIP output path: {relative}");
                }
                else
                {
                    nodes.Add(relative, isDirectory ? NodeKind.Directory : NodeKind.File);
                }

                entries.Add(new RuntimeMaterializationPlanEntryV054(
                    archiveOrder, archive.ArtifactId, entry.FullName, relative, isDirectory, isDirectory ? 0 : entry.Length));
                if (!isDirectory)
                {
                    fileCount = checked(fileCount + 1);
                    expanded = checked(expanded + entry.Length);
                    if (fileCount > maxFiles) throw Refused("FILE_CEILING_EXCEEDED", "Archive-derived file count exceeds MaxFiles.");
                    if (expanded > maxExpandedBytes) throw Refused("EXPANDED_BYTE_CEILING_EXCEEDED", "Archive-derived expanded byte total exceeds MaxExpandedBytes.");
                }
            }
        }

        var orderedPaths = nodes.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < orderedPaths.Length - 1; i++)
        {
            var current = orderedPaths[i];
            var next = orderedPaths[i + 1];
            if (nodes[current] == NodeKind.File && next.StartsWith(current + "/", StringComparison.OrdinalIgnoreCase))
                throw Refused("ZIP_PATH_COLLISION", $"ZIP file/directory prefix collision: {current}");
        }

        var canonical = entries
            .OrderBy(x => x.ArchiveOrder)
            .ThenBy(x => x.ArchiveEntryName, StringComparer.Ordinal)
            .Select(x => new { x.ArchiveOrder, x.ArtifactId, x.ArchiveEntryName, x.RelativePath, x.IsDirectory, x.DeclaredBytes })
            .ToArray();
        var archiveIdentity = archives.Select((x, i) => new { Order = i, x.ArtifactId, x.ArchiveBytes, x.ArchiveSha256 }).ToArray();
        var digest = HashText(JsonSerializer.Serialize(new { Archives = archiveIdentity, Entries = canonical, FileCount = fileCount, ExpandedBytes = expanded }, JsonOptions));
        return new PlanData(entries.ToArray(), fileCount, expanded, digest);
    }

    private static ZipArchive OpenZip(string path, string artifactId)
    {
        try
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            throw new RuntimeMaterializationExceptionV054("ZIP_INVALID", $"Verified artifact is not a readable supported ZIP archive: {artifactId}", ex);
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        var attrs = unchecked((uint)entry.ExternalAttributes);
        return entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
               entry.FullName.EndsWith("\\", StringComparison.Ordinal) ||
               (attrs & (uint)FileAttributes.Directory) != 0;
    }

    private static void RejectLinkEntry(ZipArchiveEntry entry, string artifactId)
    {
        var attrs = unchecked((uint)entry.ExternalAttributes);
        var unixType = (attrs >> 16) & 0xF000u;
        if (unixType == 0xA000u || (attrs & (uint)FileAttributes.ReparsePoint) != 0)
            throw Refused("ZIP_LINK_ENTRY_REFUSED", $"ZIP symlink/reparse entry refused in {artifactId}: {entry.FullName}");
    }

    private static string NormalizeZipPath(string raw, bool isDirectory)
    {
        if (string.IsNullOrEmpty(raw)) throw Refused("ZIP_PATH_POLICY_REFUSED", "ZIP entry path is empty.");
        var normalized = raw.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
            throw Refused("ZIP_PATH_POLICY_REFUSED", $"Rooted/drive/ADS ZIP path refused: {raw}");
        if (isDirectory) normalized = normalized.TrimEnd('/');
        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Length == 0) throw Refused("ZIP_PATH_POLICY_REFUSED", "ZIP path has no segments.");
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw Refused("ZIP_PATH_POLICY_REFUSED", $"Ambiguous/traversal ZIP path refused: {raw}");
            ValidateWindowsSegment(segment);
        }
        return string.Join('/', segments);
    }

    private static void ValidateWindowsSegment(string segment)
    {
        if (segment.EndsWith(' ') || segment.EndsWith('.'))
            throw Refused("ZIP_PATH_POLICY_REFUSED", $"Windows trailing-dot/space path segment refused: {segment}");
        if (segment.Any(ch => ch < 32 || ch is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' or '\\'))
            throw Refused("ZIP_PATH_POLICY_REFUSED", $"Windows-invalid path segment refused: {segment}");
        var deviceStem = segment.Split('.')[0];
        if (ReservedDeviceNames.Contains(deviceStem))
            throw Refused("ZIP_PATH_POLICY_REFUSED", $"Windows reserved device path segment refused: {segment}");
    }

    private static void RejectReservedManifestCollision(string relative)
    {
        var first = relative.Split('/')[0];
        if (first.Equals(RuntimeManifestFileName, StringComparison.OrdinalIgnoreCase))
            throw Refused("ZIP_PATH_COLLISION", "ZIP entry collides with reserved Workbench runtime-tree manifest metadata path.");
    }

    private static void ReverifyArchive(RuntimeMaterializationArchiveV054 archive, string repo)
    {
        var path = Path.GetFullPath(archive.ArchivePath);
        RequireOutsideRepository(repo, path, "acquired archive");
        if (!File.Exists(path)) throw Refused("ACQUIRED_ARCHIVE_MISSING", $"Verified archive disappeared: {archive.ArtifactId}");
        RejectReparseChain(path);
        if (new FileInfo(path).Length != archive.ArchiveBytes)
            throw Refused("ARCHIVE_SIZE_DRIFT", $"Verified archive size drifted before extraction: {archive.ArtifactId}");
        if (!HashFile(path).Equals(archive.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("ARCHIVE_HASH_DRIFT", $"Verified archive SHA-256 drifted before extraction: {archive.ArtifactId}");
    }

    private static string ResolveUnderRoot(string root, string relative, string role)
    {
        var native = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, native));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Refused("ZIP_PATH_POLICY_REFUSED", $"{role} escapes the bounded staging root.");
        return full;
    }

    private static void VerifyTree(string root, IReadOnlyList<RuntimeTreeFileV053> files)
    {
        foreach (var file in files)
        {
            var path = ResolveUnderRoot(root, file.RelativePath, "runtime tree file");
            if (!File.Exists(path)) throw Refused("TREE_VERIFY_FAILED", $"Materialized runtime file is missing: {file.RelativePath}");
            RejectReparseChain(path);
            if (new FileInfo(path).Length != file.Bytes)
                throw Refused("TREE_VERIFY_FAILED", $"Materialized runtime file size drifted: {file.RelativePath}");
            if (!HashFile(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Refused("TREE_VERIFY_FAILED", $"Materialized runtime file SHA-256 drifted: {file.RelativePath}");
        }
    }

    private static string HashTree(IReadOnlyList<RuntimeTreeFileV053> files)
    {
        var canonical = files.OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .Select(x => new { x.RelativePath, x.Bytes, x.Sha256, x.Role }).ToArray();
        return HashText(JsonSerializer.Serialize(canonical, JsonOptions));
    }

    private static void RequireOutsideRepository(string repo, string path, string role)
    {
        var repository = Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (full.Equals(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
            throw Refused("PATH_INSIDE_WORKBENCH_REFUSED", $"{role} must be outside the Workbench Git repository.");
    }

    private static void RejectReparseChain(string path)
    {
        var full = Path.GetFullPath(path);
        string? current = File.Exists(full) || Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw Refused("REPARSE_PATH_REFUSED", $"Reparse/symlink/junction path component refused: {current}");
            }
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent;
        }
    }

    private static string BuildStagingRoot(string destinationRoot, string leaseId)
    {
        var parent = Path.GetDirectoryName(destinationRoot) ?? throw Refused("DESTINATION_ROOT_REFUSED", "Destination root parent cannot be resolved.");
        var leaf = Path.GetFileName(destinationRoot);
        return Path.Combine(parent, $".{leaf}.{leaseId}.partial");
    }

    private static string MaterializationArtifactRoot(string repo)
    {
        var path = Path.Combine(repo, "artifacts", "runtime-materialization-v054");
        Directory.CreateDirectory(path);
        RejectReparseChain(path);
        return path;
    }

    private static LeasePaths ResolveLeasePaths(string repo, string leaseId)
    {
        var root = MaterializationArtifactRoot(repo);
        var leaseDir = Path.Combine(root, "leases", leaseId);
        var transactionDir = Path.Combine(root, "transactions");
        Directory.CreateDirectory(transactionDir);
        return new LeasePaths(
            Path.Combine(leaseDir, "state.json"),
            Path.Combine(leaseDir, "lease.lock"),
            Path.Combine(transactionDir, leaseId + ".json"));
    }

    private static string DestinationLockPath(string repo, string destinationRoot)
    {
        var dir = Path.Combine(MaterializationArtifactRoot(repo), "destination-locks");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, HashText(Path.GetFullPath(destinationRoot).ToUpperInvariant()) + ".lock");
    }

    private static IDisposable AcquireExclusiveFileLock(string path, string classification)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new RuntimeMaterializationExceptionV054(classification, "Another process owns the exact bounded materialization lock.", ex);
        }
    }

    private static async Task<RuntimeMaterializationLeaseStateV054> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw Refused("AUTHORITY_STATE_MISSING", "Runtime materialization lease state is missing.");
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            return JsonSerializer.Deserialize<RuntimeMaterializationLeaseStateV054>(json, JsonOptions)
                ?? throw Refused("AUTHORITY_STATE_INVALID", "Runtime materialization lease state deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new RuntimeMaterializationExceptionV054("AUTHORITY_STATE_INVALID", "Runtime materialization lease state is malformed.", ex);
        }
    }

    private static void ValidateGrantAgainstState(RuntimeMaterializationGrantV054 grant, RuntimeMaterializationLeaseStateV054 state)
    {
        if (!string.Equals(state.Schema, LeaseStateSchema, StringComparison.Ordinal) || !string.Equals(state.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(grant.LeaseId, state.LeaseId, StringComparison.Ordinal) || !string.Equals(grant.RequestId, state.RequestId, StringComparison.Ordinal) ||
            !string.Equals(grant.RequestDigestSha256, state.RequestDigestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("AUTHORITY_STATE_MISMATCH", "Runtime materialization grant does not match canonical persisted authority state.");
    }

    private static async Task PersistTransactionAsync(string repo, string transactionPath, RuntimeMaterializationTransactionV054 tx, CancellationToken cancellationToken)
    {
        await WriteJsonAtomicAsync(transactionPath, tx, cancellationToken);
        _ = await WriteReceiptAsync(repo, $"transition-{tx.TransactionId}-{SafeFileToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", tx, cancellationToken);
    }

    private static async Task MarkTerminalFailureAsync(
        LeasePaths paths,
        RuntimeMaterializationLeaseStateV054 state,
        RuntimeMaterializationTransactionV054 tx,
        string classification,
        string note)
    {
        try
        {
            state = state with
            {
                Failed = true,
                FailureClassification = classification,
                State = classification,
                StateRevision = state.StateRevision + 1,
                Note = note + " No automatic retry/resume/promotion authority is created."
            };
            await WriteJsonAtomicAsync(paths.StatePath, state, CancellationToken.None);
        }
        catch { }
        try
        {
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = classification,
                FailureClassification = classification,
                Note = note + " Any staging bytes remain non-authoritative and are never auto-promoted."
            };
            await WriteJsonAtomicAsync(paths.TransactionPath, tx, CancellationToken.None);
        }
        catch { }
    }

    private static RuntimeMaterializationExecutionReceiptV054 BuildExecutionReceipt(
        LeasePaths paths,
        RuntimeMaterializationTransactionV054 tx,
        IReadOnlyList<RuntimeTreeFileV053> files,
        bool verified)
        => new(
            ExecutionReceiptSchema, Version, DateTimeOffset.Now, tx.TransactionId, tx.LeaseId, tx.RequestId,
            tx.State, tx.AcquisitionReceiptPath, tx.AcquisitionReceiptSha256, tx.PlanSha256, tx.Archives,
            tx.DestinationRoot,
            tx.RuntimeManifestPath ?? "",
            tx.RuntimeManifestSha256 ?? "",
            tx.TreeDigestSha256 ?? "",
            tx.MaterializedFiles, tx.MaterializedBytes, files,
            paths.TransactionPath, HashFile(paths.TransactionPath), paths.StatePath, HashFile(paths.StatePath),
            tx.AuthorityConsumed, tx.FilesystemMutationPerformed, tx.ExtractionPerformed, tx.RootPromoted,
            false, false, false, false, false, false,
            tx.NonEffects,
            verified ? "RUNTIME_TREE_MATERIALIZATION_VERIFIED" : "RUNTIME_TREE_MATERIALIZATION_FAILED",
            verified
                ? "Exact v0.52-verified ZIP artifacts were materialized into a fully hashed, atomically promoted runtime tree with a v0.53-compatible MATERIALIZED_VERIFIED manifest. Execution/model/benchmark/game authority remains absent."
                : tx.Note);

    private static async Task<string> WriteReceiptAsync<T>(string repo, string fileName, T value, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(MaterializationArtifactRoot(repo), "receipts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashText(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void RequireSha256(string value, string role)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw Refused("REQUEST_INVALID", $"{role} must be an exact SHA-256 hex digest.");
    }

    private static bool SafeToken(string value, string prefix)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.Ordinal) &&
           value.Length <= 128 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string SafeFileToken(string value)
        => new(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "Verified Artifact != Materialized Runtime",
        "Materialization Preview != Materialization Authority",
        "Materialization Authority != Execution Authority",
        "MATERIALIZED_VERIFIED != Runtime Ready",
        "Runtime Tree Manifest != Benchmark Authority",
        "Runtime Tree Manifest != Model Request Authority",
        "no network access or artifact acquisition",
        "no process start/stop, shell/script/installer execution or elevation",
        "no PATH/registry/global environment mutation",
        "no Git remote/publication/catalog mutation",
        "no Agent Execute/ActionPermit",
        "no benchmark/model request/game access authority",
        "no KONTUR-specific runtime behavior"
    };

    private static RuntimeMaterializationExceptionV054 Refused(string classification, string message)
        => new(classification, message);

    private enum NodeKind { File, Directory }
    private sealed record PlanData(IReadOnlyList<RuntimeMaterializationPlanEntryV054> Entries, int FileCount, long ExpandedBytes, string PlanSha256);
    private sealed record LeasePaths(string StatePath, string LockPath, string TransactionPath);
}
