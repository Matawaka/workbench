using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryTransportAdversarialControlScenarioReceipt(
    string Id,
    bool Passed,
    string CandidateTransportZipPath,
    string InitialBoundSha256,
    string CandidateSha256AtAttempt,
    string NegativeMutation,
    bool InspectionAttempted,
    bool Rejected,
    string RejectionMessage,
    bool EvidenceMaterializationAttempted,
    bool EvidenceMaterializationRootCreated,
    bool CandidateTransportPreservedAfterRefusal,
    bool SourceTransportUnchanged);

public sealed record RecoveryTransportAdversarialControlAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string ControlRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool MainRepositoryMutationAllowed,
    bool SourceTransportMutationAllowed,
    bool IsolatedTransportCopyMutationAllowed,
    bool VerifyOnlyInspectionAllowed,
    bool EvidenceMaterializationAllowed,
    bool RecoveryExecutionAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record RecoveryTransportAdversarialControlMatrixReceipt(
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
    string SourceIndependenceArtifactPath,
    string SourceIndependenceArtifactSha256,
    string SourceTransportZipPath,
    string SourceTransportZipSha256,
    bool SourceIndependenceReceiptVerified,
    string ControlRoot,
    IReadOnlyList<RecoveryTransportAdversarialControlScenarioReceipt> Scenarios,
    bool CopyByteDriftAfterBindingRefused,
    bool ExtraZipEntryRefused,
    bool TransportManifestDriftRefused,
    bool AllControlsRefusedBeforeEvidenceMaterialization,
    bool SourceTransportUnchanged,
    RecoveryTransportAdversarialControlAuthorityReceipt Authority,
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
/// Post-acceptance adversarial controls over one retained passing v0.26
/// transport-independence drill. The matrix never mutates the source transport
/// or main Workbench source. It creates three isolated transport copies and
/// proves refusal for: byte drift after explicit SHA binding, an unexpected ZIP
/// entry, and a structurally valid but digest-drifted transport manifest.
///
/// The existing v0.25 transport verifier is invoked only in verify-only mode.
/// No control calls import/materialization or recovery execution.
/// </summary>
public sealed class RecoveryTransportAdversarialControlMatrixService
{
    public const string Version = "0.27.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-transport-adversarial-control-matrix/v0.27";
    public const string AuthoritySchema = "matawaka.workbench-recovery-transport-adversarial-control-matrix-authority/v0.27";
    private const string ExpectedTag = "workbench-v0.27-accepted";
    private const string ExpectedSourceIndependenceSchema = "matawaka.workbench-recovery-evidence-transport-independence-drill/v0.26";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly RecoveryEvidenceTransportService _transportService = new();

    public async Task<(RecoveryTransportAdversarialControlMatrixReceipt Receipt, string ArtifactPath)> RunAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Transport adversarial controls require explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV027(before);

        var sourceArtifactPath = FindLatestPassingIndependenceArtifact(repositoryRoot);
        var sourceArtifactBytes = await File.ReadAllBytesAsync(sourceArtifactPath, cancellationToken).ConfigureAwait(false);
        var sourceArtifactSha = HashBytes(sourceArtifactBytes);
        var sourceReceipt = JsonSerializer.Deserialize<RecoveryEvidenceTransportIndependenceDrillReceipt>(sourceArtifactBytes, JsonOptions)
            ?? throw new InvalidDataException("Retained v0.26 transport-independence receipt could not be parsed.");
        VerifySourceIndependenceReceipt(sourceReceipt);

        var sourceTransportPath = Path.GetFullPath(sourceReceipt.CopiedTransportZipPath);
        if (!File.Exists(sourceTransportPath))
            throw new InvalidDataException("The v0.26 copied transport ZIP bound by the retained independence receipt is missing.");
        var sourceTransportSha = HashFile(sourceTransportPath);
        if (!string.Equals(sourceTransportSha, sourceReceipt.CopiedTransportZipSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceTransportSha, sourceReceipt.SourceTransportZipSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained v0.26 transport ZIP no longer matches its byte-bound independence receipt.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var controlRoot = Path.Combine(repositoryRoot, ".workbench", "recovery-transport-adversarial-controls", $"v0.27-{stamp}");
        Directory.CreateDirectory(controlRoot);

        var nonEffects = new[]
        {
            "no main Workbench source mutation",
            "no source restore or rollback",
            "no deletion or modification of the source v0.26/v0.25 transport ZIP",
            "no recovery evidence import/materialization",
            "no recovery execution",
            "no dotnet restore/build/test/publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no producer-authentication claim",
            "no cross-machine or cross-OS portability claim",
            "no Stable Core or interface-registry promotion",
            "writes are limited to isolated adversarial transport copies under Workbench/.workbench/recovery-transport-adversarial-controls and one matrix receipt"
        };

        var authority = new RecoveryTransportAdversarialControlAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.isolated-transport-adversarial-control-matrix",
            repositoryRoot,
            controlRoot,
            "explicit Transport negatives button + confirmation dialog after v0.27 accepted",
            true,
            false,
            false,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "copy one exact retained v0.26-bound transport ZIP into three isolated control roots",
                "mutate only those isolated transport copies to create exact negative-control states",
                "invoke the existing v0.25 verify-only transport inspection against negative copies when applicable",
                "retain negative transport copies and one matrix receipt for audit"
            },
            nonEffects);

        var copyDrift = await RunCopyByteDriftAfterBindingAsync(
            Path.Combine(controlRoot, "copy-byte-drift-after-binding"),
            sourceTransportPath,
            sourceTransportSha,
            cancellationToken).ConfigureAwait(false);

        var extraEntry = await RunExtraZipEntryControlAsync(
            Path.Combine(controlRoot, "extra-zip-entry"),
            sourceTransportPath,
            sourceTransportSha,
            cancellationToken).ConfigureAwait(false);

        var manifestDrift = await RunManifestDriftControlAsync(
            Path.Combine(controlRoot, "transport-manifest-drift"),
            sourceTransportPath,
            sourceTransportSha,
            cancellationToken).ConfigureAwait(false);

        var sourceTransportUnchanged = string.Equals(HashFile(sourceTransportPath), sourceTransportSha, StringComparison.OrdinalIgnoreCase);
        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during transport adversarial controls.");
        if (!sourceTransportUnchanged)
            throw new InvalidDataException("Source transport ZIP changed during transport adversarial controls.");

        var scenarios = new[] { copyDrift, extraEntry, manifestDrift };
        var refusedBeforeMaterialization = scenarios.All(x =>
            x.Rejected && !x.EvidenceMaterializationAttempted && !x.EvidenceMaterializationRootCreated);
        var passed = scenarios.All(x => x.Passed) && refusedBeforeMaterialization && sourceTransportUnchanged && mainUnchanged;

        var receipt = new RecoveryTransportAdversarialControlMatrixReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            passed ? "TRANSPORT_ADVERSARIAL_CONTROLS_PASSED" : "TRANSPORT_ADVERSARIAL_CONTROLS_FAILED",
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
            sourceArtifactPath,
            sourceArtifactSha,
            sourceTransportPath,
            sourceTransportSha,
            true,
            controlRoot,
            scenarios,
            copyDrift.Passed,
            extraEntry.Passed,
            manifestDrift.Passed,
            refusedBeforeMaterialization,
            sourceTransportUnchanged,
            authority,
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
            "v0.27 proves three isolated refusal shapes around one retained passing v0.26 transport-independence artifact: post-binding copy drift, extra ZIP entry, and transport-manifest digest drift are refused before evidence materialization. This is not producer authentication, cross-machine/cross-OS portability proof, live recovery authority, production recovery proof, a general recovery claim, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "recovery-transport-adversarial-controls");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"recovery-transport-adversarial-control-matrix-v0.27-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private async Task<RecoveryTransportAdversarialControlScenarioReceipt> RunCopyByteDriftAfterBindingAsync(
        string scenarioRoot,
        string sourceTransportPath,
        string sourceTransportSha,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scenarioRoot);
        var candidatePath = Path.Combine(scenarioRoot, "transport-copy.zip");
        File.Copy(sourceTransportPath, candidatePath, overwrite: false);

        var initial = HashFile(candidatePath);
        if (!string.Equals(initial, sourceTransportSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact transport copy did not match the bound source transport before the byte-drift control.");

        var entryName = "capsule/evidence/positive-isolated-drill.json";
        var bytes = ReadZipEntry(candidatePath, entryName);
        var drifted = new byte[bytes.Length + 2];
        Buffer.BlockCopy(bytes, 0, drifted, 0, bytes.Length);
        drifted[^2] = (byte)'\n';
        drifted[^1] = (byte)' ';
        ReplaceZipEntry(candidatePath, entryName, drifted);

        var atAttempt = HashFile(candidatePath);
        var rejected = !string.Equals(atAttempt, sourceTransportSha, StringComparison.OrdinalIgnoreCase);
        var materializationRoot = Path.Combine(scenarioRoot, "evidence-materialization");
        var preserved = File.Exists(candidatePath) && string.Equals(HashFile(candidatePath), atAttempt, StringComparison.OrdinalIgnoreCase);
        var sourceUnchanged = string.Equals(HashFile(sourceTransportPath), sourceTransportSha, StringComparison.OrdinalIgnoreCase);
        var passed = rejected && preserved && sourceUnchanged && !Directory.Exists(materializationRoot);

        return new RecoveryTransportAdversarialControlScenarioReceipt(
            "copy-byte-drift-after-binding-refused",
            passed,
            candidatePath,
            initial,
            atAttempt,
            "append JSON whitespace to one expected payload entry after the exact copied transport SHA-256 has been bound",
            false,
            rejected,
            rejected ? "Transport copy changed after SHA-256 binding; evidence inspection/materialization is refused." : "Transport copy unexpectedly retained the original bound digest.",
            false,
            Directory.Exists(materializationRoot),
            preserved,
            sourceUnchanged);
    }

    private async Task<RecoveryTransportAdversarialControlScenarioReceipt> RunExtraZipEntryControlAsync(
        string scenarioRoot,
        string sourceTransportPath,
        string sourceTransportSha,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scenarioRoot);
        var candidatePath = Path.Combine(scenarioRoot, "transport-extra-entry.zip");
        File.Copy(sourceTransportPath, candidatePath, overwrite: false);
        var initial = HashFile(candidatePath);

        AddZipEntry(candidatePath, "unexpected-control.json", Utf8NoBom.GetBytes("{\"control\":\"v0.27-extra-entry\"}\n"));
        var atAttempt = HashFile(candidatePath);
        var (rejected, message) = await ExpectInspectionRefusalAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        var materializationRoot = Path.Combine(scenarioRoot, "evidence-materialization");
        var preserved = File.Exists(candidatePath) && string.Equals(HashFile(candidatePath), atAttempt, StringComparison.OrdinalIgnoreCase);
        var sourceUnchanged = string.Equals(HashFile(sourceTransportPath), sourceTransportSha, StringComparison.OrdinalIgnoreCase);
        var passed = string.Equals(initial, sourceTransportSha, StringComparison.OrdinalIgnoreCase) &&
                     rejected && message.Contains("Unexpected", StringComparison.OrdinalIgnoreCase) &&
                     preserved && sourceUnchanged && !Directory.Exists(materializationRoot);

        return new RecoveryTransportAdversarialControlScenarioReceipt(
            "extra-zip-entry-refused",
            passed,
            candidatePath,
            initial,
            atAttempt,
            "add one unexpected ZIP entry to an otherwise exact transport copy",
            true,
            rejected,
            message,
            false,
            Directory.Exists(materializationRoot),
            preserved,
            sourceUnchanged);
    }

    private async Task<RecoveryTransportAdversarialControlScenarioReceipt> RunManifestDriftControlAsync(
        string scenarioRoot,
        string sourceTransportPath,
        string sourceTransportSha,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scenarioRoot);
        var candidatePath = Path.Combine(scenarioRoot, "transport-manifest-drift.zip");
        File.Copy(sourceTransportPath, candidatePath, overwrite: false);
        var initial = HashFile(candidatePath);

        var manifestBytes = ReadZipEntry(candidatePath, "transport-manifest.json");
        var manifest = JsonSerializer.Deserialize<RecoveryEvidenceTransportManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("Transport manifest could not be parsed for the manifest-drift control.");
        var driftedManifest = manifest with { EvidenceEnvelopeDigest = new string('0', 64) };
        var driftedBytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(driftedManifest, JsonOptions));
        ReplaceZipEntry(candidatePath, "transport-manifest.json", driftedBytes);

        var atAttempt = HashFile(candidatePath);
        var (rejected, message) = await ExpectInspectionRefusalAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        var materializationRoot = Path.Combine(scenarioRoot, "evidence-materialization");
        var preserved = File.Exists(candidatePath) && string.Equals(HashFile(candidatePath), atAttempt, StringComparison.OrdinalIgnoreCase);
        var sourceUnchanged = string.Equals(HashFile(sourceTransportPath), sourceTransportSha, StringComparison.OrdinalIgnoreCase);
        var passed = string.Equals(initial, sourceTransportSha, StringComparison.OrdinalIgnoreCase) &&
                     rejected && preserved && sourceUnchanged && !Directory.Exists(materializationRoot);

        return new RecoveryTransportAdversarialControlScenarioReceipt(
            "transport-manifest-drift-refused",
            passed,
            candidatePath,
            initial,
            atAttempt,
            "change only the declared evidence-envelope digest in a structurally valid transport manifest",
            true,
            rejected,
            message,
            false,
            Directory.Exists(materializationRoot),
            preserved,
            sourceUnchanged);
    }

    private async Task<(bool Rejected, string Message)> ExpectInspectionRefusalAsync(
        string candidatePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspection = await _transportService.InspectAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            return inspection.Verified
                ? (false, $"Transport inspection unexpectedly verified the adversarial copy: {inspection.Status}")
                : (true, inspection.Status);
        }
        catch (InvalidDataException ex)
        {
            return (true, ex.Message);
        }
    }

    private static void VerifySourceIndependenceReceipt(RecoveryEvidenceTransportIndependenceDrillReceipt receipt)
    {
        if (!string.Equals(receipt.Schema, ExpectedSourceIndependenceSchema, StringComparison.Ordinal) ||
            !string.Equals(receipt.Version, "0.26.0", StringComparison.Ordinal) ||
            !receipt.Passed ||
            !string.Equals(receipt.Status, "INDEPENDENT_LOCAL_TRANSPORT_CAPSULE_VERIFIED", StringComparison.Ordinal) ||
            !receipt.MainRepositoryUnchanged ||
            !receipt.SourceImportReceiptVerified ||
            !receipt.CopiedTransportByteIdentical ||
            !receipt.CopiedTransportSeparatedFromSourceTransportRoot ||
            !receipt.CopiedTransportInspectionVerified ||
            !receipt.ExactTransportFileSetVerified ||
            !receipt.TransportPayloadDigestsVerified ||
            !receipt.TransportManifestDigestReproduced ||
            !receipt.CapsuleManifestDigestReproduced ||
            !receipt.EvidenceEnvelopeDigestReproduced ||
            !receipt.IndependentMaterializedCopiesVerified ||
            !receipt.ReplayUsedOnlyCopiedTransportBytes ||
            receipt.OriginalEvidencePathAccessAttemptsDuringTransportReplay != 0 ||
            receipt.OriginalTransportZipRequiredAfterCopy ||
            receipt.OriginalRelocationRootRequiredForDrill ||
            receipt.OriginalReplayRootRequiredForDrill ||
            receipt.OriginalEvidenceArtifactsRequiredForDrill ||
            receipt.HistoricalAbsolutePathsDereferencedDuringTransportReplay ||
            !receipt.LocalTransportIndependenceDemonstrated ||
            receipt.ProducerAuthenticationProven ||
            receipt.CrossMachinePortabilityProven ||
            receipt.CrossOsPortabilityProven ||
            receipt.ProductionMainRepositoryRecoveryProven ||
            receipt.GeneralFailureRecoveryClaimAllowed ||
            receipt.AutomaticRecoveryAuthorized ||
            receipt.RecoveryExecutionAuthorized ||
            receipt.RollbackAuthorized ||
            receipt.DeletionAuthorized ||
            receipt.SourceMutationAuthorized ||
            receipt.BuildAuthorized ||
            receipt.CheckpointAuthorized ||
            receipt.NetworkAccessAuthorized ||
            receipt.CatalogMutationAuthorized ||
            receipt.AgentExecuteAuthorized ||
            receipt.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.26 transport-independence receipt does not preserve the required bounded contract.");
    }

    private static string FindLatestPassingIndependenceArtifact(string repositoryRoot)
    {
        var directory = Path.Combine(repositoryRoot, "artifacts", "recovery-transport-independence");
        if (!Directory.Exists(directory))
            throw new InvalidDataException("No retained v0.26 transport-independence artifact directory exists.");

        foreach (var path in Directory.GetFiles(directory, "recovery-transport-independence-v0.26-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var receipt = JsonSerializer.Deserialize<RecoveryEvidenceTransportIndependenceDrillReceipt>(bytes, JsonOptions);
                if (receipt is not null && receipt.Passed &&
                    string.Equals(receipt.Status, "INDEPENDENT_LOCAL_TRANSPORT_CAPSULE_VERIFIED", StringComparison.Ordinal))
                    return Path.GetFullPath(path);
            }
            catch
            {
                // Invalid retained evidence cannot support this matrix; continue to older artifacts.
            }
        }

        throw new InvalidDataException("No passing retained v0.26 transport-independence artifact is available.");
    }

    private static byte[] ReadZipEntry(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"Expected transport ZIP entry is missing: {entryName}");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void ReplaceZipEntry(string zipPath, string entryName, byte[] replacement)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"Expected transport ZIP entry is missing for mutation: {entryName}");
        entry.Delete();
        var created = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = created.Open();
        stream.Write(replacement, 0, replacement.Length);
    }

    private static void AddZipEntry(string zipPath, string entryName, byte[] bytes)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        if (archive.GetEntry(entryName) is not null)
            throw new InvalidDataException($"Adversarial extra ZIP entry already exists: {entryName}");
        var created = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = created.Open();
        stream.Write(bytes, 0, bytes.Length);
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

    private static void RequireAcceptedV027(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Transport adversarial controls require a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Transport adversarial controls are enabled only after {ExpectedTag} points at the current HEAD.");
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
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.27 transport adversarial controls: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path[(arrow + 4)..];
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
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in v0.27 transport adversarial controls.");

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
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.27 transport adversarial controls.");

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
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.27 transport adversarial controls.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.27 transport adversarial controls: {stderr.Trim()}");
        return stdout;
    }
}
