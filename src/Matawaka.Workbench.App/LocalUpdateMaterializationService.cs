using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchUpdateMaterializationAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string TargetRepository,
    string PackageSha256,
    string TargetVersion,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool PackageRevalidationRequired,
    bool PredecessorRevalidationRequired,
    bool WorkingTreeCleanRequired,
    bool StagingOnly,
    bool RepositorySourceMutationAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record WorkbenchUpdateMaterializationReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PackageFileName,
    string PackageSha256,
    string TargetVersion,
    string TargetTag,
    string PredecessorTag,
    string PredecessorCommit,
    string CurrentHead,
    string StagingRoot,
    int PayloadFileCount,
    long PayloadBytes,
    IReadOnlyList<WorkbenchUpdateManifestFile> PayloadFiles,
    WorkbenchUpdateMaterializationAuthorityReceipt Authority,
    bool PackageDigestReverified,
    bool PredecessorReverified,
    bool WorkingTreeCleanBeforeMaterialization,
    bool PayloadDigestsReverifiedAfterWrite,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.13 consumes a READY bounded update plan only after explicit UI confirmation.
/// The only material effect is bounded payload-byte creation under
/// Workbench/.workbench/update-materializations. It does not overwrite tracked
/// source, build, execute installers, commit, tag, fetch, push, use the network,
/// mutate catalog repositories, or grant Agent Execute authority.
/// </summary>
public sealed class LocalUpdateMaterializationService
{
    public const string ReceiptSchema = "matawaka.workbench-update-materialization-receipt/v0.13";
    public const string AuthoritySchema = "matawaka.workbench-update-materialization-authority-receipt/v0.13";
    public const string Version = "0.13.0";

    private readonly LocalUpdateIntakeService _intakeService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public LocalUpdateMaterializationService(LocalUpdateIntakeService intakeService)
    {
        _intakeService = intakeService ?? throw new ArgumentNullException(nameof(intakeService));
    }

