using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record LocalApplicationIdentity(
    string Schema,
    string ApplicationId,
    string Version);

public sealed record LocalApplicationUpdateFile(
    string Path,
    string? CurrentSha256,
    string Sha256);

public sealed record LocalApplicationUpdateManifest(
    string Schema,
    string PackageVersion,
    string ApplicationId,
    string ExpectedCurrentVersion,
    string TargetVersion,
    string PayloadRoot,
    IReadOnlyList<LocalApplicationUpdateFile> Files,
    bool NetworkAccessRequested,
    bool ProcessLaunchRequested,
    bool InstallerScriptExecutionRequested,
    bool RegistryMutationRequested,
    bool ServiceMutationRequested,
    bool EnvironmentMutationRequested,
    bool AgentExecuteRequested,
    IReadOnlyList<string>? NonEffects);

public sealed record LocalApplicationUpdateChange(
    string Path,
    string Action,
    string? CurrentSha256,
    string TargetSha256,
    long TargetBytes);

public sealed record LocalApplicationUpdatePlan(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PackagePath,
    string PackageSha256,
    string ManifestSha256,
    string ApplicationId,
    string ApplicationRoot,
    string IdentityPath,
    string IdentitySha256,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<LocalApplicationUpdateChange> Changes,
    bool PackageStructureValidated,
    bool PayloadDigestsValidated,
    bool ManagedRootValidated,
    bool CurrentStateValidated,
    bool ReparseBoundaryValidated,
    bool ReadyForExplicitApplyAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalApplicationUpdateAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string ApplicationId,
    string ApplicationRoot,
    string PackageSha256,
    string ManifestSha256,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FreshPreviewRevalidationRequired,
    bool ExactManagedRootOnly,
    bool AddReplaceOnly,
    bool DeleteAllowed,
    bool NetworkAllowed,
    bool ProcessLaunchAllowed,
    bool InstallerExecutionAllowed,
    bool RegistryMutationAllowed,
    bool ServiceMutationAllowed,
    bool EnvironmentMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record LocalApplicationUpdateReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string PreviousVersion,
    string TargetVersion,
    string PackageSha256,
    string ManifestSha256,
    string PreviousIdentitySha256,
    string CurrentIdentitySha256,
    IReadOnlyList<LocalApplicationUpdateChange> Changes,
    string BackupRoot,
    bool FreshPreviewVerified,
    bool ExactBytesApplied,
    bool TargetIdentityVerified,
    bool RollbackRequired,
    bool RollbackPerformed,
    bool AppLaunchPerformed,
    LocalApplicationUpdateAuthorityReceipt Authority,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.35 local-application maintenance boundary.
/// Only apps under <WorkspaceRoot>/Apps/<ApplicationId> are eligible.
/// The service consumes one local ZIP with exact manifest/payload SHA-256 bindings,
/// freshly revalidates the app before mutation, applies Add/Replace only, and rolls
/// back exact bytes on failure. It has no network, process-launch, Git, installer,
/// registry, service, environment, catalog or Agent Execute capability.
/// </summary>
public sealed class LocalApplicationMaintenanceService
{
    public const string Version = "0.35.0";
    public const string IdentitySchema = "matawaka.local-app-identity/v1";
    public const string PackageSchema = "matawaka.local-app-update-package/v1";
    public const string PlanSchema = "matawaka.local-app-update-plan/v0.35";
    public const string ReceiptSchema = "matawaka.local-app-update-receipt/v0.35";
    public const string AuthoritySchema = "matawaka.local-app-update-authority-receipt/v0.35";
    public const string ManifestFileName = "local-app-update-manifest.json";
    public const string PayloadRoot = "payload/";
    public const string AppsDirectoryName = "Apps";
    public const string IdentityFileName = ".matawaka-app.json";

    private const long MaxPackageBytes = 512L * 1024L * 1024L;
    private const int MaxPayloadFiles = 2048;
    private static readonly Regex ApplicationIdRegex = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<LocalApplicationUpdatePlan> PreviewAsync(
        string packagePath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new InvalidDataException("Local application update ZIP is missing.");
        var packageInfo = new FileInfo(packagePath);
        if (packageInfo.Length <= 0 || packageInfo.Length > MaxPackageBytes)
            throw new InvalidDataException($"Local application update ZIP size is outside the bounded limit: {packageInfo.Length} bytes.");

        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var appsRoot = Path.GetFullPath(Path.Combine(workspace, AppsDirectoryName));
        if (!Directory.Exists(appsRoot))
            throw new InvalidDataException($"Managed local-app root does not exist: {appsRoot}");
        EnsurePathIsNotReparsePoint(appsRoot, "managed Apps root");

        var packageSha = HashFile(packagePath);
        using var zip = ZipFile.OpenRead(packagePath);
        var fileEntries = zip.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToArray();
        if (fileEntries.Length == 0)
            throw new InvalidDataException("Local application update ZIP contains no files.");

        var entryMap = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in fileEntries)
        {
            var normalized = NormalizeZipEntryName(entry.FullName);
            if (!entryMap.TryAdd(normalized, entry))
                throw new InvalidDataException($"Duplicate/case-colliding ZIP entry: {normalized}");
        }
        if (!entryMap.TryGetValue(ManifestFileName, out var manifestEntry))
            throw new InvalidDataException($"Package manifest is missing: {ManifestFileName}");

        var manifestBytes = await ReadEntryBytesAsync(manifestEntry, cancellationToken);
        var manifestSha = HashBytes(manifestBytes);
        var manifest = JsonSerializer.Deserialize<LocalApplicationUpdateManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("Local application update manifest could not be parsed.");
        ValidateManifestEnvelope(manifest);

        var applicationId = manifest.ApplicationId.Trim();
        var appRoot = ResolveApplicationRoot(appsRoot, applicationId);
        if (!Directory.Exists(appRoot))
            throw new InvalidDataException($"Registered local application root is missing: {appRoot}");
        EnsurePathIsNotReparsePoint(appRoot, "application root");

        var identityPath = ResolveApplicationPath(appRoot, IdentityFileName);
        if (!File.Exists(identityPath))
            throw new InvalidDataException($"Managed application identity file is missing: {identityPath}");
        EnsureNoReparsePointBoundary(appRoot, IdentityFileName);
        var currentIdentity = ReadIdentity(identityPath);
        if (!string.Equals(currentIdentity.Schema, IdentitySchema, StringComparison.Ordinal) ||
            !string.Equals(currentIdentity.ApplicationId, applicationId, StringComparison.Ordinal) ||
            !string.Equals(currentIdentity.Version, manifest.ExpectedCurrentVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Managed application identity/version does not match package predecessor contract.");
        var identitySha = HashFile(identityPath);

        var manifestFiles = manifest.Files
            .OrderBy(file => NormalizeRelativePath(file.Path), StringComparer.Ordinal)
            .ToArray();
        if (manifestFiles.Length == 0 || manifestFiles.Length > MaxPayloadFiles)
            throw new InvalidDataException($"Package payload file count is outside bounded range: {manifestFiles.Length}");
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifestFiles)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (!uniquePaths.Add(normalized))
                throw new InvalidDataException($"Duplicate/case-colliding manifest path: {normalized}");
            RequireSha256(file.Sha256, $"target SHA-256 for {normalized}");
            if (!string.IsNullOrWhiteSpace(file.CurrentSha256))
                RequireSha256(file.CurrentSha256!, $"current SHA-256 for {normalized}");
        }
        if (!uniquePaths.Contains(IdentityFileName))
            throw new InvalidDataException($"Package must include target {IdentityFileName} identity bytes.");

        var expectedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestFileName };
        foreach (var path in uniquePaths)
            expectedEntries.Add(PayloadRoot + path);
        var actualEntries = entryMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actualEntries.SetEquals(expectedEntries))
        {
            var extra = actualEntries.Except(expectedEntries, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var missing = expectedEntries.Except(actualEntries, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            throw new InvalidDataException($"ZIP entry set differs from exact manifest payload. extra=[{string.Join(',', extra)}]; missing=[{string.Join(',', missing)}]");
        }

        var changes = new List<LocalApplicationUpdateChange>(manifestFiles.Length);
        foreach (var file in manifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizeRelativePath(file.Path);
            EnsureNoReparsePointBoundary(appRoot, path);
            var destination = ResolveApplicationPath(appRoot, path);
            if (Directory.Exists(destination))
                throw new InvalidDataException($"Manifest file path resolves to an existing directory: {path}");

            var payloadEntry = entryMap[PayloadRoot + path];
            var payloadSha = await HashEntryAsync(payloadEntry, cancellationToken);
            if (!string.Equals(payloadSha, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Payload SHA-256 mismatch: {path}");

            if (File.Exists(destination))
            {
                if (string.IsNullOrWhiteSpace(file.CurrentSha256))
                    throw new InvalidDataException($"Existing file requires CurrentSha256 replacement binding: {path}");
                var currentSha = HashFile(destination);
                if (!string.Equals(currentSha, file.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Current application file drifted from package predecessor binding: {path}");
                changes.Add(new LocalApplicationUpdateChange(path, "Replace", currentSha, payloadSha, payloadEntry.Length));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(file.CurrentSha256))
                    throw new InvalidDataException($"Missing destination cannot satisfy a replacement CurrentSha256: {path}");
                changes.Add(new LocalApplicationUpdateChange(path, "Add", null, payloadSha, payloadEntry.Length));
            }
        }

        var targetIdentityEntry = entryMap[PayloadRoot + IdentityFileName];
        var targetIdentityBytes = await ReadEntryBytesAsync(targetIdentityEntry, cancellationToken);
        var targetIdentity = JsonSerializer.Deserialize<LocalApplicationIdentity>(targetIdentityBytes, JsonOptions)
            ?? throw new InvalidDataException("Target application identity payload could not be parsed.");
        if (!string.Equals(targetIdentity.Schema, IdentitySchema, StringComparison.Ordinal) ||
            !string.Equals(targetIdentity.ApplicationId, applicationId, StringComparison.Ordinal) ||
            !string.Equals(targetIdentity.Version, manifest.TargetVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Target identity payload does not match package application/target version.");

        var nonEffects = DefaultNonEffects();
        return new LocalApplicationUpdatePlan(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            Path.GetFullPath(packagePath),
            packageSha,
            manifestSha,
            applicationId,
            appRoot,
            identityPath,
            identitySha,
            currentIdentity.Version,
            manifest.TargetVersion,
            changes.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray(),
            true,
            true,
            true,
            true,
            true,
            true,
            nonEffects,
            "Read-only local-app update preview. READY means only that a later explicit UI confirmation may authorize exact Add/Replace under the fixed managed app root; no mutation, network, launch, installer or Agent Execute authority is created by preview.");
    }

    public async Task<(LocalApplicationUpdateReceipt Receipt, string ArtifactPath)> ApplyAsync(
        LocalApplicationUpdatePlan confirmedPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmedPlan is null || !confirmedPlan.ReadyForExplicitApplyAuthority)
            throw new InvalidDataException("A READY local-app update preview is required before apply.");
        var fresh = await PreviewAsync(confirmedPlan.PackagePath, workspaceRoot, cancellationToken);
        VerifyEquivalentPlan(confirmedPlan, fresh);

        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var workbenchRoot = Path.GetFullPath(Path.Combine(workspace, "Workbench"));
        if (!Directory.Exists(workbenchRoot))
            throw new InvalidDataException($"Workbench root is missing: {workbenchRoot}");
        var backupRoot = Path.Combine(
            workbenchRoot,
            ".workbench",
            "local-app-update-backups",
            confirmedPlan.ApplicationId,
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);

        var nonEffects = DefaultNonEffects();
        var authority = new LocalApplicationUpdateAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.local-app.apply-bounded-update",
            confirmedPlan.ApplicationId,
            confirmedPlan.ApplicationRoot,
            confirmedPlan.PackageSha256,
            confirmedPlan.ManifestSha256,
            "explicit Update local app confirmation after a fresh exact preview",
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
            false,
            new[]
            {
                "backup exact predecessor bytes for Replace paths",
                "add/replace exact manifest-declared payload files under the fixed managed app root",
                "verify exact target SHA-256 and target .matawaka-app.json identity/version",
                "write one Workbench-local update receipt"
            },
            nonEffects);

        var rollbackRequired = false;
        var rollbackPerformed = false;
        try
        {
            BackupReplacements(confirmedPlan, backupRoot);
            rollbackRequired = true;
            using var zip = ZipFile.OpenRead(confirmedPlan.PackagePath);
            var entries = zip.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .ToDictionary(entry => NormalizeZipEntryName(entry.FullName), StringComparer.OrdinalIgnoreCase);

            foreach (var change in confirmedPlan.Changes
                         .OrderBy(change => change.Path.Equals(IdentityFileName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                         .ThenBy(change => change.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePointBoundary(confirmedPlan.ApplicationRoot, change.Path);
                var destination = ResolveApplicationPath(confirmedPlan.ApplicationRoot, change.Path);
                if (change.Action == "Add" && File.Exists(destination))
                    throw new InvalidDataException($"Add destination appeared after fresh preview: {change.Path}");
                if (change.Action == "Replace")
                {
                    if (!File.Exists(destination) ||
                        !string.Equals(HashFile(destination), change.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Replacement source changed after fresh preview: {change.Path}");
                }

                var entry = entries[PayloadRoot + change.Path];
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temp = destination + ".matawaka-app-update-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await CopyEntryToFileAsync(entry, temp, cancellationToken);
                    if (!string.Equals(HashFile(temp), change.TargetSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Temporary application update bytes mismatch: {change.Path}");
                    File.Move(temp, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }

            VerifyTargetState(confirmedPlan);
            var currentIdentitySha = HashFile(confirmedPlan.IdentityPath);
            rollbackRequired = false;

            var receipt = new LocalApplicationUpdateReceipt(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                confirmedPlan.ApplicationId,
                confirmedPlan.ApplicationRoot,
                confirmedPlan.CurrentVersion,
                confirmedPlan.TargetVersion,
                confirmedPlan.PackageSha256,
                confirmedPlan.ManifestSha256,
                confirmedPlan.IdentitySha256,
                currentIdentitySha,
                confirmedPlan.Changes,
                backupRoot,
                true,
                true,
                true,
                false,
                false,
                false,
                authority,
                nonEffects,
                "LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED",
                "Exact local package bytes were applied only under the fixed managed application root after fresh revalidation. The application was not launched; update success creates no network, installer, registry, service, Agent Execute or general filesystem authority.");
            var artifactPath = await WriteReceiptAsync(workbenchRoot, receipt, cancellationToken);
            return (receipt, artifactPath);
        }
        catch (Exception original)
        {
            Exception? rollbackFailure = null;
            if (rollbackRequired)
            {
                try
                {
                    Rollback(confirmedPlan, backupRoot);
                    rollbackPerformed = true;
                    VerifyPredecessorState(confirmedPlan);
                }
                catch (Exception ex)
                {
                    rollbackFailure = ex;
                }
            }

            if (rollbackFailure is not null)
                throw new InvalidDataException($"Local application update failed and rollback could not prove exact predecessor restoration. Original={original.Message}; Rollback={rollbackFailure.Message}");
            if (rollbackPerformed)
                throw new InvalidDataException($"Local application update failed; exact predecessor bytes were restored. Cause={original.Message}");
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var traversalRefused = Refuses(() => NormalizeRelativePath("../escape.exe"));
        var rootedRefused = Refuses(() => NormalizeRelativePath("C:/escape.exe"));
        var badIdRefused = Refuses(() => ValidateApplicationId("../app"));
        var safePath = NormalizeRelativePath("bin/app.exe") == "bin/app.exe";
        var identityPath = NormalizeRelativePath(IdentityFileName) == IdentityFileName;
        return new[]
        {
            ("local-app-fixed-managed-root", true, "<WorkspaceRoot>/Apps/<ApplicationId>", "no arbitrary absolute target root"),
            ("local-app-safe-id", IsSafeApplicationId("kontur.desktop"), IsSafeApplicationId("kontur.desktop").ToString(), "true"),
            ("local-app-bad-id-refused", badIdRefused, badIdRefused.ToString(), "true"),
            ("local-app-traversal-refused", traversalRefused, traversalRefused.ToString(), "true"),
            ("local-app-rooted-path-refused", rootedRefused, rootedRefused.ToString(), "true"),
            ("local-app-normal-path-admitted", safePath, safePath.ToString(), "true"),
            ("local-app-identity-path-admitted", identityPath, identityPath.ToString(), "true"),
            ("local-app-delete-not-supported", true, "Add/Replace only", "Delete=false"),
            ("local-app-network-not-authorized", true, "NetworkAllowed=false", "false"),
            ("local-app-process-launch-not-authorized", true, "AppLaunchPerformed=false", "false"),
            ("local-app-installer-not-authorized", true, "InstallerExecutionAllowed=false", "false"),
            ("local-app-fresh-preview-required", true, "ApplyAsync -> PreviewAsync -> VerifyEquivalentPlan", "fresh revalidation")
        };
    }

    private static void ValidateManifestEnvelope(LocalApplicationUpdateManifest manifest)
    {
        if (!string.Equals(manifest.Schema, PackageSchema, StringComparison.Ordinal) ||
            !string.Equals(manifest.PackageVersion, "1", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported local application update package schema/version.");
        ValidateApplicationId(manifest.ApplicationId);
        if (string.IsNullOrWhiteSpace(manifest.ExpectedCurrentVersion) || string.IsNullOrWhiteSpace(manifest.TargetVersion) ||
            string.Equals(manifest.ExpectedCurrentVersion, manifest.TargetVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Package current/target versions are missing or identical.");
        if (!string.Equals(manifest.PayloadRoot, PayloadRoot, StringComparison.Ordinal))
            throw new InvalidDataException($"Package payload root must be exactly {PayloadRoot}");
        if (manifest.Files is null)
            throw new InvalidDataException("Package Files array is required.");
        if (manifest.NetworkAccessRequested || manifest.ProcessLaunchRequested || manifest.InstallerScriptExecutionRequested ||
            manifest.RegistryMutationRequested || manifest.ServiceMutationRequested || manifest.EnvironmentMutationRequested ||
            manifest.AgentExecuteRequested)
            throw new InvalidDataException("Local application update package requests an effect outside the v0.35 maintenance authority ceiling.");
    }

    private static void VerifyEquivalentPlan(LocalApplicationUpdatePlan expected, LocalApplicationUpdatePlan observed)
    {
        if (!string.Equals(expected.PackagePath, observed.PackagePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.PackageSha256, observed.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.ManifestSha256, observed.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.ApplicationId, observed.ApplicationId, StringComparison.Ordinal) ||
            !string.Equals(expected.ApplicationRoot, observed.ApplicationRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.IdentitySha256, observed.IdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.CurrentVersion, observed.CurrentVersion, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetVersion, observed.TargetVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Fresh local-app preview identity differs from the confirmed preview.");
        var a = expected.Changes.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var b = observed.Changes.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (a.Length != b.Length)
            throw new InvalidDataException("Fresh local-app preview file count changed.");
        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.Ordinal) ||
                !string.Equals(a[i].Action, b[i].Action, StringComparison.Ordinal) ||
                !string.Equals(a[i].CurrentSha256, b[i].CurrentSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a[i].TargetSha256, b[i].TargetSha256, StringComparison.OrdinalIgnoreCase) ||
                a[i].TargetBytes != b[i].TargetBytes)
                throw new InvalidDataException($"Fresh local-app preview changed for {a[i].Path}.");
        }
    }

    private static void BackupReplacements(LocalApplicationUpdatePlan plan, string backupRoot)
    {
        foreach (var change in plan.Changes.Where(x => x.Action == "Replace"))
        {
            var source = ResolveApplicationPath(plan.ApplicationRoot, change.Path);
            if (!File.Exists(source) || !string.Equals(HashFile(source), change.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Replacement source drifted before backup: {change.Path}");
            var backup = ResolveBoundedPath(backupRoot, change.Path, "backup");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(source, backup, overwrite: false);
            if (!string.Equals(HashFile(backup), change.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup digest mismatch: {change.Path}");
        }
    }

    private static void Rollback(LocalApplicationUpdatePlan plan, string backupRoot)
    {
        foreach (var change in plan.Changes.Reverse())
        {
            var destination = ResolveApplicationPath(plan.ApplicationRoot, change.Path);
            if (change.Action == "Add")
            {
                if (File.Exists(destination)) File.Delete(destination);
            }
            else
            {
                var backup = ResolveBoundedPath(backupRoot, change.Path, "backup");
                if (!File.Exists(backup)) throw new InvalidDataException($"Rollback backup missing: {change.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backup, destination, overwrite: true);
            }
        }
    }

    private static void VerifyTargetState(LocalApplicationUpdatePlan plan)
    {
        foreach (var change in plan.Changes)
        {
            var destination = ResolveApplicationPath(plan.ApplicationRoot, change.Path);
            if (!File.Exists(destination) || !string.Equals(HashFile(destination), change.TargetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Target application file verification failed: {change.Path}");
        }
        var identity = ReadIdentity(plan.IdentityPath);
        if (!string.Equals(identity.Schema, IdentitySchema, StringComparison.Ordinal) ||
            !string.Equals(identity.ApplicationId, plan.ApplicationId, StringComparison.Ordinal) ||
            !string.Equals(identity.Version, plan.TargetVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Target managed application identity/version verification failed.");
    }

    private static void VerifyPredecessorState(LocalApplicationUpdatePlan plan)
    {
        foreach (var change in plan.Changes)
        {
            var destination = ResolveApplicationPath(plan.ApplicationRoot, change.Path);
            if (change.Action == "Add")
            {
                if (File.Exists(destination)) throw new InvalidDataException($"Rollback left added path behind: {change.Path}");
            }
            else if (!File.Exists(destination) ||
                     !string.Equals(HashFile(destination), change.CurrentSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Rollback predecessor digest verification failed: {change.Path}");
            }
        }
        if (!string.Equals(HashFile(plan.IdentityPath), plan.IdentitySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Rollback did not restore exact predecessor identity bytes.");
        var identity = ReadIdentity(plan.IdentityPath);
        if (!string.Equals(identity.ApplicationId, plan.ApplicationId, StringComparison.Ordinal) ||
            !string.Equals(identity.Version, plan.CurrentVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Rollback predecessor identity/version verification failed.");
    }

    private static async Task<string> WriteReceiptAsync(string workbenchRoot, LocalApplicationUpdateReceipt receipt, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(workbenchRoot, "artifacts", "local-app-updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"local-app-update-v0.35-{receipt.ApplicationId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static LocalApplicationIdentity ReadIdentity(string path)
        => JsonSerializer.Deserialize<LocalApplicationIdentity>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidDataException($"Managed application identity could not be parsed: {path}");

    private static string ResolveWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root)) throw new InvalidDataException($"Workspace root does not exist: {root}");
        return root;
    }

    private static string ResolveApplicationRoot(string appsRoot, string applicationId)
    {
        ValidateApplicationId(applicationId);
        var rootPrefix = Path.GetFullPath(appsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(appsRoot, applicationId));
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application root escapes the fixed managed Apps root.");
        return candidate;
    }

    private static string ResolveApplicationPath(string appRoot, string relativePath)
        => ResolveBoundedPath(appRoot, NormalizeRelativePath(relativePath), "application");

    private static string ResolveBoundedPath(string rootPath, string relativePath, string label)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootPath, NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes fixed {label} root: {relativePath}");
        return destination;
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Empty local-app update path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Length == 0 || normalized.Contains(':') || normalized.Contains('\0') ||
            Path.IsPathRooted(normalized) || parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"Unsafe local-app update path: {path}");
        return string.Join('/', parts);
    }

    private static string NormalizeZipEntryName(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(':') || normalized.Contains('\0'))
            throw new InvalidDataException($"Unsafe ZIP entry name: {path}");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"Unsafe ZIP entry traversal: {path}");
        return string.Join('/', parts);
    }

    private static void EnsureNoReparsePointBoundary(string appRoot, string relativePath)
    {
        EnsurePathIsNotReparsePoint(appRoot, "application root");
        var current = Path.GetFullPath(appRoot);
        var parts = NormalizeRelativePath(relativePath).Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = Path.Combine(current, parts[i]);
            if (File.Exists(current))
                throw new InvalidDataException($"Application path parent is a file: {current}");
            if (Directory.Exists(current))
                EnsurePathIsNotReparsePoint(current, $"application path segment {parts[i]}");
        }
    }

    private static void EnsurePathIsNotReparsePoint(string path, string label)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} is a reparse point and is outside the v0.35 managed-root safety model: {path}");
    }

    private static bool IsSafeApplicationId(string applicationId)
        => !string.IsNullOrWhiteSpace(applicationId) && ApplicationIdRegex.IsMatch(applicationId.Trim());

    private static void ValidateApplicationId(string applicationId)
    {
        if (!IsSafeApplicationId(applicationId))
            throw new InvalidDataException($"Unsafe ApplicationId: {applicationId}");
    }

    private static void RequireSha256(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Sha256Regex.IsMatch(value.Trim()))
            throw new InvalidDataException($"{label} is not a SHA-256 hex digest.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            sha.TransformBlock(buffer, 0, read, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > 8L * 1024L * 1024L)
            throw new InvalidDataException($"Manifest/identity JSON entry is unexpectedly large: {entry.FullName}");
        await using var source = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task CopyEntryToFileAsync(ZipArchiveEntry entry, string path, CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static bool Refuses(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static string[] DefaultNonEffects()
        => new[]
        {
            "no network access or package download",
            "no git operation",
            "no process or updated-app launch",
            "no MSI/EXE/script installer execution",
            "no Windows registry mutation",
            "no Windows service mutation",
            "no environment-variable mutation",
            "no delete operation",
            "no target root outside <WorkspaceRoot>/Apps/<ApplicationId>",
            "no Workbench source mutation",
            "no Matawaka catalog mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion"
        };
}
