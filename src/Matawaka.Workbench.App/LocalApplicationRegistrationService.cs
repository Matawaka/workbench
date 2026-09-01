using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record LocalApplicationRegistrationFile(string Path, string Sha256, long Bytes);

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
/// Identity-only adoption of one existing direct child of Workspace/Apps.
/// Registration creates exactly one sidecar and never imports, copies, moves,
/// updates or launches application content.
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
    private static readonly Regex AppIdRegex = new(
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
        var workspace = RequireDirectory(workspaceRoot, "Workspace root");
        var appsRoot = RequireDirectory(Path.Combine(workspace, AppsDirectoryName), "Managed Apps root");
        RejectReparse(appsRoot, "Managed Apps root");

        var appRoot = RequireDirectory(selectedApplicationRoot, "Selected application root");
        RejectReparse(appRoot, "Selected application root");
        var parent = Directory.GetParent(appRoot)?.FullName;
        if (parent is null || !Path.GetFullPath(parent).Equals(Path.GetFullPath(appsRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only a direct child of <WorkspaceRoot>/Apps may be registered.");

        var appId = Path.GetFileName(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!AppIdRegex.IsMatch(appId))
            throw new InvalidDataException($"Application directory name is not a safe ApplicationId token: {appId}");

        var identityPath = Path.Combine(appRoot, IdentityFileName);
        if (File.Exists(identityPath) || Directory.Exists(identityPath))
            throw new InvalidDataException("Selected application is already registered or the identity path is occupied.");

        var files = Inventory(appRoot, cancellationToken);
        if (files.Count == 0)
            throw new InvalidDataException("Refusing to register an empty application directory.");
        var totalBytes = files.Sum(item => item.Bytes);
        var tree = ComputeTreeDigest(files);
        var identity = new LocalApplicationIdentity(IdentitySchema, appId, "baseline-" + tree[..16]);
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
            tree,
            identity,
            HashBytes(identityBytes),
            files,
            true,
            true,
            true,
            true,
            true,
            nonEffects,
            "READY means only that a later explicit confirmation may create the exact identity sidecar. baseline-* is a deterministic observed-byte baseline, not a vendor version claim."));
    }

    public async Task<(LocalApplicationRegistrationReceipt Receipt, string ArtifactPath)> RegisterAsync(
        LocalApplicationRegistrationPlan confirmedPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmedPlan is null || !confirmedPlan.ReadyForExplicitRegistrationAuthority)
            throw new InvalidDataException("A READY registration preview is required.");

        var fresh = await PreviewAsync(confirmedPlan.ApplicationRoot, workspaceRoot, cancellationToken);
        RequireEquivalent(confirmedPlan, fresh);
        var identityBytes = SerializeIdentity(fresh.ProposedIdentity);
        var identitySha = HashBytes(identityBytes);
        if (!identitySha.Equals(fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Proposed identity bytes drifted after fresh preview.");

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
                "verify all pre-existing application bytes remain at the confirmed tree digest",
                "write one Workbench-local registration receipt"
            },
            NonEffects: nonEffects);

        var created = false;
        string? receiptPath = null;
        try
        {
            if (File.Exists(fresh.IdentityPath) || Directory.Exists(fresh.IdentityPath))
                throw new InvalidDataException("Identity path appeared after fresh preview.");

            var temp = fresh.IdentityPath + ".matawaka-register-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, identityBytes, cancellationToken);
                RejectReparse(temp, "temporary identity file");
                if (!HashFile(temp).Equals(identitySha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Temporary identity digest mismatch.");
                File.Move(temp, fresh.IdentityPath, overwrite: false);
                created = true;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            RejectReparse(fresh.IdentityPath, "application identity file");
            var observedIdentity = JsonSerializer.Deserialize<LocalApplicationIdentity>(
                await File.ReadAllBytesAsync(fresh.IdentityPath, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("Created identity could not be parsed.");
            if (!Equals(observedIdentity, fresh.ProposedIdentity) ||
                !HashFile(fresh.IdentityPath).Equals(identitySha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Created identity does not equal confirmed bytes/fields.");

            var after = Inventory(fresh.ApplicationRoot, cancellationToken);
            if (!SameFiles(fresh.Files, after) || !ComputeTreeDigest(after).Equals(fresh.TreeSha256, StringComparison.OrdinalIgnoreCase))
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
                identitySha,
                true,
                true,
                false,
                false,
                false,
                authority,
                nonEffects,
                "LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED",
                "Only .matawaka-app.json was created. baseline-* identifies the observed byte baseline and is not an upstream/vendor version assertion. Update and launch remain separate later decisions.");

            receiptPath = await WriteReceiptAsync(workspaceRoot, receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        catch
        {
            if (created && File.Exists(fresh.IdentityPath)) File.Delete(fresh.IdentityPath);
            if (receiptPath is not null && File.Exists(receiptPath)) File.Delete(receiptPath);
            var rollback = Inventory(fresh.ApplicationRoot, CancellationToken.None);
            if (!SameFiles(fresh.Files, rollback) || !ComputeTreeDigest(rollback).Equals(fresh.TreeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Registration failed and the original application byte baseline was not restored.");
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var fixture = new[]
        {
            new LocalApplicationRegistrationFile("a.txt", new string('a', 64), 2),
            new LocalApplicationRegistrationFile("sub/b.bin", new string('b', 64), 3)
        };
        var digest1 = ComputeTreeDigest(fixture);
        var digest2 = ComputeTreeDigest(fixture.Reverse());
        return new[]
        {
            ("registration-safe-app-id", AppIdRegex.IsMatch("demo.app_1-test"), "demo.app_1-test", "safe token"),
            ("registration-traversal-app-id-refused", !AppIdRegex.IsMatch("../outside"), "../outside", "refused"),
            ("registration-tree-digest-order-stable", digest1 == digest2, digest1, digest2),
            ("registration-baseline-token-length", ("baseline-" + digest1[..16]).Length == 25, "baseline-" + digest1[..16], "25 chars"),
            ("registration-identity-only-contract", true, "copy=false;move=false;delete=false;replace=false;launch=false;network=false", "identity create only")
        };
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalApplicationRegistrationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var workspace = RequireDirectory(workspaceRoot, "Workspace root");
        var workbenchRoot = RequireDirectory(Path.Combine(workspace, "Workbench"), "Workbench root");
        var directory = Path.Combine(workbenchRoot, "artifacts", "local-app-registration");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"local-app-registration-{receipt.ApplicationId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static List<LocalApplicationRegistrationFile> Inventory(string appRoot, CancellationToken cancellationToken)
    {
        RejectReparse(appRoot, "application root");
        var files = new List<LocalApplicationRegistrationFile>();
        var stack = new Stack<string>();
        stack.Push(appRoot);
        long total = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            RejectReparse(current, "application directory");

            foreach (var directory in Directory.EnumerateDirectories(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(directory, "application subdirectory");
                stack.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(file, "application file");
                var relative = NormalizeRelative(Path.GetRelativePath(appRoot, file));
                if (relative.Equals(IdentityFileName, StringComparison.OrdinalIgnoreCase)) continue;
                var size = new FileInfo(file).Length;
                total += size;
                if (files.Count + 1 > MaxFiles || total > MaxTotalBytes)
                    throw new InvalidDataException($"Application inventory exceeds registration bounds. files>{MaxFiles} or bytes>{MaxTotalBytes}");
                files.Add(new LocalApplicationRegistrationFile(relative, HashFile(file), size));
            }
        }
        return files.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
    }

    public static string ComputeTreeDigest(IEnumerable<LocalApplicationRegistrationFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in files.OrderBy(item => item.Path, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes($"{item.Path}\0{item.Sha256.ToLowerInvariant()}\0{item.Bytes}\n"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void RequireEquivalent(LocalApplicationRegistrationPlan confirmed, LocalApplicationRegistrationPlan fresh)
    {
        if (!confirmed.ApplicationId.Equals(fresh.ApplicationId, StringComparison.Ordinal) ||
            !Path.GetFullPath(confirmed.ApplicationRoot).Equals(Path.GetFullPath(fresh.ApplicationRoot), StringComparison.OrdinalIgnoreCase) ||
            !confirmed.TreeSha256.Equals(fresh.TreeSha256, StringComparison.OrdinalIgnoreCase) ||
            !confirmed.ProposedIdentitySha256.Equals(fresh.ProposedIdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !Equals(confirmed.ProposedIdentity, fresh.ProposedIdentity) ||
            !SameFiles(confirmed.Files, fresh.Files))
            throw new InvalidDataException("Registration preview is stale; application state changed before confirmation.");
    }

    private static bool SameFiles(IReadOnlyList<LocalApplicationRegistrationFile> left, IReadOnlyList<LocalApplicationRegistrationFile> right)
    {
        if (left.Count != right.Count) return false;
        var a = left.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        return a.Zip(b).All(pair =>
            pair.First.Path.Equals(pair.Second.Path, StringComparison.Ordinal) &&
            pair.First.Sha256.Equals(pair.Second.Sha256, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Bytes == pair.Second.Bytes);
    }

    private static byte[] SerializeIdentity(LocalApplicationIdentity identity)
        => JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) ||
            normalized.StartsWith('/') || normalized == ".." || normalized.StartsWith("../") || normalized.Contains("/../"))
            throw new InvalidDataException($"Unsafe application relative path: {path}");
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

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string[] DefaultNonEffects() => new[]
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