    public async Task<(WorkbenchUpdateMaterializationReceipt Receipt, string ArtifactPath)> MaterializeAsync(
        string packagePath,
        WorkbenchUpdatePlanReceipt authorizedPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (authorizedPlan is null)
            throw new InvalidDataException("A validated update plan is required before materialization.");
        if (!string.Equals(authorizedPlan.Status, "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY", StringComparison.Ordinal) ||
            !authorizedPlan.PackageStructureValidated ||
            !authorizedPlan.PayloadDigestsValidated ||
            !authorizedPlan.PredecessorTagMatched ||
            !authorizedPlan.PredecessorCommitMatched)
            throw new InvalidDataException("The update plan is not eligible for a separate materialization authority decision.");
        if (authorizedPlan.MaterializationAuthorized || authorizedPlan.BuildAuthorized || authorizedPlan.CheckpointAuthorized)
            throw new InvalidDataException("The intake plan must remain non-authorizing; materialization authority is created only by the explicit UI gate.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new InvalidDataException("The planned update package is no longer available.");

        var packageSha = HashFile(packagePath);
        if (!string.Equals(packageSha, authorizedPlan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update package changed after planning. Re-plan before materialization.");

        // Re-run the bounded intake verifier against the exact same package and
        // current predecessor. This intentionally creates a fresh read-only plan
        // receipt and prevents a stale plan from authorizing changed bytes.
        var replanned = await _intakeService.PlanAsync(packagePath, workspaceRoot, cancellationToken);
        VerifyEquivalentPlan(authorizedPlan, replanned.Receipt);

        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!string.Equals(currentHead, authorizedPlan.PredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after planning. Re-plan before materialization.");

        var currentTags = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"))
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (!currentTags.Contains(authorizedPlan.PredecessorTag, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench predecessor tag changed after planning. Re-plan before materialization.");

        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(status))
            throw new InvalidDataException("Workbench tracked working tree must be clean before staging materialization.");

        var stagingParent = Path.Combine(repositoryRoot, ".workbench", "update-materializations");
        Directory.CreateDirectory(stagingParent);
        var stagingRoot = Path.Combine(stagingParent, $"{packageSha[..16]}-{DateTime.Now:yyyyMMdd-HHmmssfff}");
        var payloadRoot = Path.Combine(stagingRoot, "payload");
        Directory.CreateDirectory(payloadRoot);

        var expected = authorizedPlan.PayloadFiles.ToDictionary(item => NormalizeRelativePath(item.Path), item => item.Sha256, StringComparer.Ordinal);
        var written = new List<WorkbenchUpdateManifestFile>();
        long writtenBytes = 0;

        try
        {
            using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lockedPackageSha = Convert.ToHexString(SHA256.HashData(packageStream)).ToLowerInvariant();
            if (!string.Equals(lockedPackageSha, packageSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package changed between re-plan and materialization. Re-plan before materialization.");
            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var item in authorizedPlan.PayloadFiles.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeRelativePath(item.Path);
                var entryName = "payload/" + relative;
                var entry = archive.Entries.SingleOrDefault(candidate => string.Equals(candidate.FullName, entryName, StringComparison.Ordinal));
                if (entry is null || string.IsNullOrEmpty(entry.Name))
                    throw new InvalidDataException($"Planned payload entry is missing during materialization: {relative}");

                var destination = ResolveStagingDestination(payloadRoot, relative);
                var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Unable to resolve staging parent directory.");
                Directory.CreateDirectory(parent);

                await using (var input = entry.Open())
                await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await input.CopyToAsync(output, cancellationToken);

                var actualSha = HashFile(destination);
                if (!expected.TryGetValue(relative, out var expectedSha) ||
                    !string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Materialized payload SHA-256 mismatch: {relative}");

                var length = new FileInfo(destination).Length;
                writtenBytes = checked(writtenBytes + length);
                written.Add(new WorkbenchUpdateManifestFile(relative, actualSha));
            }

            if (written.Count != authorizedPlan.PayloadFileCount || writtenBytes != authorizedPlan.PayloadBytes)
                throw new InvalidDataException("Materialized payload count/size differs from the validated update plan.");

            var nonEffects = new[]
            {
                "no tracked Workbench source overwrite",
                "no dotnet restore/build/publish",
                "no installer or arbitrary process execution",
                "no git add/commit/tag",
                "no git fetch or push",
                "no remote change",
                "no catalog repository mutation",
                "no network access",
                "no agent Execute authority",
                "materialization receipt does not authorize build, source apply, checkpoint, or publication"
            };

            var authority = new WorkbenchUpdateMaterializationAuthorityReceipt(
                AuthoritySchema,
                "human-operator-at-workbench-ui",
                "workbench.update.materialize-staging",
                repositoryRoot,
                packageSha,
                authorizedPlan.TargetVersion,
                "explicit Materialize button + Yes confirmation after a READY bounded update plan",
                true,
                true,
                true,
                true,
                true,
                false,
                false,
                false,
                false,
                false,
                false,
                new[]
                {
                    "create bounded payload files under Workbench/.workbench/update-materializations",
                    "write a Workbench-local materialization receipt under Workbench/artifacts/update-materializations"
                },
                nonEffects);

            var receipt = new WorkbenchUpdateMaterializationReceipt(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                Path.GetFileName(packagePath),
                packageSha,
                authorizedPlan.TargetVersion,
                authorizedPlan.TargetTag,
                authorizedPlan.PredecessorTag,
                authorizedPlan.PredecessorCommit,
                currentHead,
                stagingRoot,
                written.Count,
                writtenBytes,
                written,
                authority,
                true,
                true,
                true,
                true,
                "MATERIALIZED_STAGING_ONLY",
                nonEffects,
                "v0.13 materialization is a reversible staging effect only and carries its predecessor tag explicitly. The validated bytes are copied to an ignored Workbench-local staging area after explicit human confirmation. No build, tracked-source apply, Git checkpoint, network, catalog mutation, or Agent Execute authority is inferred.");

            var artifactDir = Path.Combine(repositoryRoot, "artifacts", "update-materializations");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"materialization-v0.13-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
            return (receipt, artifactPath);
        }
        catch
        {
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch { /* A failed cleanup must not hide the original fail-closed error. */ }
            }
            throw;
        }
    }

    private static void VerifyEquivalentPlan(WorkbenchUpdatePlanReceipt expected, WorkbenchUpdatePlanReceipt observed)
    {
        if (!string.Equals(observed.PackageSha256, expected.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.TargetVersion, expected.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(observed.TargetTag, expected.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(observed.PredecessorTag, expected.PredecessorTag, StringComparison.Ordinal) ||
            !string.Equals(observed.PredecessorCommit, expected.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Status, "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY", StringComparison.Ordinal))
            throw new InvalidDataException("Fresh package re-plan differs from the plan that received UI confirmation.");

        var expectedFiles = expected.PayloadFiles.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var observedFiles = observed.PayloadFiles.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        if (expectedFiles.Length != observedFiles.Length)
            throw new InvalidDataException("Fresh package re-plan payload count differs from the confirmed plan.");

        for (var i = 0; i < expectedFiles.Length; i++)
        {
            if (!string.Equals(expectedFiles[i].Path, observedFiles[i].Path, StringComparison.Ordinal) ||
                !string.Equals(expectedFiles[i].Sha256, observedFiles[i].Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Fresh package re-plan payload differs from the confirmed plan: {expectedFiles[i].Path}");
        }
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

    private static string ResolveStagingDestination(string payloadRoot, string relativePath)
    {
        var root = Path.GetFullPath(payloadRoot) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(payloadRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Materialization path escapes staging root: {relativePath}");
        return destination;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Empty payload path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Contains(':') || normalized.Contains('\0'))
            throw new InvalidDataException($"Unsafe payload path: {path}");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"Payload path traversal rejected: {path}");
        return string.Join('/', parts);
    }

    private static async Task<string> RunGitReadOnlyAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only git operation failed: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
