using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryTransportIndependenceMaterializedFile(
    string RelativePath,
    string Sha256,
    long Bytes,
    bool Verified);

public sealed record RecoveryEvidenceTransportIndependenceDrillReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Passed,
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
    string SourceImportArtifactPath,
    string SourceImportArtifactSha256,
    string SourceTransportZipPath,
    string SourceTransportZipSha256,
    string SourceTransportManifestSha256,
    bool SourceImportReceiptVerified,
    string DrillRoot,
    string CopiedTransportZipPath,
    string CopiedTransportZipSha256,
    long CopiedTransportZipBytes,
    bool CopiedTransportByteIdentical,
    bool CopiedTransportSeparatedFromSourceTransportRoot,
    bool CopiedTransportInspectionVerified,
    bool ExactTransportFileSetVerified,
    bool TransportPayloadDigestsVerified,
    bool TransportManifestDigestReproduced,
    bool CapsuleManifestDigestReproduced,
    bool EvidenceEnvelopeDigestReproduced,
    string IndependentMaterializationRoot,
    IReadOnlyList<RecoveryTransportIndependenceMaterializedFile> IndependentMaterializedFiles,
    bool IndependentMaterializedCopiesVerified,
    bool PositiveRecoveryDrillReplayed,
    bool RecoveryCapabilityAdmissionReplayed,
    bool NegativeControlMatrixReplayed,
    bool AdmissionToDrillBindingReplayed,
    bool NegativeRefusalSemanticsReplayed,
    bool AuthorityLimitationsPreserved,
    bool TransportOnlyEvidenceReplayPathGuardEnabled,
    int OriginalEvidencePathAccessAttemptsDuringTransportReplay,
    bool ReplayUsedOnlyCopiedTransportBytes,
    bool OriginalTransportZipRequiredAfterCopy,
    bool OriginalRelocationRootRequiredForDrill,
    bool OriginalReplayRootRequiredForDrill,
    bool OriginalEvidenceArtifactsRequiredForDrill,
    bool HistoricalAbsolutePathsDereferencedDuringTransportReplay,
    bool LocalTransportIndependenceDemonstrated,
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
    IReadOnlyList<string> Scope,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Post-acceptance local independence drill over one passing v0.25 recovery
