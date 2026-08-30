using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryEvidenceTransportFile(
    string RelativePath,
    string Sha256,
    long Bytes);

public sealed record RecoveryEvidenceTransportManifest(
    string Schema,
    string Version,
    DateTimeOffset CreatedAt,
    string SourceRelocationArtifactSha256,
    string SourceCapsuleManifestDigest,
    string EvidenceEnvelopeDigest,
    IReadOnlyList<RecoveryEvidenceTransportFile> Files,
    bool ProducerAuthenticationProven,
    bool CrossMachinePortabilityProven,
    bool RecoveryExecutionAuthorized,
    bool NetworkAccessAuthorized,
    string Note);

public sealed record RecoveryEvidenceTransportInspection(
    bool Verified,
    string Status,
    string TransportZipPath,
    string TransportZipSha256,
    long TransportZipBytes,
    string TransportManifestSha256,
    RecoveryEvidenceTransportManifest Manifest,
    bool ExactZipFileSetVerified,
    bool PayloadDigestsVerified,
    bool CapsuleManifestDigestReproduced,
    bool EvidenceEnvelopeDigestReproduced,
    bool PositiveRecoveryDrillReplayed,
    bool RecoveryCapabilityAdmissionReplayed,
    bool NegativeControlMatrixReplayed,
    bool AdmissionToDrillBindingReplayed,
    bool NegativeRefusalSemanticsReplayed,
    bool AuthorityLimitationsPreserved);

public sealed record RecoveryEvidenceTransportExportReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Exported,
    string Status,
    string MainRepositoryRoot,
    string MainHeadBefore,
    IReadOnlyList<string> MainTagsBefore,
    IReadOnlyList<string> MainDirtyPathsBefore,
    string MainHeadAfter,
    IReadOnlyList<string> MainTagsAfter,
    IReadOnlyList<string> MainDirtyPathsAfter,
    bool MainRepositoryUnchanged,
    bool ExplicitUiConfirmationRequired,
    bool ExplicitUiConfirmationObserved,
    string SourceRelocationArtifactPath,
    string SourceRelocationArtifactSha256,
    string SourceRelocatedCapsuleRoot,
    string TransportZipPath,
    string TransportZipSha256,
    long TransportZipBytes,
    string TransportManifestSha256,
    bool TransportManifestVerified,
    bool ExactTransportFileSetVerified,
    bool TransportPayloadDigestsVerified,
    bool CapsuleManifestDigestReproduced,
    bool EvidenceEnvelopeDigestReproduced,
    bool SelfContainedLocalTransportCreated,
    bool ProducerAuthenticationProven,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
    bool RecoveryExecutionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    bool StableCorePromotionAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RecoveryEvidenceTransportImportReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Verified,
    string Status,
    string MainRepositoryRoot,
    string MainHeadBefore,
    IReadOnlyList<string> MainTagsBefore,
    IReadOnlyList<string> MainDirtyPathsBefore,
    string MainHeadAfter,
    IReadOnlyList<string> MainTagsAfter,
    IReadOnlyList<string> MainDirtyPathsAfter,
    bool MainRepositoryUnchanged,
    bool ExplicitUiConfirmationRequired,
    bool ExplicitUiConfirmationObserved,
    string TransportZipPath,
    string TransportZipSha256,
    long TransportZipBytes,
    string TransportManifestSha256,
    string ImportRoot,
    string ImportedCapsuleRoot,
    IReadOnlyList<RecoveryEvidenceTransportFile> ImportedFiles,
    bool ExactTransportFileSetVerified,
    bool TransportPayloadDigestsVerified,
    bool ImportedCopiesVerified,
    bool CapsuleManifestDigestReproduced,
    bool EvidenceEnvelopeDigestReproduced,
    bool PositiveRecoveryDrillReplayed,
    bool RecoveryCapabilityAdmissionReplayed,
    bool NegativeControlMatrixReplayed,
    bool AdmissionToDrillBindingReplayed,
    bool NegativeRefusalSemanticsReplayed,
    bool AuthorityLimitationsPreserved,
    bool ImportUsedOnlyTransportZipBytes,
    bool OriginalRelocationRootRequiredForImport,
    bool OriginalEvidenceArtifactsRequiredForImport,
    bool LocalExportImportBoundaryDemonstrated,
    bool ProducerAuthenticationProven,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
    bool ProductionMainRepositoryRecoveryProven,
    bool GeneralFailureRecoveryClaimAllowed,
    bool AutomaticRecoveryAuthorized,
    bool RecoveryExecutionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    bool StableCorePromotionAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Post-acceptance local recovery-evidence transport boundary.
/// Export serializes one already-proven v0.24 relocated replay capsule into a
/// self-contained ZIP. Import validates a user-selected ZIP and, only after a
/// separate explicit UI confirmation, materializes exact evidence copies into
/// a disjoint .workbench root. Neither operation executes recovery or expands
/// live authority.
/// </summary>
public sealed class RecoveryEvidenceTransportService
{
    public const string Version = "0.25.0";
    public const string TransportSchema = "matawaka.workbench-recovery-evidence-transport/v0.25";
    public const string ExportReceiptSchema = "matawaka.workbench-recovery-evidence-transport-export/v0.25";
    public const string ImportReceiptSchema = "matawaka.workbench-recovery-evidence-transport-import/v0.25";
    private const string V024AcceptedHead = "1774834bf8baf89730feb28c0d0fe4a997466039";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private const long MaxTransportBytes = 8L * 1024 * 1024;
    private const long MaxEntryBytes = 2L * 1024 * 1024;

