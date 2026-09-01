using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record LocalApplicationRegistrationFile(
    string Path,
    string Sha256,
    long Bytes);

public sealed record LocalApplicationRegistrationPlan(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string IdentityPath,
    int FileCount,
    long TotalBytes,
    string TreeSha256,
    LocalApplicationIdentity ProposedIdentity,
    string ProposedIdentitySha256,
    IReadOnlyList<LocalApplicationRegistrationFile> Files,
    bool DirectManagedChildValidated,
    bool ReparseBoundaryValidated,
    bool IdentityAbsentValidated,
    bool InventoryBounded,
    bool ReadyForExplicitRegistrationAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalApplicationRegistrationAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string ApplicationId,
    string ApplicationRoot,
    string TreeSha256,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FreshPreviewRevalidationRequired,
    bool IdentityCreateOnly,
    bool CopyAllowed,
    bool MoveAllowed,
    bool DeleteAllowed,
    bool ReplaceExistingAppFileAllowed,
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

public sealed record LocalApplicationRegistrationReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string TreeSha256,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<LocalApplicationRegistrationFile> Files,
    LocalApplicationIdentity Identity,
    string IdentitySha256,
    bool FreshPreviewVerified,
    bool IdentityCreated,
    bool OtherApplicationFileChanged,
    bool ApplicationLaunchPerformed,
    bool VendorVersionClaimCreated,
    LocalApplicationRegistrationAuthorityReceipt Authority,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// Registers one existing direct child of <WorkspaceRoot>/Apps by creating only
/// .matawaka-app.json after a fresh byte-for-byte inventory revalidation.
/// It does not import/copy/move/update/launch the application and creates no
/// vendor-version claim; the baseline token is derived from observed bytes.
/// </summary>
public sealed class LocalApplicationRegistrationService
{
    public const string Version = "0.36.0";
    public const string PlanSchema = "matawaka.local-app-registration-plan/v0.36";
    public const string ReceiptSchema = "matawaka.local-app-registration-receipt/v0.36";
    public const string AuthoritySchema = "matawaka.local-app-registration-authority-receipt/v0.36";
    public const string IdentitySchema = LocalApplicationMaintenanceService.IdentitySchema;
    public const string IdentityFileName = LocalApplicationMaintenanceService.IdentityFileName;
    public const string AppsDirectoryName = LocalApplicationMaintenanceService.AppsDirectoryName;

    private const int MaxFiles = 4096;
    private const long MaxTotalBytes = 2L * 1024L * 1024L * 1024L;
    private static readonly Regex ApplicationIdRegex = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public Task<LocalApplicationRegistrationPlan> PreviewAsync(
        string selectedApplicationRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var appsRoot = Path.GetFullPath(Path.Combine(workspace, AppsDirectoryName));
        if (!Directory.Exists(appsRoot))
            throw new InvalidDataException($"Managed Apps root is missing: {appsRoot}");
        EnsureNotReparse(appsRoot, "managed Apps root");

        if (string.IsNullOrWhiteSpace(selectedApplicationRoot))
            throw new InvalidDataException("Application directory selection is required.");
        var appRoot = Path.GetFullPath(selectedApplicationRoot.Trim());
        if (!Directory.Exists(appRoot))
            throw new InvalidDataException($"Selected application directory is missing: {appRoot}");
        EnsureNotReparse(appRoot, "selected application root");

        var parent = Directory.GetParent(appRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) ||
            !string.Equals(Path.GetFullPath(parent), appsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only a direct child of <WorkspaceRoot>/Apps may be registered.");

        var appId = Path.GetFileName(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!ApplicationIdRegex.IsMatch(appId))
            throw new InvalidDataException($"Application folder name is not a safe ApplicationId token: {appId}");

        var identityPath = Path.Combine(appRoot, IdentityFileName);
        if (File.Exists(identityPath) || Directory.Exists(identityPath))
            throw new InvalidDataException($"Application is already registered or identity path is occupied: {identityPath}");

        var files = Inventory(appRoot, cancellationToken);
        if (files.Count == 0)
            throw new InvalidDataException("Refusing to register an empty application directory.");
        var totalBytes = files.Sum(item => item.Bytes);
        if (files.Count > MaxFiles || totalBytes > MaxTotalBytes)
            throw new InvalidDataException($"Application inventory exceeds registration bounds. files={files.Count}; bytes={totalBytes}");

        var treeSha = ComputeTreeDigest(files);
        var baseline = "baseline-" + treeSha[..16];
        var identity = new LocalApplicationIdentity(IdentitySchema, appId, baseline);
        var identityBytes = SerializeIdentity(identity);
        var nonEffects = DefaultNonEffects();

        return Task.FromResult(new LocalApplicationRegistrationPlan(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            appId,
            appRoot,
            identityPath,
            files.Count,
            totalBytes,
            treeSha,
            identity,
            HashBytes(identityBytes),
            files,
            true,
            true,
            true,
            true,
            true,
            nonEffects,
            "Read-only registration preview. READY authorizes nothing by itself. Proposed baseline version is derived from observed application bytes and is not a vendor/upstream version claim."));
    }

    public async Task<(LocalApplicationRegistrationReceipt Receipt, string ArtifactPath)> RegisterAsync(
        LocalApplicationRegistrationPlan confirmedPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmedPlan is null || !confirmedPlan.ReadyForExplicitRegistrationAuthority)
            throw new InvalidDataException("A READY registration preview is required.");

        var fresh = await PreviewAsync(confirmedPlan.ApplicationRoot, workspaceRoot, cancellationToken);
        VerifyEquivalent(confirmedPlan, fresh);

        var identityBytes = SerializeIdentity(fresh.ProposedIdentity);
        if (!HashBytes(identityBytes).Equals(fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Proposed identity digest drifted after fresh preview.");

        var nonEffects = DefaultNonEffects();
        var authority = new LocalApplicationRegistrationAuthorityReceipt(
            Schema: AuthoritySchema,
            Subject: "human-operator-at-workbench-ui",
            Operation: "workbench.local-app.register-existing-managed-directory",
            ApplicationId: fresh.ApplicationId,
            ApplicationRoot: fresh.ApplicationRoot,
            TreeSha256: fresh.TreeSha256,
            AuthoritySource: "explicit Local apps registration confirmation after fresh exact inventory preview",
            ExplicitUiConfirmationRequired: true,
            FreshPreviewRevalidationRequired: true,
            IdentityCreateOnly: true,
            CopyAllowed: false,
            MoveAllowed: false,
            DeleteAllowed: false,
            ReplaceExistingAppFileAllowed: false,
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
                "atomically create exact <AppRoot>/.matawaka-app.json when absent",
                "verify the pre-existing application inventory/tree digest remains unchanged",
                "write one Workbench-local registration receipt"
            },
            NonEffects: nonEffects);

        var identityCreated = false;
        string? receiptPath = null;
        try
        {
            EnsureNoReparseBoundary(fresh.ApplicationRoot);
            if (File.Exists(fresh.IdentityPath) || Directory.Exists(fresh.IdentityPath))
                throw new InvalidDataException("Identity path appeared after fresh preview.");

            var tempIdentity = fresh.IdentityPath + ".matawaka-register-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempIdentity, identityBytes, cancellationToken);
                if (!HashFile(tempIdentity).Equals(fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Temporary identity bytes do not match the preview digest.");
                File.Move(tempIdentity, fresh.IdentityPath, overwrite: false);
                identityCreated = true;
            }
            finally
            {
                if (File.Exists(tempIdentity)) File.Delete(tempIdentity);
            }

            var observedIdentity = JsonSerializer.Deserialize<LocalApplicationIdentity>(
                await File.ReadAllBytesAsync(fresh.IdentityPath, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("Created identity could not be parsed.");
            if (!Equals(observedIdentity, fresh.ProposedIdentity) ||
                !HashFile(fresh.IdentityPath).Equals(fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Created identity does not exactly match the confirmed registration preview.");

            var postFiles = Inventory(fresh.ApplicationRoot, cancellationToken);
            if (!SameFiles(fresh.Files, postFiles) ||
                !ComputeTreeDigest(postFiles).Equals(fresh.TreeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A pre-existing application file changed during registration.");

            var receipt = new LocalApplicationRegistrationReceipt(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                fresh.ApplicationId,
                fresh.ApplicationRoot,
                fresh.TreeSha256,
                fresh.FileCount,
                fresh.TotalBytes,
                fresh.Files,
                fresh.ProposedIdentity,
                fresh.ProposedIdentitySha256,
                true,
                true,
                false,
                false,
                false,
                authority,
                nonEffects,
                "LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED",
                "Registration created only the Workbench-local app identity sidecar. baseline-* is an observed-byte baseline, not a vendor version claim. Update and launch remain separate later decisions.");

            receiptPath = await WriteReceiptAsync(workspaceRoot, receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        catch
        {
            if (identityCreated && File.Exists(fresh.IdentityPath))
                File.Delete(fresh.IdentityPath);
            if (receiptPath is not null && File.Exists(receiptPath))
                File.Delete(receiptPath);

            var rollbackFiles = Inventory(fresh.ApplicationRoot, CancellationToken.None);
            if (!SameFiles(fresh.Files, rollbackFiles) ||
                !ComputeTreeDigest(rollbackFiles).Equals(fresh.TreeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Registration failed and pre-existing application bytes no longer match the confirmed baseline.");
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var safe = ApplicationIdRegex.IsMatch("demo.app_1-test");
        var unsafeId = !ApplicationIdRegex.IsMatch("../outside");
        var fixtureFiles = new[]
        {
            new LocalApplicationRegistrationFile("a.txt", new string('a', 64), 2),
            new LocalApplicationRegistrationFile("sub/b.bin", new string('b', 64), 3)
        };
        var digestA = ComputeTreeDigest(fixtureFiles);
        var digestB = ComputeTreeDigest(fixtureFiles.Reverse().ToArray());
        return new[]
        {
            ("registration-safe-app-id", safe, safe.ToString(), "true"),
            ("registration-traversal-app-id-refused", unsafeId, unsafeId.ToString(), "true"),
            ("registration-tree-digest-order-stable", digestA == digestB, digestA, digestB),
            ("registration-baseline-token-bounded", ("baseline-" + digestA[..16]).Length == 25, "baseline-" + digestA[..16], "baseline-<16hex>"),
            ("registration-identity-only-contract", true, "copy=false; move=false; delete=false; replace=false; launch=false; network=false", "identity create only")
        };
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalApplicationRegistrationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var workbenchRoot = Path.GetFullPath(Path.Combine(workspace, "Workbench"));
        if (!Directory.Exists(workbenchRoot))
            throw new InvalidDataException($"Workbench root is missing: {workbenchRoot}");
        var directory = Path.Combine(workbenchRoot, "artifacts", "local-app-registration");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"local-app-registration-{receipt.ApplicationId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        return path;
    }

    private static List<LocalApplicationRegistrationFile> Inventory(
        string appRoot,
        CancellationToken cancellationToken)
    {
        EnsureNoReparseBoundary(appRoot);
        var files = new List<LocalApplicationRegistrationFile>();
        var stack = new Stack<string>();
        stack.Push(appRoot);
        long totalBytes = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            EnsureNotReparse(current, "application directory");

            foreach (var directory in Directory.EnumerateDirectories(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                EnsureNotReparse(directory, "application subdirectory");
                stack.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotReparse(file, "application file");
                var relative = NormalizeRelativePath(Path.GetRelativePath(appRoot, file));
                if (relative.Equals(IdentityFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var info = new FileInfo(file);
                totalBytes += info.Length;
                if (files.Count + 1 > MaxFiles || totalBytes > MaxTotalBytes)
                    throw new InvalidDataException($"Application inventory exceeds registration bounds. files>{MaxFiles} or bytes>{MaxTotalBytes}");
                files.Add(new LocalApplicationRegistrationFile(relative, HashFile(file), info.Length));
            }
        }

        return files.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
    }

    public static string ComputeTreeDigest(IEnumerable<LocalApplicationRegistrationFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in files.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            var line = $"{item.Path}\0{item.Sha256.ToLowerInvariant()}\0{item.Bytes}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void VerifyEquivalent(
        LocalApplicationRegistrationPlan confirmed,
        LocalApplicationRegistrationPlan fresh)
    {
        if (!string.Equals(confirmed.ApplicationId, fresh.ApplicationId, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFullPath(confirmed.ApplicationRoot), Path.GetFullPath(fresh.ApplicationRoot), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(confirmed.TreeSha256, fresh.TreeSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(confirmed.ProposedIdentitySha256, fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !Equals(confirmed.ProposedIdentity, fresh.ProposedIdentity) ||
            !SameFiles(confirmed.Files, fresh.Files))
            throw new InvalidDataException("Application registration state changed after preview; explicit confirmation is stale.");
    }

    private static bool SameFiles(
        IReadOnlyList<LocalApplicationRegistrationFile> left,
        IReadOnlyList<LocalApplicationRegistrationFile> right)
    {
        if (left.Count != right.Count) return false;
        var a = left.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.Ordinal) ||
                !string.Equals(a[i].Sha256, b[i].Sha256, StringComparison.OrdinalIgnoreCase) ||
                a[i].Bytes != b[i].Bytes)
                return false;
        }
        return true;
    }

    private static byte[] SerializeIdentity(LocalApplicationIdentity identity)
        => JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith('/') ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.Equals("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
            throw new InvalidDataException($"Unsafe application relative path: {path}");
        return normalized;
    }

    private static void EnsureNoReparseBoundary(string appRoot)
    {
        EnsureNotReparse(appRoot, "application root");
        var current = Directory.GetParent(appRoot);
        if (current is not null) EnsureNotReparse(current.FullName, "Apps root");
    }

    private static void EnsureNotReparse(string path, string role)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse points are not allowed at {role}: {path}");
    }

    private static string ResolveWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var full = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(full))
            throw new InvalidDataException($"Workspace root does not exist: {full}");
        return full;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string[] DefaultNonEffects() =>
    {
        "no application file copy or move",
        "no application file delete or replacement",
        "no application launch",
        "no installer or script execution",
        "no network access",
        "no Git operation",
        "no registry/service/environment mutation",
        "no target outside <WorkspaceRoot>/Apps/<ApplicationId>",
        "no Workbench source mutation",
        "no catalog mutation",
        "no Agent Execute authority",
        "no ActionPermit creation",
        "baseline identity does not claim vendor/upstream version"
    };
}