/// evidence transport import. The drill first binds the retained import receipt
/// and its exact transport ZIP, copies that ZIP into a disjoint .workbench root,
/// and then enters a transport-only evidence replay phase. From that phase
/// forward the evidence verifier is given only the copied ZIP path; original
/// replay/relocation/evidence paths are neither inputs nor dereferenced.
///
/// The path guard is an application-level evidence-access invariant, not an OS
/// sandbox. The drill proves one local transport-copy independence shape only;
/// it does not authenticate a producer or prove cross-machine/cross-OS
/// portability and it does not create live recovery authority.
/// </summary>
public sealed class RecoveryEvidenceTransportIndependenceDrillService
{
    public const string Version = "0.26.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-evidence-transport-independence-drill/v0.26";
    private const string ExpectedTag = "workbench-v0.26-accepted";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] ExpectedZipEntries =
    {
        "capsule/evidence/bounded-capability-admission.json",
        "capsule/evidence/negative-control-matrix.json",
        "capsule/evidence/positive-isolated-drill.json",
        "capsule/replay-receipt.json",
        "capsule/source-closure.json",
        "transport-manifest.json"
    };

    private readonly RecoveryEvidenceTransportService _transportService = new();

    public async Task<(RecoveryEvidenceTransportIndependenceDrillReceipt Receipt, string ArtifactPath)> RunAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Recovery transport independence drill requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV026(before);

        var importArtifactPath = FindLatestPassingImportArtifact(repositoryRoot);
        var importBytes = await File.ReadAllBytesAsync(importArtifactPath, cancellationToken).ConfigureAwait(false);
        var importArtifactSha = HashBytes(importBytes);
        var importReceipt = JsonSerializer.Deserialize<RecoveryEvidenceTransportImportReceipt>(importBytes, JsonOptions)
            ?? throw new InvalidDataException("Passing v0.25 recovery transport import receipt could not be parsed.");
        VerifyImportReceipt(importReceipt);

        var sourceTransportPath = Path.GetFullPath(importReceipt.TransportZipPath);
        if (!File.Exists(sourceTransportPath))
            throw new InvalidDataException("The transport ZIP bound by the retained v0.25 import receipt is missing.");
        var sourceTransportSha = HashFile(sourceTransportPath);
        if (!string.Equals(sourceTransportSha, importReceipt.TransportZipSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained source transport ZIP no longer matches the v0.25 import receipt.");

        var sourceInspection = await _transportService.InspectAsync(sourceTransportPath, cancellationToken).ConfigureAwait(false);
        if (!sourceInspection.Verified || !string.Equals(sourceInspection.TransportManifestSha256, importReceipt.TransportManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained source transport ZIP no longer satisfies the v0.25 transport inspection contract.");

        var transportBytes = await File.ReadAllBytesAsync(sourceTransportPath, cancellationToken).ConfigureAwait(false);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var drillRoot = Path.Combine(repositoryRoot, ".workbench", "recovery-transport-independence", $"v0.26-{sourceTransportSha[..16]}-{stamp}");
        Directory.CreateDirectory(drillRoot);
        var copiedTransportPath = Path.Combine(drillRoot, "transport-copy.zip");
        await File.WriteAllBytesAsync(copiedTransportPath, transportBytes, cancellationToken).ConfigureAwait(false);

        var copiedTransportSha = HashFile(copiedTransportPath);
        var copiedInfo = new FileInfo(copiedTransportPath);
        var copiedByteIdentical = string.Equals(copiedTransportSha, sourceTransportSha, StringComparison.OrdinalIgnoreCase) && copiedInfo.Length == transportBytes.LongLength;
        if (!copiedByteIdentical)
            throw new InvalidDataException("Independent transport copy differs from the v0.25 source transport bytes.");

        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourceTransportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var copiedRoot = Path.GetFullPath(drillRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var separated = !copiedRoot.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) && !sourceRoot.StartsWith(copiedRoot, StringComparison.OrdinalIgnoreCase);
        if (!separated)
            throw new InvalidDataException("Transport independence drill root must be disjoint from the source transport directory.");

        // Transport-only evidence replay phase begins here. From this point until
        // semantic verification is complete, the only evidence source path passed
        // to a verifier is copiedTransportPath under drillRoot. Historical paths
        // embedded inside JSON receipts remain data fields and are not dereferenced.
        EnsureInsideRoot(copiedTransportPath, drillRoot);
        var copiedInspection = await _transportService.InspectAsync(copiedTransportPath, cancellationToken).ConfigureAwait(false);
        if (!copiedInspection.Verified)
            throw new InvalidDataException($"Copied recovery evidence transport failed transport-only inspection: {copiedInspection.Status}");

        var transportManifestDigestReproduced = string.Equals(copiedInspection.TransportManifestSha256, importReceipt.TransportManifestSha256, StringComparison.OrdinalIgnoreCase);
        var capsuleManifestDigestReproduced = string.Equals(copiedInspection.Manifest.SourceCapsuleManifestDigest, sourceInspection.Manifest.SourceCapsuleManifestDigest, StringComparison.OrdinalIgnoreCase);
        var evidenceEnvelopeDigestReproduced = string.Equals(copiedInspection.Manifest.EvidenceEnvelopeDigest, sourceInspection.Manifest.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase);
        if (!transportManifestDigestReproduced || !capsuleManifestDigestReproduced || !evidenceEnvelopeDigestReproduced)
            throw new InvalidDataException("Copied transport did not reproduce the v0.25 manifest/capsule/evidence digests.");

        var exactEntries = ReadExactTransportEntries(copiedTransportPath);
        var materializationRoot = Path.Combine(drillRoot, "transport-only-import");
        Directory.CreateDirectory(materializationRoot);
        var materialized = new List<RecoveryTransportIndependenceMaterializedFile>();

        foreach (var pair in exactEntries.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveWithinRoot(materializationRoot, pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, pair.Value, cancellationToken).ConfigureAwait(false);
            var sha = HashFile(destination);
            var len = new FileInfo(destination).Length;
            var expectedSha = string.Equals(pair.Key, "transport-manifest.json", StringComparison.Ordinal)
                ? copiedInspection.TransportManifestSha256
                : copiedInspection.Manifest.Files.Single(x => string.Equals($"capsule/{x.RelativePath}", pair.Key, StringComparison.Ordinal)).Sha256;
            var expectedBytes = string.Equals(pair.Key, "transport-manifest.json", StringComparison.Ordinal)
                ? pair.Value.LongLength
                : copiedInspection.Manifest.Files.Single(x => string.Equals($"capsule/{x.RelativePath}", pair.Key, StringComparison.Ordinal)).Bytes;
            var verified = string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase) && len == expectedBytes;
            materialized.Add(new RecoveryTransportIndependenceMaterializedFile(pair.Key, sha, len, verified));
        }

        var independentCopiesVerified = materialized.Count == ExpectedZipEntries.Length && materialized.All(x => x.Verified);
        if (!independentCopiesVerified)
            throw new InvalidDataException("Transport-only materialized copies do not match the copied ZIP bindings.");

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during the transport independence drill.");

        var passed = copiedByteIdentical && separated && copiedInspection.Verified && copiedInspection.ExactZipFileSetVerified &&
                     copiedInspection.PayloadDigestsVerified && transportManifestDigestReproduced && capsuleManifestDigestReproduced &&
                     evidenceEnvelopeDigestReproduced && independentCopiesVerified && copiedInspection.PositiveRecoveryDrillReplayed &&
                     copiedInspection.RecoveryCapabilityAdmissionReplayed && copiedInspection.NegativeControlMatrixReplayed &&
                     copiedInspection.AdmissionToDrillBindingReplayed && copiedInspection.NegativeRefusalSemanticsReplayed &&
                     copiedInspection.AuthorityLimitationsPreserved && mainUnchanged;

        var scope = new[]
        {
            "bind one passing retained v0.25 import receipt and its exact self-contained transport ZIP",
            "copy the exact transport ZIP bytes into a disjoint Workbench/.workbench/recovery-transport-independence root",
            "after the copy boundary, inspect and replay evidence semantics using only the copied transport ZIP path",
            "materialize exact transport entries only under the drill root and re-verify their SHA-256/length bindings",
            "reproduce transport-manifest, capsule-manifest and recovery evidence-envelope digests from the copied transport",
            "leave the main Workbench Git state unchanged"
        };
        var limitations = new[]
        {
            "the path guard is an application-level evidence-access invariant, not OS-enforced filesystem isolation",
            "the source transport ZIP must exist and match the retained v0.25 import receipt before the independent copy is created",
            "the drill runs on the same machine and filesystem family and does not prove cross-machine or cross-OS portability",
            "historical absolute paths may remain inside JSON as data fields; they are not dereferenced during transport-only replay",
            "SHA-256/cross-evidence bindings do not authenticate an external producer",
            "the drill does not prove production-main recovery, arbitrary future schema compatibility, canonical UU-AAP conformance, or Stable Core suitability"
        };
        var nonEffects = new[]
        {
            "no main Workbench source mutation",
            "no source restore or rollback",
            "no deletion or modification of source recovery evidence or the source transport ZIP",
            "no dotnet restore/build/test/publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no automatic recovery authority",
            "no producer-authentication claim",
            "no cross-machine or cross-OS portability claim",
            "no Stable Core or interface-registry promotion",
            "writes are limited to the bounded independence drill root and one drill receipt under Workbench/artifacts/recovery-transport-independence"
        };

        var receipt = new RecoveryEvidenceTransportIndependenceDrillReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            passed ? "INDEPENDENT_LOCAL_TRANSPORT_CAPSULE_VERIFIED" : "TRANSPORT_INDEPENDENCE_DRILL_FAILED",
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
            importArtifactPath,
            importArtifactSha,
            sourceTransportPath,
            sourceTransportSha,
            importReceipt.TransportManifestSha256,
            true,
            drillRoot,
            copiedTransportPath,
            copiedTransportSha,
            copiedInfo.Length,
            copiedByteIdentical,
            separated,
            copiedInspection.Verified,
            copiedInspection.ExactZipFileSetVerified,
            copiedInspection.PayloadDigestsVerified,
            transportManifestDigestReproduced,
            capsuleManifestDigestReproduced,
            evidenceEnvelopeDigestReproduced,
            materializationRoot,
            materialized.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray(),
            independentCopiesVerified,
            copiedInspection.PositiveRecoveryDrillReplayed,
            copiedInspection.RecoveryCapabilityAdmissionReplayed,
            copiedInspection.NegativeControlMatrixReplayed,
            copiedInspection.AdmissionToDrillBindingReplayed,
            copiedInspection.NegativeRefusalSemanticsReplayed,
            copiedInspection.AuthorityLimitationsPreserved,
            true,
            0,
            true,
            false,
            false,
            false,
            false,
            false,
            passed,
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
            scope,
            limitations,
            nonEffects,
            "v0.26 demonstrates one same-machine transport-copy independence shape: after an exact v0.25 transport ZIP is copied into a disjoint drill root, evidence inspection, semantic replay and exact evidence materialization are performed from the copied transport only. This is not OS isolation, producer authentication, cross-machine/cross-OS portability proof, live recovery authority, production recovery proof, a general recovery claim, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDirectory = Path.Combine(repositoryRoot, "artifacts", "recovery-transport-independence");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, $"recovery-transport-independence-v0.26-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static string FindLatestPassingImportArtifact(string repositoryRoot)
    {
        var directory = Path.Combine(repositoryRoot, "artifacts", "recovery-imports");
        if (!Directory.Exists(directory))
            throw new InvalidDataException("No retained v0.25 recovery import artifacts directory exists.");

        foreach (var path in Directory.GetFiles(directory, "recovery-transport-import-v0.25-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<RecoveryEvidenceTransportImportReceipt>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (receipt is not null && IsPassingImportReceipt(receipt))
                    return Path.GetFullPath(path);
            }
            catch
            {
                // Invalid retained evidence cannot support the drill; continue to older receipts.
            }
        }

        throw new InvalidDataException("No passing retained v0.25 recovery transport import receipt is available for the independence drill.");
    }

    private static void VerifyImportReceipt(RecoveryEvidenceTransportImportReceipt receipt)
    {
        if (!IsPassingImportReceipt(receipt))
            throw new InvalidDataException("Retained v0.25 recovery transport import receipt does not preserve the required bounded evidence/authority contract.");
    }

    private static bool IsPassingImportReceipt(RecoveryEvidenceTransportImportReceipt receipt)
        => string.Equals(receipt.Schema, RecoveryEvidenceTransportService.ImportReceiptSchema, StringComparison.Ordinal) &&
           string.Equals(receipt.Version, RecoveryEvidenceTransportService.Version, StringComparison.Ordinal) &&
           receipt.Verified && string.Equals(receipt.Status, "IMPORTED_LOCAL_TRANSPORT_CAPSULE_VERIFIED", StringComparison.Ordinal) &&
           receipt.MainRepositoryUnchanged && receipt.ExplicitUiConfirmationRequired && receipt.ExplicitUiConfirmationObserved &&
           receipt.ExactTransportFileSetVerified && receipt.TransportPayloadDigestsVerified && receipt.ImportedCopiesVerified &&
           receipt.CapsuleManifestDigestReproduced && receipt.EvidenceEnvelopeDigestReproduced &&
           receipt.PositiveRecoveryDrillReplayed && receipt.RecoveryCapabilityAdmissionReplayed && receipt.NegativeControlMatrixReplayed &&
           receipt.AdmissionToDrillBindingReplayed && receipt.NegativeRefusalSemanticsReplayed && receipt.AuthorityLimitationsPreserved &&
           receipt.ImportUsedOnlyTransportZipBytes && !receipt.OriginalRelocationRootRequiredForImport && !receipt.OriginalEvidenceArtifactsRequiredForImport &&
           receipt.LocalExportImportBoundaryDemonstrated && !receipt.ProducerAuthenticationProven && !receipt.CrossMachinePortabilityProven &&
           !receipt.CrossOsPortabilityProven && !receipt.ProductionMainRepositoryRecoveryProven && !receipt.GeneralFailureRecoveryClaimAllowed &&
           !receipt.AutomaticRecoveryAuthorized && !receipt.RecoveryExecutionAuthorized && !receipt.RollbackAuthorized && !receipt.DeletionAuthorized &&
           !receipt.SourceMutationAuthorized && !receipt.BuildAuthorized && !receipt.CheckpointAuthorized && !receipt.NetworkAccessAuthorized &&
           !receipt.CatalogMutationAuthorized && !receipt.AgentExecuteAuthorized && !receipt.StableCorePromotionAuthorized;

    private static Dictionary<string, byte[]> ReadExactTransportEntries(string zipPath)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal) ||
                name.Contains("../", StringComparison.Ordinal) || name.Contains(":", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe recovery transport entry in independence drill: {entry.FullName}");
            if (!ExpectedZipEntries.Contains(name, StringComparer.Ordinal))
                throw new InvalidDataException($"Unexpected recovery transport entry in independence drill: {name}");
            if (result.ContainsKey(name))
                throw new InvalidDataException($"Duplicate recovery transport entry in independence drill: {name}");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            result[name] = memory.ToArray();
        }

        var names = result.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(ExpectedZipEntries.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Copied recovery transport ZIP does not contain the exact expected six entries.");
        return result;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static void RequireAcceptedV026(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery transport independence drill requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Recovery transport independence drill is enabled only after {ExpectedTag} points at the current HEAD.");
    }

    private static void EnsureInsideRoot(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Transport-only replay attempted to access evidence outside the bounded drill root.");
    }

    private static string ResolveWithinRoot(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains(":", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe relative path in transport-only materialization: {relative}");
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transport-only materialization path escapes drill root: {relative}");
        return full;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record GitState(string Head, IReadOnlyList<string> Tags, IReadOnlyList<string> DirtyPaths);

    private static async Task<GitState> ObserveGitStateAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static bool GitStatesEqual(GitState left, GitState right)
        => string.Equals(left.Head, right.Head, StringComparison.OrdinalIgnoreCase) &&
           left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal) &&
           left.DirtyPaths.SequenceEqual(right.DirtyPaths, StringComparer.Ordinal);

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4)
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.26 independence drill: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            paths.Add(path.Trim('"').Replace('\\', '/').TrimStart('/'));
        }
        return paths.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        if (args.Length == 0 || !new[] { "rev-parse", "tag", "status" }.Contains(args[0], StringComparer.Ordinal))
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in the v0.26 transport independence drill.");

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
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.26 independence drill.");

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
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.26 independence drill.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.26 independence drill: {stderr.Trim()}");
        return stdout;
    }
}