    private static readonly string[] ExpectedCapsuleFiles =
    {
        "evidence/bounded-capability-admission.json",
        "evidence/negative-control-matrix.json",
        "evidence/positive-isolated-drill.json",
        "replay-receipt.json",
        "source-closure.json"
    };

    private static readonly string[] ExpectedZipEntries =
        new[] { "transport-manifest.json" }
            .Concat(ExpectedCapsuleFiles.Select(x => $"capsule/{x}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    public async Task<(RecoveryEvidenceTransportExportReceipt Receipt, string ArtifactPath)> ExportAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Recovery evidence export requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV025(before, "export");

        var relocationArtifactPath = FindLatestPassingRelocationArtifact(repositoryRoot);
        var relocationBytes = await File.ReadAllBytesAsync(relocationArtifactPath, cancellationToken).ConfigureAwait(false);
        var relocationSha = HashBytes(relocationBytes);
        var relocation = DeserializeBytes<RecoveryEvidenceRelocationDrillReceipt>(relocationBytes, "v0.24 relocation drill receipt");
        VerifyRelocationReceipt(relocation);

        var capsuleRoot = Path.GetFullPath(relocation.RelocatedCapsuleRoot);
        var sourceFiles = EnumerateRelativeFiles(capsuleRoot);
        if (!sourceFiles.SequenceEqual(ExpectedCapsuleFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Relocated replay capsule does not contain the exact five expected JSON files.");

        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<RecoveryEvidenceTransportFile>();
        var receiptFiles = relocation.RelocatedFiles.ToDictionary(x => x.RelativePath.Replace('\\', '/'), StringComparer.Ordinal);
        foreach (var relative in ExpectedCapsuleFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = ResolveCapsuleFile(capsuleRoot, relative);
            var fileBytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
            var sha = HashBytes(fileBytes);
            if (!receiptFiles.TryGetValue(relative, out var bound) || !bound.Verified ||
                !string.Equals(bound.Sha256, sha, StringComparison.OrdinalIgnoreCase) || bound.Bytes != fileBytes.LongLength)
                throw new InvalidDataException($"Relocated capsule file no longer matches the v0.24 drill receipt: {relative}");
            bytes[relative] = fileBytes;
            hashes[relative] = sha;
            files.Add(new RecoveryEvidenceTransportFile(relative, sha, fileBytes.LongLength));
        }

        var semantics = VerifyCapsuleSemantics(bytes, hashes);
        if (!string.Equals(semantics.CapsuleManifestDigest, relocation.RelocatedCapsuleManifestDigest, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(semantics.EvidenceEnvelopeDigest, relocation.RelocatedEvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Relocated capsule digests no longer match the retained v0.24 relocation receipt.");

        var manifest = new RecoveryEvidenceTransportManifest(
            TransportSchema,
            Version,
            DateTimeOffset.Now,
            relocationSha,
            semantics.CapsuleManifestDigest,
            semantics.EvidenceEnvelopeDigest,
            files.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray(),
            false,
            false,
            false,
            false,
            "Self-contained local recovery-evidence transport. SHA-256 and cross-evidence bindings provide integrity evidence only; this manifest does not authenticate an external producer, prove cross-machine portability, or grant recovery/network authority.");
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        var transportDirectory = Path.Combine(repositoryRoot, "artifacts", "recovery-transports");
        Directory.CreateDirectory(transportDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var zipPath = Path.Combine(transportDirectory, $"recovery-evidence-transport-v0.25-{stamp}.zip");
        CreateTransportZip(zipPath, manifestBytes, bytes);

        var inspection = InspectTransport(zipPath);
        if (!inspection.Verified)
            throw new InvalidDataException($"Freshly exported recovery evidence transport failed self-inspection: {inspection.Status}");

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during recovery evidence export.");

        var nonEffects = CommonNonEffects("export writes are limited to one transport ZIP and one export receipt under Workbench/artifacts/recovery-transports");
        var receipt = new RecoveryEvidenceTransportExportReceipt(
            ExportReceiptSchema,
            Version,
            DateTimeOffset.Now,
            true,
            "EXPORTED_SELF_CONTAINED_LOCAL_RECOVERY_EVIDENCE_CAPSULE",
            repositoryRoot,
            before.Head,
            before.Tags,
            before.DirtyPaths,
            after.Head,
            after.Tags,
            after.DirtyPaths,
            mainUnchanged,
            true,
            explicitUiConfirmation,
            relocationArtifactPath,
            relocationSha,
            capsuleRoot,
            zipPath,
            inspection.TransportZipSha256,
            inspection.TransportZipBytes,
            inspection.TransportManifestSha256,
            inspection.Verified,
            inspection.ExactZipFileSetVerified,
            inspection.PayloadDigestsVerified,
            inspection.CapsuleManifestDigestReproduced,
            inspection.EvidenceEnvelopeDigestReproduced,
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
            false,
            false,
            false,
            false,
            nonEffects,
            "v0.25 export serializes one already-proven v0.24 relocated recovery replay capsule into a self-contained local ZIP. Export is evidence transport, not producer authentication, cross-machine portability proof, live recovery authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactPath = Path.Combine(transportDirectory, $"recovery-transport-export-v0.25-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    public Task<RecoveryEvidenceTransportInspection> InspectAsync(string transportZipPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InspectTransport(transportZipPath));
    }

    public async Task<(RecoveryEvidenceTransportImportReceipt Receipt, string ArtifactPath)> ImportAsync(
        string workspaceRoot,
        string transportZipPath,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Recovery evidence import requires explicit UI confirmation after transport inspection.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV025(before, "import");

        var inspection = InspectTransport(transportZipPath);
        if (!inspection.Verified)
            throw new InvalidDataException($"Recovery evidence transport is not importable: {inspection.Status}");

        var entries = ReadTransportEntries(transportZipPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var importRoot = Path.Combine(repositoryRoot, ".workbench", "recovery-capsule-imports", $"v0.25-{inspection.TransportZipSha256[..16]}-{stamp}");
        var capsuleRoot = Path.Combine(importRoot, "capsule");
        Directory.CreateDirectory(capsuleRoot);

        var manifestPath = Path.Combine(importRoot, "transport-manifest.json");
        await File.WriteAllBytesAsync(manifestPath, entries["transport-manifest.json"], cancellationToken).ConfigureAwait(false);
        if (!string.Equals(HashFile(manifestPath), inspection.TransportManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Imported transport manifest bytes differ after write.");

        var importedFiles = new List<RecoveryEvidenceTransportFile>();
        var importedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in inspection.Manifest.Files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = entries[$"capsule/{file.RelativePath}"];
            var destination = ResolveCapsuleFile(capsuleRoot, file.RelativePath, requireExists: false);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
            var sha = HashFile(destination);
            var len = new FileInfo(destination).Length;
            if (!string.Equals(sha, file.Sha256, StringComparison.OrdinalIgnoreCase) || len != file.Bytes)
                throw new InvalidDataException($"Imported evidence copy differs from transport manifest after write: {file.RelativePath}");
            importedFiles.Add(new RecoveryEvidenceTransportFile(file.RelativePath, sha, len));
            importedHashes[file.RelativePath] = sha;
            importedBytes[file.RelativePath] = await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        var semantics = VerifyCapsuleSemantics(importedBytes, importedHashes);
        var copiesVerified = importedFiles.Count == ExpectedCapsuleFiles.Length &&
                             importedFiles.All(x => inspection.Manifest.Files.Any(m => string.Equals(m.RelativePath, x.RelativePath, StringComparison.Ordinal) &&
                                 string.Equals(m.Sha256, x.Sha256, StringComparison.OrdinalIgnoreCase) && m.Bytes == x.Bytes));
        var capsuleDigestReproduced = string.Equals(semantics.CapsuleManifestDigest, inspection.Manifest.SourceCapsuleManifestDigest, StringComparison.OrdinalIgnoreCase);
        var evidenceDigestReproduced = string.Equals(semantics.EvidenceEnvelopeDigest, inspection.Manifest.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase);

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during recovery evidence import.");

        var verified = copiesVerified && capsuleDigestReproduced && evidenceDigestReproduced && semantics.AllVerified && mainUnchanged;
        var nonEffects = CommonNonEffects("import writes are limited to exact evidence copies under Workbench/.workbench/recovery-capsule-imports and one import receipt under Workbench/artifacts/recovery-imports");
        var receipt = new RecoveryEvidenceTransportImportReceipt(
            ImportReceiptSchema,
            Version,
            DateTimeOffset.Now,
            verified,
            verified ? "IMPORTED_LOCAL_TRANSPORT_CAPSULE_VERIFIED" : "IMPORT_TRANSPORT_BINDING_FAILED",
            repositoryRoot,
            before.Head,
            before.Tags,
            before.DirtyPaths,
            after.Head,
            after.Tags,
            after.DirtyPaths,
            mainUnchanged,
            true,
            explicitUiConfirmation,
            Path.GetFullPath(transportZipPath),
            inspection.TransportZipSha256,
            inspection.TransportZipBytes,
            inspection.TransportManifestSha256,
            importRoot,
            capsuleRoot,
            importedFiles.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray(),
            inspection.ExactZipFileSetVerified,
            inspection.PayloadDigestsVerified,
            copiesVerified,
            capsuleDigestReproduced,
            evidenceDigestReproduced,
            semantics.PositiveRecoveryDrillReplayed,
            semantics.RecoveryCapabilityAdmissionReplayed,
            semantics.NegativeControlMatrixReplayed,
            semantics.AdmissionToDrillBindingReplayed,
            semantics.NegativeRefusalSemanticsReplayed,
            semantics.AuthorityLimitationsPreserved,
            true,
            false,
            false,
            verified,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
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
            "v0.25 import verifies and materializes only retained evidence bytes from one self-contained local transport ZIP. It proves a local export/import file boundary, not producer authentication, cross-machine/cross-OS portability, production recovery, general recovery, automatic recovery, live recovery authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDirectory = Path.Combine(repositoryRoot, "artifacts", "recovery-imports");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, $"recovery-transport-import-v0.25-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static RecoveryEvidenceTransportInspection InspectTransport(string transportZipPath)
    {
        if (string.IsNullOrWhiteSpace(transportZipPath) || !File.Exists(transportZipPath))
            throw new InvalidDataException("Recovery evidence transport ZIP is missing.");
        var full = Path.GetFullPath(transportZipPath);
        var info = new FileInfo(full);
        if (info.Length <= 0 || info.Length > MaxTransportBytes)
            throw new InvalidDataException($"Recovery evidence transport ZIP size is outside the bounded limit: {info.Length} bytes.");

        var entries = ReadTransportEntries(full);
        var names = entries.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var exactSet = names.SequenceEqual(ExpectedZipEntries, StringComparer.Ordinal);
        if (!exactSet)
            throw new InvalidDataException($"Recovery evidence transport ZIP file set is not exact. Expected: {string.Join(", ", ExpectedZipEntries)}; actual: {string.Join(", ", names)}");

        var manifestBytes = entries["transport-manifest.json"];
        var manifest = DeserializeBytes<RecoveryEvidenceTransportManifest>(manifestBytes, "recovery evidence transport manifest");
        if (!string.Equals(manifest.Schema, TransportSchema, StringComparison.Ordinal) || !string.Equals(manifest.Version, Version, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected recovery evidence transport manifest schema/version.");
        if (manifest.ProducerAuthenticationProven || manifest.CrossMachinePortabilityProven || manifest.RecoveryExecutionAuthorized || manifest.NetworkAccessAuthorized)
            throw new InvalidDataException("Recovery evidence transport manifest attempts to expand authority or portability claims.");

        var manifestFiles = manifest.Files.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray();
        if (manifestFiles.Length != ExpectedCapsuleFiles.Length || !manifestFiles.Select(x => x.RelativePath).SequenceEqual(ExpectedCapsuleFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Recovery evidence transport manifest does not declare the exact five capsule files.");

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var capsuleBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var digestsVerified = true;
        foreach (var file in manifestFiles)
        {
            var bytes = entries[$"capsule/{file.RelativePath}"];
            var sha = HashBytes(bytes);
            hashes[file.RelativePath] = sha;
            capsuleBytes[file.RelativePath] = bytes;
            if (!string.Equals(sha, file.Sha256, StringComparison.OrdinalIgnoreCase) || bytes.LongLength != file.Bytes)
                digestsVerified = false;
        }
        if (!digestsVerified)
            throw new InvalidDataException("One or more recovery evidence transport payload digests do not match the manifest.");

        var semantics = VerifyCapsuleSemantics(capsuleBytes, hashes);
        var capsuleDigestReproduced = string.Equals(semantics.CapsuleManifestDigest, manifest.SourceCapsuleManifestDigest, StringComparison.OrdinalIgnoreCase);
        var evidenceDigestReproduced = string.Equals(semantics.EvidenceEnvelopeDigest, manifest.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase);
        var verified = exactSet && digestsVerified && capsuleDigestReproduced && evidenceDigestReproduced && semantics.AllVerified;

        return new RecoveryEvidenceTransportInspection(
            verified,
            verified ? "TRANSPORT_VERIFIED_FOR_LOCAL_EVIDENCE_IMPORT" : "TRANSPORT_VERIFICATION_FAILED",
            full,
            HashFile(full),
            info.Length,
            HashBytes(manifestBytes),
            manifest,
            exactSet,
            digestsVerified,
            capsuleDigestReproduced,
            evidenceDigestReproduced,
            semantics.PositiveRecoveryDrillReplayed,
            semantics.RecoveryCapabilityAdmissionReplayed,
            semantics.NegativeControlMatrixReplayed,
            semantics.AdmissionToDrillBindingReplayed,
            semantics.NegativeRefusalSemanticsReplayed,
            semantics.AuthorityLimitationsPreserved);
    }

    private static Dictionary<string, byte[]> ReadTransportEntries(string zipPath)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith("/", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal) ||
                name.Contains("../", StringComparison.Ordinal) || name.Contains(":", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe or unsupported recovery evidence transport ZIP entry: {entry.FullName}");
            if (!ExpectedZipEntries.Contains(name, StringComparer.Ordinal))
                throw new InvalidDataException($"Unexpected recovery evidence transport ZIP entry: {name}");
            if (entry.Length < 0 || entry.Length > MaxEntryBytes)
                throw new InvalidDataException($"Recovery evidence transport ZIP entry exceeds the bounded size limit: {name}");
            if (result.ContainsKey(name))
                throw new InvalidDataException($"Duplicate recovery evidence transport ZIP entry: {name}");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (memory.Length != entry.Length)
                throw new InvalidDataException($"Recovery evidence transport ZIP entry length changed while reading: {name}");
            result[name] = memory.ToArray();
        }
        return result;
    }

    private static void CreateTransportZip(string zipPath, byte[] manifestBytes, IReadOnlyDictionary<string, byte[]> capsuleBytes)
    {
        if (File.Exists(zipPath))
            throw new InvalidDataException($"Recovery evidence transport target already exists: {zipPath}");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteZipEntry(archive, "transport-manifest.json", manifestBytes);
        foreach (var relative in ExpectedCapsuleFiles)
            WriteZipEntry(archive, $"capsule/{relative}", capsuleBytes[relative]);
    }

    private static void WriteZipEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string FindLatestPassingRelocationArtifact(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "recovery-relocation-drills");
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained v0.24 recovery relocation drill evidence is available for export.");
        foreach (var path in Directory.GetFiles(root, "recovery-relocation-drill-v0.24-*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var receipt = DeserializeFile<RecoveryEvidenceRelocationDrillReceipt>(path, "candidate v0.24 relocation drill receipt");
                if (receipt.Passed && string.Equals(receipt.Status, "RELOCATED_LOCAL_REPLAY_CAPSULE_VERIFIED", StringComparison.Ordinal) &&
                    string.Equals(receipt.MainHeadAfter, V024AcceptedHead, StringComparison.OrdinalIgnoreCase) && receipt.MainTagsAfter.Contains("workbench-v0.24-accepted", StringComparer.Ordinal))
                    return Path.GetFullPath(path);
            }
            catch
            {
                // Unreadable or invalid retained evidence cannot support export; continue to older evidence.
            }
        }
        throw new InvalidDataException("No passing retained v0.24 recovery relocation drill is available for export.");
    }

    private static void VerifyRelocationReceipt(RecoveryEvidenceRelocationDrillReceipt receipt)
    {
        if (!string.Equals(receipt.Schema, "matawaka.workbench-recovery-evidence-relocation-drill/v0.24", StringComparison.Ordinal) ||
            !string.Equals(receipt.Version, "0.24.0", StringComparison.Ordinal) || !receipt.Passed ||
            !string.Equals(receipt.Status, "RELOCATED_LOCAL_REPLAY_CAPSULE_VERIFIED", StringComparison.Ordinal) || !receipt.MainRepositoryUnchanged ||
            !receipt.ExactSourceCapsuleFileSetVerified || !receipt.RelocatedCopiesVerified || !receipt.RelocationRootSeparatedFromSourceReplayRoot ||
            !receipt.CapsuleManifestDigestReproduced || !receipt.RelocatedEvidenceEnvelopeDigestReproduced || !receipt.ReplayUsedOnlyRelocatedCopies ||
            receipt.OriginalReplayCapsuleDereferencedDuringRelocatedReplay || receipt.OriginalEvidenceArtifactsDereferencedDuringRelocatedReplay ||
            receipt.HistoricalFixtureRootsDereferencedDuringRelocatedReplay || !receipt.LocalRootRelocationDemonstrated || receipt.CrossMachinePortabilityProven ||
            receipt.CrossOsPortabilityProven || receipt.ProductionMainRepositoryRecoveryProven || receipt.GeneralFailureRecoveryClaimAllowed ||
            receipt.AutomaticRecoveryAuthorized || receipt.RecoveryExecutionAuthorized || receipt.RollbackAuthorized || receipt.DeletionAuthorized ||
            receipt.SourceMutationAuthorized || receipt.BuildAuthorized || receipt.CheckpointAuthorized || receipt.NetworkAccessAuthorized ||
            receipt.CatalogMutationAuthorized || receipt.AgentExecuteAuthorized || receipt.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.24 relocation receipt does not preserve the required bounded evidence/authority contract.");
    }

    private sealed record CapsuleSemantics(
        string CapsuleManifestDigest,
        string EvidenceEnvelopeDigest,
        bool PositiveRecoveryDrillReplayed,
        bool RecoveryCapabilityAdmissionReplayed,
        bool NegativeControlMatrixReplayed,
        bool AdmissionToDrillBindingReplayed,
        bool NegativeRefusalSemanticsReplayed,
        bool AuthorityLimitationsPreserved)
    {
        public bool AllVerified => PositiveRecoveryDrillReplayed && RecoveryCapabilityAdmissionReplayed && NegativeControlMatrixReplayed &&
                                   AdmissionToDrillBindingReplayed && NegativeRefusalSemanticsReplayed && AuthorityLimitationsPreserved;
    }

    private static CapsuleSemantics VerifyCapsuleSemantics(
        IReadOnlyDictionary<string, byte[]> bytes,
        IReadOnlyDictionary<string, string> hashes)
    {
        var replay = DeserializeBytes<RecoveryEvidenceReplayReceipt>(bytes["replay-receipt.json"], "transport replay receipt");
        var closure = DeserializeBytes<RecoveryEvidenceClosureReceipt>(bytes["source-closure.json"], "transport source closure");
        var drill = DeserializeBytes<IsolatedRecoveryDrillReceipt>(bytes["evidence/positive-isolated-drill.json"], "transport positive drill");
        var admission = DeserializeBytes<RecoveryCapabilityAdmissionReceipt>(bytes["evidence/bounded-capability-admission.json"], "transport recovery admission");
        var matrix = DeserializeBytes<RecoveryNegativeControlMatrixReceipt>(bytes["evidence/negative-control-matrix.json"], "transport negative matrix");

        var closureVerified = VerifyClosure(closure, hashes);
        var positive = VerifyPositiveDrill(drill);
        var admissionVerified = VerifyAdmission(admission, drill, hashes["evidence/positive-isolated-drill.json"]);
        var matrixVerified = VerifyNegativeMatrix(matrix);
        var negativeRefusals = VerifyNegativeRefusals(matrix);
        var replayVerified = VerifyReplayReceipt(replay, closure, hashes);
        var authority = VerifyAuthorityLimitations(replay, closure, admission);
        var manifestDigest = HashCapsuleManifest(hashes);
        var evidenceDigest = HashEvidenceEnvelope(closure, hashes);

        if (!closureVerified || !replayVerified)
            throw new InvalidDataException("Recovery evidence transport capsule closure/replay binding verification failed.");
        if (!string.Equals(evidenceDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery evidence transport capsule does not reproduce the closed evidence-envelope digest.");

        return new CapsuleSemantics(manifestDigest, evidenceDigest, positive, admissionVerified, matrixVerified, admissionVerified, negativeRefusals, authority);
    }

    private static bool VerifyClosure(RecoveryEvidenceClosureReceipt closure, IReadOnlyDictionary<string, string> hashes)
    {
        if (!string.Equals(closure.Schema, "matawaka.workbench-recovery-evidence-closure/v0.22", StringComparison.Ordinal) ||
            !string.Equals(closure.Version, "0.22.0", StringComparison.Ordinal) || !closure.Closed ||
            !string.Equals(closure.Status, "CLOSED_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal) || !closure.WorkingTreeClean ||
            !closure.PositiveRecoveryDrillVerified || !closure.RecoveryCapabilityAdmissionVerified || !closure.NegativeControlMatrixVerified ||
            !closure.AdmissionToDrillBindingVerified || !closure.CrossEvidenceBindingsVerified || !closure.PositiveRecoveryShapeVerified ||
            !closure.UnknownDirtyRefusalVerified || !closure.CandidateByteDriftRefusalVerified || !closure.DirtyPathSetDriftRefusalVerified ||
            !closure.AllNegativeRecoveryAttemptsRefusedBeforeAuthority || !closure.MainRepositoryUnchangedAcrossFixtureEvidence || !closure.BoundedRecoveryCapabilityPreserved ||
            closure.ProductionMainRepositoryRecoveryProven || closure.GeneralFailureRecoveryClaimAllowed || closure.AutomaticRecoveryAuthorized ||
            closure.RecoveryExecutionAuthorized || closure.RollbackAuthorized || closure.DeletionAuthorized || closure.SourceMutationAuthorized || closure.BuildAuthorized ||
            closure.CheckpointAuthorized || closure.NetworkAccessAuthorized || closure.CatalogMutationAuthorized || closure.AgentExecuteAuthorized || closure.StableCorePromotionAuthorized)
            return false;

        var relativeByRole = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive-isolated-drill"] = "evidence/positive-isolated-drill.json",
            ["bounded-capability-admission"] = "evidence/bounded-capability-admission.json",
            ["negative-control-matrix"] = "evidence/negative-control-matrix.json"
        };
        if (closure.Evidence.Count != 3) return false;
        foreach (var item in closure.Evidence)
        {
            if (!item.Verified || !relativeByRole.TryGetValue(item.Role, out var relative) ||
                !string.Equals(item.Sha256, hashes[relative], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool VerifyReplayReceipt(RecoveryEvidenceReplayReceipt replay, RecoveryEvidenceClosureReceipt closure, IReadOnlyDictionary<string, string> hashes)
    {
        if (!string.Equals(replay.Schema, "matawaka.workbench-recovery-evidence-replay/v0.23", StringComparison.Ordinal) ||
            !string.Equals(replay.Version, "0.23.0", StringComparison.Ordinal) || !replay.Replayed ||
            !string.Equals(replay.Status, "REPLAYED_PORTABLE_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal) || !replay.WorkingTreeClean ||
            !replay.ClosureDigestReproduced || !replay.PortableCopiesVerified || !replay.ReplayUsedOnlyPortableCopies || replay.HistoricalAbsolutePathsDereferencedDuringReplay ||
            replay.OriginalFixtureRootsRequiredForReplay || replay.OriginalEvidenceArtifactsRequiredAfterCapsuleCreation || !replay.PositiveRecoveryDrillReplayed ||
            !replay.RecoveryCapabilityAdmissionReplayed || !replay.NegativeControlMatrixReplayed || !replay.AdmissionToDrillBindingReplayed ||
            !replay.NegativeRefusalSemanticsReplayed || !replay.BoundedRecoveryCapabilityPreserved || replay.CrossMachinePortabilityProven ||
            replay.ProductionMainRepositoryRecoveryProven || replay.GeneralFailureRecoveryClaimAllowed || replay.AutomaticRecoveryAuthorized ||
            replay.RecoveryExecutionAuthorized || replay.RollbackAuthorized || replay.DeletionAuthorized || replay.SourceMutationAuthorized || replay.BuildAuthorized ||
            replay.CheckpointAuthorized || replay.NetworkAccessAuthorized || replay.CatalogMutationAuthorized || replay.AgentExecuteAuthorized || replay.StableCorePromotionAuthorized)
            return false;
        if (!string.Equals(replay.SourceClosureArtifactSha256, hashes["source-closure.json"], StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(replay.SourceEvidenceEnvelopeDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(replay.ReplayedEvidenceEnvelopeDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase))
            return false;
        var portable = replay.PortableEvidence.ToDictionary(x => x.Role, StringComparer.Ordinal);
        return portable.Count == 3 &&
               PortableMatches(portable, "positive-isolated-drill", "evidence/positive-isolated-drill.json", hashes) &&
               PortableMatches(portable, "bounded-capability-admission", "evidence/bounded-capability-admission.json", hashes) &&
               PortableMatches(portable, "negative-control-matrix", "evidence/negative-control-matrix.json", hashes);
    }

    private static bool PortableMatches(IReadOnlyDictionary<string, RecoveryEvidenceReplayItem> portable, string role, string relative, IReadOnlyDictionary<string, string> hashes)
        => portable.TryGetValue(role, out var item) && item.Verified && string.Equals(item.RelativePath.Replace('\\', '/'), relative, StringComparison.Ordinal) &&
           string.Equals(item.Sha256, hashes[relative], StringComparison.OrdinalIgnoreCase);

    private static bool VerifyPositiveDrill(IsolatedRecoveryDrillReceipt drill)
        => string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal) &&
           string.Equals(drill.Version, "0.19.0", StringComparison.Ordinal) && drill.Passed && drill.MainRepositoryUnchanged &&
           string.Equals(drill.MainHeadBefore, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) && drill.MainDirtyPathsBefore.Count == 0 && drill.MainDirtyPathsAfter.Count == 0 &&
           drill.CandidateDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fixture/new.txt", "fixture/tracked.txt" }, StringComparer.Ordinal) &&
           string.Equals(drill.PreRecoveryClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
           string.Equals(drill.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
           string.Equals(drill.RecoveryExecutionStatus, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
           string.Equals(drill.PostRecoveryClassification, "CLEAN_ACCEPTED", StringComparison.Ordinal) && drill.PostRecoveryWorkingTreeClean &&
           drill.TrackedFileRestored && drill.UntrackedAdditionRemoved && drill.FixtureHeadUnchanged && drill.FixtureTagsUnchanged &&
           drill.Authority.ExplicitUiConfirmationRequired && !drill.Authority.MainRepositoryMutationAllowed && !drill.Authority.BuildAllowed &&
           !drill.Authority.CheckpointAllowed && !drill.Authority.NetworkAccessAllowed && !drill.Authority.CatalogMutationAllowed && !drill.Authority.AgentExecuteAllowed;

    private static bool VerifyAdmission(RecoveryCapabilityAdmissionReceipt admission, IsolatedRecoveryDrillReceipt drill, string drillSha)
        => string.Equals(admission.Schema, "matawaka.workbench-recovery-capability-admission/v0.20", StringComparison.Ordinal) &&
           string.Equals(admission.Version, "0.20.0", StringComparison.Ordinal) && admission.Admitted &&
           string.Equals(admission.Status, "ADMITTED_ISOLATED_BOUNDED_RECOVERY_CAPABILITY", StringComparison.Ordinal) && admission.BoundedRecoveryCapabilityAdmitted &&
           string.Equals(admission.EvidenceArtifactSha256, drillSha, StringComparison.OrdinalIgnoreCase) && string.Equals(admission.EvidenceSchema, drill.Schema, StringComparison.Ordinal) &&
           string.Equals(admission.EvidenceVersion, drill.Version, StringComparison.Ordinal) && string.Equals(admission.EvidenceMainHead, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
           admission.EvidenceMainTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
           !admission.ProductionMainRepositoryRecoveryProven && !admission.GeneralFailureRecoveryClaimAllowed && !admission.AutomaticRecoveryAuthorized &&
           !admission.RecoveryExecutionAuthorized && !admission.RollbackAuthorized && !admission.DeletionAuthorized && !admission.SourceMutationAuthorized &&
           !admission.BuildAuthorized && !admission.CheckpointAuthorized && !admission.NetworkAccessAuthorized && !admission.CatalogMutationAuthorized &&
           !admission.AgentExecuteAuthorized && !admission.StableCorePromotionAuthorized;

    private static bool VerifyNegativeMatrix(RecoveryNegativeControlMatrixReceipt matrix)
        => string.Equals(matrix.Schema, "matawaka.workbench-recovery-negative-control-matrix/v0.21", StringComparison.Ordinal) &&
           string.Equals(matrix.Version, "0.21.0", StringComparison.Ordinal) && matrix.Passed && matrix.MainRepositoryUnchanged &&
           matrix.MainDirtyPathsBefore.Count == 0 && matrix.MainDirtyPathsAfter.Count == 0 && matrix.UnknownDirtyRefused && matrix.ByteDriftAfterPlanRefused &&
           matrix.PathSetDriftAfterPlanRefused && matrix.AllRecoveryAttemptsRefusedBeforeAuthority && matrix.Scenarios.Count == 3 &&
           matrix.Scenarios.All(x => x.Passed && x.ExecutionAttempted && x.ExecutionRejected && !x.RecoveryAuthorityArtifactCreated && !x.RecoveryExecutionArtifactCreated &&
               x.CandidateStatePreservedAfterRefusal && x.FixtureHeadUnchanged && x.FixtureTagsUnchanged) && matrix.Authority.ExplicitUiConfirmationRequired &&
           !matrix.Authority.MainRepositoryMutationAllowed && !matrix.Authority.ExpectedRecoveryMutationAllowed && !matrix.Authority.BuildAllowed &&
           !matrix.Authority.CheckpointAllowed && !matrix.Authority.NetworkAccessAllowed && !matrix.Authority.CatalogMutationAllowed && !matrix.Authority.AgentExecuteAllowed;

    private static bool VerifyNegativeRefusals(RecoveryNegativeControlMatrixReceipt matrix)
    {
        var unknown = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "unknown-dirty-refused", StringComparison.Ordinal));
        var bytes = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "candidate-byte-drift-after-plan-refused", StringComparison.Ordinal));
        var paths = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "dirty-path-set-drift-after-plan-refused", StringComparison.Ordinal));
        return unknown is not null && bytes is not null && paths is not null &&
               string.Equals(unknown.AssessmentClassification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) &&
               string.Equals(unknown.RecoveryPlanStatus, "REFUSED_UNBOUNDED_RECOVERY_PLAN", StringComparison.Ordinal) && !unknown.SeparateRecoveryAuthorityEligible &&
               string.Equals(bytes.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
               string.Equals(bytes.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && bytes.SeparateRecoveryAuthorityEligible &&
               bytes.RejectionMessage.Contains("byte-bound", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(paths.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
               string.Equals(paths.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && paths.SeparateRecoveryAuthorityEligible &&
               paths.RejectionMessage.Contains("changed after", StringComparison.OrdinalIgnoreCase);
    }

    private static bool VerifyAuthorityLimitations(RecoveryEvidenceReplayReceipt replay, RecoveryEvidenceClosureReceipt closure, RecoveryCapabilityAdmissionReceipt admission)
        => !replay.CrossMachinePortabilityProven && !replay.ProductionMainRepositoryRecoveryProven && !replay.GeneralFailureRecoveryClaimAllowed &&
           !replay.AutomaticRecoveryAuthorized && !replay.RecoveryExecutionAuthorized && !replay.RollbackAuthorized && !replay.DeletionAuthorized &&
           !replay.SourceMutationAuthorized && !replay.BuildAuthorized && !replay.CheckpointAuthorized && !replay.NetworkAccessAuthorized &&
           !replay.CatalogMutationAuthorized && !replay.AgentExecuteAuthorized && !replay.StableCorePromotionAuthorized &&
           !closure.ProductionMainRepositoryRecoveryProven && !closure.GeneralFailureRecoveryClaimAllowed && !closure.AutomaticRecoveryAuthorized &&
           !closure.RecoveryExecutionAuthorized && !closure.RollbackAuthorized && !closure.DeletionAuthorized && !closure.SourceMutationAuthorized &&
           !closure.BuildAuthorized && !closure.CheckpointAuthorized && !closure.NetworkAccessAuthorized && !closure.CatalogMutationAuthorized &&
           !closure.AgentExecuteAuthorized && !closure.StableCorePromotionAuthorized && admission.BoundedRecoveryCapabilityAdmitted &&
           !admission.ProductionMainRepositoryRecoveryProven && !admission.GeneralFailureRecoveryClaimAllowed && !admission.AutomaticRecoveryAuthorized &&
           !admission.RecoveryExecutionAuthorized && !admission.RollbackAuthorized && !admission.DeletionAuthorized && !admission.SourceMutationAuthorized &&
           !admission.BuildAuthorized && !admission.CheckpointAuthorized && !admission.NetworkAccessAuthorized && !admission.CatalogMutationAuthorized &&
           !admission.AgentExecuteAuthorized && !admission.StableCorePromotionAuthorized;

    private static string HashEvidenceEnvelope(RecoveryEvidenceClosureReceipt closure, IReadOnlyDictionary<string, string> hashes)
    {
        var relativeByRole = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive-isolated-drill"] = "evidence/positive-isolated-drill.json",
            ["bounded-capability-admission"] = "evidence/bounded-capability-admission.json",
            ["negative-control-matrix"] = "evidence/negative-control-matrix.json"
        };
        var canonical = string.Join("\n", closure.Evidence.OrderBy(x => x.Role, StringComparer.Ordinal).Select(x =>
        {
            if (!relativeByRole.TryGetValue(x.Role, out var relative))
                throw new InvalidDataException($"Unexpected recovery closure role in v0.25 transport: {x.Role}");
            return $"{x.Role}|{hashes[relative]}|{x.Schema}|{x.Version}";
        })) + "\n";
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static string HashCapsuleManifest(IReadOnlyDictionary<string, string> hashes)
    {
        var canonical = string.Join("\n", hashes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}|{x.Value}")) + "\n";
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static IReadOnlyList<string> EnumerateRelativeFiles(string capsuleRoot)
    {
        if (!Directory.Exists(capsuleRoot)) throw new InvalidDataException($"Recovery evidence capsule root is missing: {capsuleRoot}");
        var root = Path.GetFullPath(capsuleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.GetFiles(capsuleRoot, "*", SearchOption.AllDirectories).Select(file =>
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery evidence capsule file escapes its root.");
            return Path.GetRelativePath(capsuleRoot, full).Replace('\\', '/');
        }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static string ResolveCapsuleFile(string capsuleRoot, string relative, bool requireExists = true)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe recovery evidence capsule relative path: {relative}");
        var root = Path.GetFullPath(capsuleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(capsuleRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recovery evidence capsule path escapes root: {relative}");
        if (requireExists && !File.Exists(full))
            throw new InvalidDataException($"Recovery evidence capsule file is missing: {relative}");
        return full;
    }

    private static T DeserializeFile<T>(string path, string label)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static T DeserializeBytes<T>(byte[] bytes, string label)
        => JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private sealed record GitState(string Head, IReadOnlyList<string> Tags, IReadOnlyList<string> DirtyPaths);

    private static async Task<GitState> ObserveGitStateAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static void RequireAcceptedV025(GitState state, string operation)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException($"Recovery evidence {operation} requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains("workbench-v0.25-accepted", StringComparer.Ordinal))
            throw new InvalidDataException($"Recovery evidence {operation} is enabled only after workbench-v0.25-accepted points at the current HEAD.");
    }

    private static bool GitStatesEqual(GitState left, GitState right)
        => string.Equals(left.Head, right.Head, StringComparison.OrdinalIgnoreCase) && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal) &&
           left.DirtyPaths.SequenceEqual(right.DirtyPaths, StringComparer.Ordinal);

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery transport gate: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            paths.Add(path.Trim('"').Replace('\\', '/').TrimStart('/'));
        }
        return paths.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        if (args.Length == 0 || !new[] { "rev-parse", "tag", "status" }.Contains(args[0], StringComparer.Ordinal))
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in the v0.25 recovery evidence transport gate.");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery transport process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Read-only Git recovery transport operation timed out after {GitTimeout.TotalSeconds:0} seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Read-only Git recovery transport operation failed: {string.Join(' ', args)} :: {stderr.Trim()}");
        return stdout;
    }

    private static IReadOnlyList<string> CommonNonEffects(string boundedWrite)
        => new[]
        {
            "no main Workbench source mutation",
            "no source restore or rollback",
            "no deletion or modification of retained source recovery evidence",
            "no dotnet restore/build/test/publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no automatic recovery authority",
            "no general recovery claim",
            "no production-main-repository recovery proof",
            "no producer-authentication claim",
            "no cross-machine or cross-OS portability claim",
            "no Stable Core or interface-registry promotion",
            boundedWrite
        };

    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
