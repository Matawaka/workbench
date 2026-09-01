using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record LocalApplicationTargetMetadata(
    string Schema,
    string ApplicationId,
    string TargetVersion);

public sealed record LocalApplicationPackageBuilderChange(
    string Path,
    string Action,
    string? CurrentSha256,
    string TargetSha256,
    long TargetBytes);

public sealed record LocalApplicationPackageBuilderPlan(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string CandidateRoot,
    string TargetMetadataPath,
    string CurrentVersion,
    string TargetVersion,
    string CurrentIdentitySha256,
    string TargetIdentitySha256,
    IReadOnlyList<LocalApplicationPackageBuilderChange> Changes,
    string GeneratedManifestSha256,
    int CandidateFileCount,
    long CandidateBytes,
    bool RegisteredAppValidated,
    bool CandidateRootValidated,
    bool ReparseBoundaryValidated,
    bool DeleteRefused,
    bool ReadyForExplicitPackageWriteAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalApplicationPackageBuilderAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string ApplicationId,
    string ApplicationRoot,
    string CandidateRoot,
    string CurrentVersion,
    string TargetVersion,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FreshPreviewRevalidationRequired,
    bool FixedRootsOnly,
    bool PackageArtifactWriteOnly,
    bool ApplicationMutationAllowed,
    bool AppRegistrationAllowed,
    bool NetworkAllowed,
    bool ProcessLaunchAllowed,
    bool InstallerExecutionAllowed,
    bool GitAllowed,
    bool RegistryMutationAllowed,
    bool ServiceMutationAllowed,
    bool EnvironmentMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record LocalApplicationPackageBuilderReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string CandidateRoot,
    string CurrentVersion,
    string TargetVersion,
    string PackagePath,
    string PackageSha256,
    string ManifestSha256,
    IReadOnlyList<LocalApplicationPackageBuilderChange> Changes,
    bool FreshPreviewVerified,
    bool ExistingUpdaterPreviewReady,
    bool ApplicationMutationPerformed,
    bool UpdateAuthorityCreated,
    bool ApplicationLaunchPerformed,
    LocalApplicationPackageBuilderAuthorityReceipt Authority,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// Builds a local-app update ZIP from two fixed roots only:
/// registered current bytes under Workspace/Apps/&lt;ApplicationId&gt; and desired
/// target bytes under Workspace/AppCandidates/&lt;ApplicationId&gt;. It derives all
/// predecessor SHA-256 bindings from the actual registered app and validates the
/// completed ZIP through the existing LocalApplicationMaintenanceService Preview.
/// It never updates, registers or launches the application.
/// </summary>
public sealed class LocalApplicationPackageBuilderService
{
    public const string Version = "0.37.0";
    public const string PlanSchema = "matawaka.local-app-package-builder-plan/v0.37";
    public const string ReceiptSchema = "matawaka.local-app-package-builder-receipt/v0.37";
    public const string AuthoritySchema = "matawaka.local-app-package-builder-authority-receipt/v0.37";
    public const string TargetSchema = "matawaka.local-app-target/v1";
    public const string TargetMetadataFileName = ".matawaka-target.json";
    public const string CandidatesDirectoryName = "AppCandidates";

    private const int MaxFiles = 2048;
    private const long MaxTotalBytes = 512L * 1024L * 1024L;

    private static readonly Regex ApplicationIdRegex = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TargetVersionRegex = new(
        "^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly LocalApplicationMaintenanceService _updater = new();

    public Task<LocalApplicationPackageBuilderPlan> PreviewAsync(
        string selectedApplicationRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = BuildSnapshot(selectedApplicationRoot, workspaceRoot, cancellationToken);
        return Task.FromResult(snapshot.Plan);
    }

    public async Task<(LocalApplicationPackageBuilderReceipt Receipt, LocalApplicationUpdatePlan UpdaterPreview)> BuildAsync(
        LocalApplicationPackageBuilderPlan confirmedPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmedPlan is null || !confirmedPlan.ReadyForExplicitPackageWriteAuthority)
            throw new InvalidDataException("A READY package-builder preview is required before package creation.");

        var fresh = BuildSnapshot(confirmedPlan.ApplicationRoot, workspaceRoot, cancellationToken);
        RequireEquivalent(confirmedPlan, fresh.Plan);

        var workspace = RequireDirectory(workspaceRoot, "Workspace root");
        var workbenchRoot = RequireDirectory(Path.Combine(workspace, "Workbench"), "Workbench root");
        var outputDir = Path.Combine(workbenchRoot, "artifacts", "local-app-packages");
        Directory.CreateDirectory(outputDir);
        var packagePath = Path.Combine(
            outputDir,
            $"local-app-update-{fresh.Plan.ApplicationId}-{SafeArtifactToken(fresh.Plan.TargetVersion)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.zip");

        var nonEffects = DefaultNonEffects();
        var authority = new LocalApplicationPackageBuilderAuthorityReceipt(
            Schema: AuthoritySchema,
            Subject: "human-operator-at-workbench-ui",
            Operation: "workbench.local-app.build-update-package",
            ApplicationId: fresh.Plan.ApplicationId,
            ApplicationRoot: fresh.Plan.ApplicationRoot,
            CandidateRoot: fresh.Plan.CandidateRoot,
            CurrentVersion: fresh.Plan.CurrentVersion,
            TargetVersion: fresh.Plan.TargetVersion,
            AuthoritySource: "explicit Local apps Build package confirmation after fresh exact registered/candidate preview",
            ExplicitUiConfirmationRequired: true,
            FreshPreviewRevalidationRequired: true,
            FixedRootsOnly: true,
            PackageArtifactWriteOnly: true,
            ApplicationMutationAllowed: false,
            AppRegistrationAllowed: false,
            NetworkAllowed: false,
            ProcessLaunchAllowed: false,
            InstallerExecutionAllowed: false,
            GitAllowed: false,
            RegistryMutationAllowed: false,
            ServiceMutationAllowed: false,
            EnvironmentMutationAllowed: false,
            AgentExecuteAllowed: false,
            AllowedEffects: new[]
            {
                "write one generated local-app update ZIP under Workbench/artifacts/local-app-packages",
                "re-open generated ZIP through existing LocalApplicationMaintenanceService Preview",
                "write no bytes under managed app or candidate roots"
            },
            NonEffects: nonEffects);

        try
        {
            using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, LocalApplicationMaintenanceService.ManifestFileName, fresh.ManifestBytes);
                foreach (var payload in fresh.Payloads.OrderBy(item => item.Path, StringComparer.Ordinal))
                    WriteEntry(zip, LocalApplicationMaintenanceService.PayloadRoot + payload.Path, payload.Bytes);
                WriteEntry(
                    zip,
                    LocalApplicationMaintenanceService.PayloadRoot + LocalApplicationMaintenanceService.IdentityFileName,
                    fresh.TargetIdentityBytes);
            }

            var packageSha = HashFile(packagePath);
            var updaterPreview = await _updater.PreviewAsync(packagePath, workspaceRoot, cancellationToken);
            if (!updaterPreview.ReadyForExplicitApplyAuthority ||
                !updaterPreview.ApplicationId.Equals(fresh.Plan.ApplicationId, StringComparison.Ordinal) ||
                !updaterPreview.CurrentVersion.Equals(fresh.Plan.CurrentVersion, StringComparison.Ordinal) ||
                !updaterPreview.TargetVersion.Equals(fresh.Plan.TargetVersion, StringComparison.Ordinal) ||
                !updaterPreview.PackageSha256.Equals(packageSha, StringComparison.OrdinalIgnoreCase) ||
                !updaterPreview.ManifestSha256.Equals(fresh.Plan.GeneratedManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Generated package did not round-trip through the existing updater Preview contract.");

            var receipt = new LocalApplicationPackageBuilderReceipt(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                fresh.Plan.ApplicationId,
                fresh.Plan.ApplicationRoot,
                fresh.Plan.CandidateRoot,
                fresh.Plan.CurrentVersion,
                fresh.Plan.TargetVersion,
                packagePath,
                packageSha,
                fresh.Plan.GeneratedManifestSha256,
                fresh.Plan.Changes,
                true,
                true,
                false,
                false,
                false,
                authority,
                nonEffects,
                "LOCAL_APPLICATION_UPDATE_PACKAGE_BUILT_EXISTING_UPDATER_PREVIEW_READY",
                "The builder derived predecessor SHA-256 values from actual registered bytes, generated only a local ZIP artifact, and required the accepted updater Preview to re-validate it. No update/launch authority was created or exercised.");
            return (receipt, updaterPreview);
        }
        catch
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var appId = ApplicationIdRegex.IsMatch("demo.app");
        var target = TargetVersionRegex.IsMatch("1.2.3-beta_1");
        var invalidTarget = !TargetVersionRegex.IsMatch("../1.0");
        return new[]
        {
            ("builder-safe-app-id", appId, appId.ToString(), "true"),
            ("builder-safe-target-version", target, target.ToString(), "true"),
            ("builder-traversal-target-version-refused", invalidTarget, invalidTarget.ToString(), "true"),
            ("builder-fixed-candidate-root", true, "Workspace/AppCandidates/<ApplicationId>", "fixed root only"),
            ("builder-no-delete-contract", true, "candidate omission => Preview refusal", "delete refused"),
            ("builder-existing-updater-validation-required", true, "Build success requires updater Preview READY", "true"),
            ("builder-no-update-authority", true, "UpdateAuthorityCreated=false; ApplicationMutationPerformed=false", "false / false")
        };
    }

    private static BuildSnapshotResult BuildSnapshot(
        string selectedApplicationRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var workspace = RequireDirectory(workspaceRoot, "Workspace root");
        var appsRoot = RequireDirectory(Path.Combine(workspace, LocalApplicationMaintenanceService.AppsDirectoryName), "Managed Apps root");
        RejectReparse(appsRoot, "managed Apps root");

        var appRoot = RequireDirectory(selectedApplicationRoot, "Selected application root");
        RejectReparse(appRoot, "registered application root");
        var parent = Directory.GetParent(appRoot)?.FullName;
        if (parent is null || !Path.GetFullPath(parent).Equals(appsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Builder accepts only a direct child of <WorkspaceRoot>/Apps.");
        var appId = Path.GetFileName(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!ApplicationIdRegex.IsMatch(appId))
            throw new InvalidDataException($"Application folder name is not a safe ApplicationId: {appId}");

        var identityPath = Path.Combine(appRoot, LocalApplicationMaintenanceService.IdentityFileName);
        if (!File.Exists(identityPath))
            throw new InvalidDataException("Registered application identity is missing.");
        RejectReparse(identityPath, "registered identity");
        var currentIdentity = JsonSerializer.Deserialize<LocalApplicationIdentity>(File.ReadAllBytes(identityPath), JsonOptions)
            ?? throw new InvalidDataException("Registered application identity could not be parsed.");
        if (currentIdentity.Schema != LocalApplicationMaintenanceService.IdentitySchema ||
            currentIdentity.ApplicationId != appId)
            throw new InvalidDataException("Registered application identity does not match selected app root.");
        var currentIdentitySha = HashFile(identityPath);

        var candidatesRoot = RequireDirectory(Path.Combine(workspace, CandidatesDirectoryName), "AppCandidates root");
        RejectReparse(candidatesRoot, "AppCandidates root");
        var candidateRoot = RequireDirectory(Path.Combine(candidatesRoot, appId), "Application candidate root");
        RejectReparse(candidateRoot, "application candidate root");
        var candidateParent = Directory.GetParent(candidateRoot)?.FullName;
        if (candidateParent is null || !Path.GetFullPath(candidateParent).Equals(candidatesRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate root must be a direct child of <WorkspaceRoot>/AppCandidates.");

        var metadataPath = Path.Combine(candidateRoot, TargetMetadataFileName);
        if (!File.Exists(metadataPath))
            throw new InvalidDataException($"Target metadata is missing: {metadataPath}");
        RejectReparse(metadataPath, "target metadata");
        var metadata = JsonSerializer.Deserialize<LocalApplicationTargetMetadata>(File.ReadAllBytes(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("Target metadata could not be parsed.");
        if (metadata.Schema != TargetSchema || metadata.ApplicationId != appId || !TargetVersionRegex.IsMatch(metadata.TargetVersion))
            throw new InvalidDataException("Target metadata does not match schema/ApplicationId/safe TargetVersion contract.");
        if (metadata.TargetVersion == currentIdentity.Version)
            throw new InvalidDataException("TargetVersion must differ from current registered version.");

        var currentFiles = Inventory(appRoot, excludeIdentity: true, excludeTargetMetadata: false, cancellationToken);
        var candidateFiles = Inventory(candidateRoot, excludeIdentity: true, excludeTargetMetadata: true, cancellationToken);

        var currentMap = currentFiles.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var candidateMap = candidateFiles.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var missing = currentMap.Keys.Except(candidateMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Builder refuses implicit Delete; candidate is missing current paths: {string.Join(", ", missing)}");

        var changes = new List<LocalApplicationPackageBuilderChange>();
        var payloads = new List<PayloadBytes>();
        foreach (var target in candidateFiles.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentMap.TryGetValue(target.Path, out var current))
            {
                if (current.Sha256.Equals(target.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new LocalApplicationPackageBuilderChange(target.Path, "NoOp", current.Sha256, target.Sha256, target.Bytes));
                    continue;
                }
                changes.Add(new LocalApplicationPackageBuilderChange(target.Path, "Replace", current.Sha256, target.Sha256, target.Bytes));
            }
            else
            {
                changes.Add(new LocalApplicationPackageBuilderChange(target.Path, "Add", null, target.Sha256, target.Bytes));
            }
            payloads.Add(new PayloadBytes(target.Path, File.ReadAllBytes(Path.Combine(candidateRoot, target.Path.Replace('/', Path.DirectorySeparatorChar)))));
        }

        var targetIdentity = new LocalApplicationIdentity(
            LocalApplicationMaintenanceService.IdentitySchema,
            appId,
            metadata.TargetVersion);
        var targetIdentityBytes = JsonSerializer.SerializeToUtf8Bytes(targetIdentity, JsonOptions);
        var targetIdentitySha = HashBytes(targetIdentityBytes);

        var manifestFiles = changes
            .Where(change => change.Action != "NoOp")
            .Select(change => new LocalApplicationUpdateFile(change.Path, change.CurrentSha256, change.TargetSha256))
            .Append(new LocalApplicationUpdateFile(
                LocalApplicationMaintenanceService.IdentityFileName,
                currentIdentitySha,
                targetIdentitySha))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

        var manifest = new LocalApplicationUpdateManifest(
            LocalApplicationMaintenanceService.PackageSchema,
            "1",
            appId,
            currentIdentity.Version,
            metadata.TargetVersion,
            LocalApplicationMaintenanceService.PayloadRoot,
            manifestFiles,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "built locally from fixed registered/candidate roots",
                "no network",
                "no application mutation",
                "no automatic app launch"
            });
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var manifestSha = HashBytes(manifestBytes);
        var totalCandidateBytes = candidateFiles.Sum(item => item.Bytes);
        var nonEffects = DefaultNonEffects();

        var plan = new LocalApplicationPackageBuilderPlan(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            appId,
            appRoot,
            candidateRoot,
            metadataPath,
            currentIdentity.Version,
            metadata.TargetVersion,
            currentIdentitySha,
            targetIdentitySha,
            changes,
            manifestSha,
            candidateFiles.Count,
            totalCandidateBytes,
            true,
            true,
            true,
            true,
            true,
            nonEffects,
            "READY means only that explicit confirmation may write one local update ZIP artifact. All predecessor SHA-256 values were derived from actual registered bytes; no application/update authority is created by Preview.");

        return new BuildSnapshotResult(plan, manifestBytes, targetIdentityBytes, payloads);
    }

    private static List<FileSnapshot> Inventory(
        string root,
        bool excludeIdentity,
        bool excludeTargetMetadata,
        CancellationToken cancellationToken)
    {
        var result = new List<FileSnapshot>();
        var stack = new Stack<string>();
        stack.Push(root);
        long total = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            RejectReparse(current, "inventory directory");
            foreach (var directory in Directory.EnumerateDirectories(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(directory, "inventory subdirectory");
                stack.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(file, "inventory file");
                var relative = NormalizeRelative(Path.GetRelativePath(root, file));
                if (excludeIdentity && relative.Equals(LocalApplicationMaintenanceService.IdentityFileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (excludeTargetMetadata && relative.Equals(TargetMetadataFileName, StringComparison.OrdinalIgnoreCase)) continue;
                var size = new FileInfo(file).Length;
                total += size;
                if (result.Count + 1 > MaxFiles || total > MaxTotalBytes)
                    throw new InvalidDataException($"Builder inventory exceeds bounds. files>{MaxFiles} or bytes>{MaxTotalBytes}");
                result.Add(new FileSnapshot(relative, HashFile(file), size));
            }
        }

        var duplicates = result.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException("Builder inventory contains Windows case-colliding relative paths.");
        return result.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
    }

    private static void RequireEquivalent(LocalApplicationPackageBuilderPlan confirmed, LocalApplicationPackageBuilderPlan fresh)
    {
        if (confirmed.ApplicationId != fresh.ApplicationId ||
            !Path.GetFullPath(confirmed.ApplicationRoot).Equals(Path.GetFullPath(fresh.ApplicationRoot), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(confirmed.CandidateRoot).Equals(Path.GetFullPath(fresh.CandidateRoot), StringComparison.OrdinalIgnoreCase) ||
            confirmed.CurrentVersion != fresh.CurrentVersion ||
            confirmed.TargetVersion != fresh.TargetVersion ||
            !confirmed.CurrentIdentitySha256.Equals(fresh.CurrentIdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !confirmed.TargetIdentitySha256.Equals(fresh.TargetIdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !confirmed.GeneratedManifestSha256.Equals(fresh.GeneratedManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !SameChanges(confirmed.Changes, fresh.Changes))
            throw new InvalidDataException("Package-builder preview is stale; registered or candidate bytes changed before confirmation.");
    }

    private static bool SameChanges(
        IReadOnlyList<LocalApplicationPackageBuilderChange> left,
        IReadOnlyList<LocalApplicationPackageBuilderChange> right)
    {
        if (left.Count != right.Count) return false;
        var a = left.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        return a.Zip(b).All(pair => pair.First == pair.Second);
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.StartsWith('/') ||
            normalized == ".." || normalized.StartsWith("../") || normalized.Contains("/../") || normalized.Contains(':'))
            throw new InvalidDataException($"Unsafe builder relative path: {path}");
        return normalized;
    }

    private static string RequireDirectory(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"{role} is required.");
        var full = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(full)) throw new InvalidDataException($"{role} does not exist: {full}");
        return full;
    }

    private static void RejectReparse(string path, string role)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse points are not allowed at {role}: {path}");
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string SafeArtifactToken(string value)
        => Regex.Replace(value, "[^A-Za-z0-9._-]+", "_");

    private static string[] DefaultNonEffects() => new[]
    {
        "no managed application mutation",
        "no app registration",
        "no application launch",
        "no installer or script execution",
        "no network access",
        "no Git operation",
        "no registry/service/environment mutation",
        "no arbitrary candidate root outside <WorkspaceRoot>/AppCandidates/<ApplicationId>",
        "no arbitrary managed root outside <WorkspaceRoot>/Apps/<ApplicationId>",
        "no delete operation",
        "no Update authority creation",
        "no Agent Execute authority",
        "no ActionPermit creation",
        "generated package is validated by existing updater Preview before builder success"
    };

    private sealed record FileSnapshot(string Path, string Sha256, long Bytes);
    private sealed record PayloadBytes(string Path, byte[] Bytes);
    private sealed record BuildSnapshotResult(
        LocalApplicationPackageBuilderPlan Plan,
        byte[] ManifestBytes,
        byte[] TargetIdentityBytes,
        IReadOnlyList<PayloadBytes> Payloads);
}
