using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.V0551Recovery;

internal sealed record RecoveryRequest(
    string Schema,
    string RequestId,
    string RepositoryRoot,
    string FailedBootstrapLeasePath,
    string ExpectedAcceptedExecutablePath,
    string ExpectedAcceptedExecutableSha256,
    string ExpectedFailedCandidateSha256);

internal sealed record DirtyPathPolicy(string Path, string DirtySha256, string Action);

internal sealed record RecoveryPolicy(
    string RequestSchema,
    string RequestId,
    string RepositoryRoot,
    string ExpectedHead,
    string ExpectedTag,
    string FailedTargetVersion,
    string FailedTargetTag,
    string FailedLeaseId,
    string FailedLeasePath,
    string FailedCandidateSha256,
    string AcceptedExecutablePath,
    string AcceptedExecutableSha256,
    byte[] AcceptedAppBytes,
    string AcceptedAppSha256,
    IReadOnlyList<DirtyPathPolicy> DirtyPaths);

internal sealed record RecoveryPreview(
    string Schema,
    string RequestId,
    string RepositoryRoot,
    string Head,
    string AcceptedTag,
    string FailedLeaseId,
    string FailedLeaseSha256,
    string BuildReceiptPath,
    string BuildReceiptSha256,
    string FailedCandidateSha256,
    string AcceptedExecutablePath,
    string AcceptedExecutableSha256,
    IReadOnlyList<DirtyPathPolicy> DirtyPaths,
    bool NetworkAccessPerformed,
    bool GitMutationPerformed,
    bool SourceMutationPerformed,
    string Status);

internal sealed record RecoveryReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string RepositoryRoot,
    string HeadBefore,
    string HeadAfter,
    string AcceptedTag,
    string AcceptedTagCommitAfter,
    string FailedLeaseId,
    string FailedLeaseSha256,
    string BuildReceiptSha256,
    string BackupRoot,
    IReadOnlyList<DirtyPathPolicy> RestoredDirtyPaths,
    string AcceptedAppSha256,
    string AcceptedExecutablePath,
    string AcceptedExecutableSha256,
    bool SourceMutationPerformed,
    bool ExactAcceptedSourceRestored,
    bool WorkingTreeCleanAfterRecovery,
    bool GitCommitPerformed,
    bool GitTagMutationPerformed,
    bool GitRefMutationPerformed,
    bool GitRemoteMutationPerformed,
    bool NetworkAccessPerformed,
    bool ProcessLaunchPerformed,
    bool ProcessTerminationPerformed,
    bool PublicationPerformed,
    bool AutomaticRetryPerformed,
    bool HistoricalReceiptReinterpreted,
    string Status,
    string Note);

internal static class Program
{
    private const string Confirmation = "RESTORE-EXACT-V055";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
                return RecoverySelfTest.Run();

            var requestPath = args.Length == 0
                ? Path.Combine(AppContext.BaseDirectory, "recovery-request.json")
                : Path.GetFullPath(args[0]);
            var request = RecoveryRequestParser.ParseExact(File.ReadAllText(requestPath, Encoding.UTF8));
            var engine = new RecoveryEngine(ProductionPolicy());
            var preview = engine.Preview(request);

            Console.WriteLine("Matawaka Workbench v0.55.1 fail-closed source recovery");
            Console.WriteLine();
            Console.WriteLine($"Status: {preview.Status}");
            Console.WriteLine($"Repository: {preview.RepositoryRoot}");
            Console.WriteLine($"HEAD/tag: {preview.Head} / {preview.AcceptedTag}");
            Console.WriteLine($"Failed lease: {preview.FailedLeaseId} / {preview.FailedLeaseSha256}");
            Console.WriteLine($"Exact dirty paths: {preview.DirtyPaths.Count}");
            Console.WriteLine($"Accepted executable: {preview.AcceptedExecutableSha256}");
            Console.WriteLine();
            Console.WriteLine("No source/Git/network/process effect has occurred during Preview.");
            Console.WriteLine("Recovery will only restore the exact seven failed-v0.55.1 source paths to accepted v0.55 and then require git status clean.");
            Console.WriteLine();
            Console.Write($"Type {Confirmation} exactly to authorize this one local recovery: ");
            var typed = Console.ReadLine();
            if (!string.Equals(typed, Confirmation, StringComparison.Ordinal))
            {
                Console.WriteLine("CANCELLED_NO_EFFECT");
                Pause();
                return 2;
            }

            var receipt = engine.Apply(request, preview);
            Console.WriteLine();
            Console.WriteLine($"COMPLETED: {receipt.Status}");
            Console.WriteLine($"HEAD: {receipt.HeadAfter}");
            Console.WriteLine($"git status clean: {receipt.WorkingTreeCleanAfterRecovery}");
            Console.WriteLine($"Receipt: {engine.LastReceiptPath}");
            Pause();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RECOVERY_REFUSED_OR_FAILED: " + ex.Message);
            Pause();
            return 1;
        }
    }

    private static void Pause()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            _ = Console.ReadLine();
        }
    }

    private static RecoveryPolicy ProductionPolicy()
    {
        var acceptedApp = Encoding.UTF8.GetBytes(
            "using System.Windows;\n\n" +
            "namespace Matawaka.Workbench.App;\n\n" +
            "public partial class App : Application\n" +
            "{\n" +
            "    protected override void OnStartup(StartupEventArgs e)\n" +
            "    {\n" +
            "        base.OnStartup(e);\n" +
            "        var window = new MainWindow();\n" +
            "        window.ConfigureV055Routing();\n" +
            "        window.ConfigureV055AcceptanceRouting();\n" +
            "        MainWindow = window;\n" +
            "        window.Show();\n" +
            "    }\n" +
            "}\n\n" +
            "internal static class V048StringCompatibilityExtensions\n" +
            "{\n" +
            "    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)\n" +
            "        => value.EndsWith(suffix.ToString(), comparisonType);\n" +
            "}\n");

        return new RecoveryPolicy(
            "matawaka.workbench-v0551-failed-firstboot-recovery-request/v0.1",
            "recover-v0551-title-harness-failure-50b6af3831c84032bbcd51d5b03dc7eb-v1",
            @"K:\Matawaka\Workbench",
            "02d81b8559bc7c9676949be0557d20ecb50a9890",
            "workbench-v0.55-accepted",
            "0.55.1",
            "workbench-v0.55.1-accepted",
            "50b6af3831c84032bbcd51d5b03dc7eb",
            @"K:\Matawaka\Workbench\artifacts\transition-bootstrap\transition-bootstrap-v0.40-50b6af3831c84032bbcd51d5b03dc7eb.json",
            "d836d45c823e8388cb252214af176f2a1b315f2da8bbd3d9418ddd74b8c1fea7e",
            @"K:\Matawaka\Workbench\artifacts\app-v0.55-gui-update\Matawaka.Workbench.App.exe",
            "eac74afec61019095ef07649704e70e3a63cb289b3f2e86fec7a0fe4723b3872",
            acceptedApp,
            "1bdd6c83818ae5134ddbf90264c55bb3515124976ddd82c71c4e6c6681ab1655",
            new[]
            {
                new DirtyPathPolicy("PATCH-v0.55.1.md", "fd19d63ad19e1fb2849eb4e4e45b85e4ea99a61858856f33e7bb2822985ca33d", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/App.xaml.cs", "e820e987d02f50553dd964dde64f9d67e4bf525a9519f910133e4526fd9ab4d2", "RestoreAccepted"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/RealHostModelInvocationAdmissionV0551.cs", "dd794caf869a0c057881af82ab5d519c687f3b77736487d78c9c6d6f7579d8ae", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/FixedGitHubPublicationV0551Service.cs", "735541583a4f83e478e0da0abd08fb0661dc6bf3fa415fd63ec94984f55fa0e3", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/LocalCheckpointV0551Service.cs", "e5f9437ac8ce47abb6a66574d1cbd6e525fa95469840924cbbd5ae46beb26824", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/MainWindow.V0551.Acceptance.cs", "c20e20663ee49d641d875778aea14ee27ed77c3d9a7c03bd3c310752b8fa4298", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/WorkbenchV0551AcceptanceHarness.cs", "f4d5ab6d5de618216e4fe8051df00e4206d2b80cd80c18fc77e50f85fe145a4e", "RemoveAdded")
            });
    }
}

internal static class RecoveryRequestParser
{
    private static readonly HashSet<string> Expected = new(StringComparer.Ordinal)
    {
        "Schema", "RequestId", "RepositoryRoot", "FailedBootstrapLeasePath",
        "ExpectedAcceptedExecutablePath", "ExpectedAcceptedExecutableSha256", "ExpectedFailedCandidateSha256"
    };

    public static RecoveryRequest ParseExact(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Recovery request must be one JSON object.");
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != Expected.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Recovery request has missing or duplicate properties.");
        var unknown = names.Where(n => !Expected.Contains(n)).ToArray();
        if (unknown.Length != 0) throw new InvalidDataException("Recovery request contains unknown properties: " + string.Join(",", unknown));
        return JsonSerializer.Deserialize<RecoveryRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = false })
            ?? throw new InvalidDataException("Recovery request deserialized to null.");
    }
}

internal sealed class RecoveryEngine
{
    private readonly RecoveryPolicy _policy;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public string? LastReceiptPath { get; private set; }

    public RecoveryEngine(RecoveryPolicy policy) => _policy = policy;

    public RecoveryPreview Preview(RecoveryRequest request)
    {
        ValidateRequest(request);
        var root = RequireExactRoot(request.RepositoryRoot);
        var head = ReadGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();
        if (head != _policy.ExpectedHead.ToLowerInvariant()) throw Refused("HEAD_MISMATCH", $"Expected {_policy.ExpectedHead}; observed {head}.");
        var tagHead = ReadGit(root, "rev-list", "-n", "1", _policy.ExpectedTag).Trim().ToLowerInvariant();
        if (tagHead != head) throw Refused("TAG_MISMATCH", "Exact accepted v0.55 tag is not at HEAD.");

        var acceptedBytesSha = HashBytes(_policy.AcceptedAppBytes);
        if (acceptedBytesSha != _policy.AcceptedAppSha256) throw Refused("EMBEDDED_ACCEPTED_BYTES_INVALID", "Embedded accepted App.xaml.cs bytes do not match policy SHA-256.");

        var leasePath = Path.GetFullPath(request.FailedBootstrapLeasePath);
        if (!leasePath.Equals(Path.GetFullPath(_policy.FailedLeasePath), StringComparison.OrdinalIgnoreCase) || !File.Exists(leasePath))
            throw Refused("FAILED_LEASE_PATH_MISMATCH", "Exact failed bootstrap lease file is missing.");
        var leaseSha = HashFile(leasePath);
        using var leaseDoc = JsonDocument.Parse(File.ReadAllText(leasePath, Encoding.UTF8));
        var lease = leaseDoc.RootElement;
        RequireJson(lease, "Schema", "matawaka.workbench-transition-bootstrap-lease/v0.40");
        RequireJson(lease, "LeaseId", _policy.FailedLeaseId);
        RequireJson(lease, "State", "FAILED_NO_RETRY");
        RequireJson(lease, "PredecessorCommit", _policy.ExpectedHead);
        RequireJson(lease, "PredecessorTag", _policy.ExpectedTag);
        RequireJson(lease, "TargetVersion", _policy.FailedTargetVersion);
        RequireJson(lease, "TargetTag", _policy.FailedTargetTag);
        RequireJson(lease, "CandidateExecutableSha256", _policy.FailedCandidateSha256);
        RequireJson(lease, "Failure", "v0.55.1 validation returned Passed=false");
        RequireFalse(lease, "RetryAuthorized");
        RequireFalse(lease, "PublishAllowed");
        RequireFalse(lease, "LifecycleAllowed");
        var claimPath = GetString(lease, "ClaimPath");
        if (string.IsNullOrWhiteSpace(claimPath) || !File.Exists(Path.GetFullPath(claimPath)))
            throw Refused("FAILED_LEASE_CLAIM_MISSING", "Consumed bootstrap claim evidence is missing.");

        var buildReceiptPath = Path.GetFullPath(GetString(lease, "BuildReceiptPath"));
        var buildReceiptExpectedSha = GetString(lease, "BuildReceiptSha256").ToLowerInvariant();
        if (!File.Exists(buildReceiptPath)) throw Refused("BUILD_RECEIPT_MISSING", "Failed transition build receipt is missing.");
        var buildSha = HashFile(buildReceiptPath);
        if (buildSha != buildReceiptExpectedSha) throw Refused("BUILD_RECEIPT_HASH_MISMATCH", "Failed transition build receipt changed.");
        ValidateBuildReceipt(buildReceiptPath);

        var acceptedExe = Path.GetFullPath(request.ExpectedAcceptedExecutablePath);
        if (!acceptedExe.Equals(Path.GetFullPath(_policy.AcceptedExecutablePath), StringComparison.OrdinalIgnoreCase) || !File.Exists(acceptedExe))
            throw Refused("ACCEPTED_EXECUTABLE_MISSING", "Exact accepted v0.55 executable is missing.");
        var acceptedExeSha = HashFile(acceptedExe);
        if (acceptedExeSha != _policy.AcceptedExecutableSha256 || acceptedExeSha != request.ExpectedAcceptedExecutableSha256.ToLowerInvariant())
            throw Refused("ACCEPTED_EXECUTABLE_HASH_MISMATCH", "Accepted v0.55 executable digest mismatch.");

        var actualDirty = ReadStatusPaths(root);
        var expectedDirty = _policy.DirtyPaths.Select(p => p.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (!actualDirty.SequenceEqual(expectedDirty, StringComparer.Ordinal))
            throw Refused("DIRTY_SET_MISMATCH", $"Workbench source dirty set differs from exact failed v0.55.1 payload. observed={string.Join('|', actualDirty)}");
        foreach (var item in _policy.DirtyPaths)
        {
            var full = ResolveUnderRoot(root, item.Path);
            if (!File.Exists(full)) throw Refused("DIRTY_FILE_MISSING", item.Path);
            if (HashFile(full) != item.DirtySha256) throw Refused("DIRTY_HASH_MISMATCH", item.Path);
        }

        return new RecoveryPreview(
            "matawaka.workbench-v0551-failed-firstboot-recovery-preview/v0.1",
            request.RequestId, root, head, _policy.ExpectedTag, _policy.FailedLeaseId, leaseSha,
            buildReceiptPath, buildSha, _policy.FailedCandidateSha256, acceptedExe, acceptedExeSha,
            _policy.DirtyPaths, false, false, false, "READY_FOR_EXPLICIT_EXACT_V055_SOURCE_RECOVERY");
    }

    public RecoveryReceipt Apply(RecoveryRequest request, RecoveryPreview preview, int? faultAfterMutation = null)
    {
        var fresh = Preview(request);
        if (fresh.Head != preview.Head || fresh.FailedLeaseSha256 != preview.FailedLeaseSha256 || fresh.BuildReceiptSha256 != preview.BuildReceiptSha256)
            throw Refused("PREVIEW_STALE", "Exact recovery evidence changed after Preview.");

        var root = fresh.RepositoryRoot;
        var recoveryRoot = Path.Combine(root, "artifacts", "recovery-v0551");
        Directory.CreateDirectory(recoveryRoot);
        var backupRoot = Path.Combine(recoveryRoot, "backups", request.RequestId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);
        foreach (var item in _policy.DirtyPaths)
        {
            var src = ResolveUnderRoot(root, item.Path);
            var backup = ResolveUnderRoot(backupRoot, item.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(src, backup, overwrite: false);
            if (HashFile(backup) != item.DirtySha256) throw Refused("BACKUP_VERIFY_FAILED", item.Path);
        }

        var mutationCount = 0;
        try
        {
            var appPolicy = _policy.DirtyPaths.Single(p => p.Action == "RestoreAccepted");
            var appPath = ResolveUnderRoot(root, appPolicy.Path);
            var temp = appPath + ".v055-recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, _policy.AcceptedAppBytes);
                if (HashFile(temp) != _policy.AcceptedAppSha256) throw Refused("ACCEPTED_TEMP_HASH_MISMATCH", "Accepted App.xaml.cs temporary bytes mismatch.");
                File.Move(temp, appPath, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            mutationCount++;
            InjectFault(faultAfterMutation, mutationCount);

            foreach (var item in _policy.DirtyPaths.Where(p => p.Action == "RemoveAdded"))
            {
                var path = ResolveUnderRoot(root, item.Path);
                if (!File.Exists(path) || HashFile(path) != item.DirtySha256) throw Refused("DIRTY_CHANGED_DURING_RECOVERY", item.Path);
                File.Delete(path);
                mutationCount++;
                InjectFault(faultAfterMutation, mutationCount);
            }

            if (HashFile(appPath) != _policy.AcceptedAppSha256) throw Refused("ACCEPTED_APP_VERIFY_FAILED", "Restored App.xaml.cs hash mismatch.");
            var clean = ReadStatusPaths(root);
            if (clean.Length != 0) throw Refused("WORKTREE_NOT_CLEAN", string.Join('|', clean));
            var headAfter = ReadGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();
            var tagAfter = ReadGit(root, "rev-list", "-n", "1", _policy.ExpectedTag).Trim().ToLowerInvariant();
            if (headAfter != _policy.ExpectedHead || tagAfter != _policy.ExpectedHead) throw Refused("FRONTIER_DRIFT", "HEAD/tag changed during recovery.");

            var receipt = new RecoveryReceipt(
                "matawaka.workbench-v0551-failed-firstboot-recovery-receipt/v0.1", "0.1.0", DateTimeOffset.Now,
                request.RequestId, root, fresh.Head, headAfter, _policy.ExpectedTag, tagAfter,
                _policy.FailedLeaseId, fresh.FailedLeaseSha256, fresh.BuildReceiptSha256, backupRoot,
                _policy.DirtyPaths, _policy.AcceptedAppSha256, fresh.AcceptedExecutablePath, fresh.AcceptedExecutableSha256,
                true, true, true,
                false, false, false, false, false, false, false, false, false, false,
                "EXACT_ACCEPTED_V055_SOURCE_RESTORED",
                "Only the exact seven failed-v0.55.1 source paths were restored to the already accepted v0.55 source frontier. HEAD/tag remained unchanged. No Git ref/remote/network/process/publication authority was used.");
            var receiptDir = Path.Combine(recoveryRoot, "receipts");
            Directory.CreateDirectory(receiptDir);
            LastReceiptPath = Path.Combine(receiptDir, $"recovery-{request.RequestId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.WriteAllText(LastReceiptPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false));
            return receipt;
        }
        catch (Exception original)
        {
            Exception? rollbackFailure = null;
            try
            {
                foreach (var item in _policy.DirtyPaths)
                {
                    var backup = ResolveUnderRoot(backupRoot, item.Path);
                    var destination = ResolveUnderRoot(root, item.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backup, destination, overwrite: true);
                }
                var restoredDirty = ReadStatusPaths(root);
                var expectedDirty = _policy.DirtyPaths.Select(p => p.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (!restoredDirty.SequenceEqual(expectedDirty, StringComparer.Ordinal))
                    throw new InvalidDataException("Rollback dirty set differs from exact pre-recovery failed state.");
                foreach (var item in _policy.DirtyPaths)
                    if (HashFile(ResolveUnderRoot(root, item.Path)) != item.DirtySha256)
                        throw new InvalidDataException("Rollback hash mismatch: " + item.Path);
            }
            catch (Exception ex) { rollbackFailure = ex; }
            if (rollbackFailure is not null)
                throw new InvalidDataException($"RECOVERY_ROLLBACK_UNPROVEN. Original={original.Message}; Rollback={rollbackFailure.Message}", original);
            throw new InvalidDataException("Recovery failed and exact failed dirty source state was restored transactionally. No automatic retry was performed. " + original.Message, original);
        }
    }

    private void ValidateRequest(RecoveryRequest request)
    {
        if (request.Schema != _policy.RequestSchema || request.RequestId != _policy.RequestId)
            throw Refused("REQUEST_IDENTITY_MISMATCH", "Unexpected recovery request identity.");
        if (!Path.GetFullPath(request.RepositoryRoot).Equals(Path.GetFullPath(_policy.RepositoryRoot), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(request.FailedBootstrapLeasePath).Equals(Path.GetFullPath(_policy.FailedLeasePath), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(request.ExpectedAcceptedExecutablePath).Equals(Path.GetFullPath(_policy.AcceptedExecutablePath), StringComparison.OrdinalIgnoreCase) ||
            request.ExpectedAcceptedExecutableSha256.ToLowerInvariant() != _policy.AcceptedExecutableSha256 ||
            request.ExpectedFailedCandidateSha256.ToLowerInvariant() != _policy.FailedCandidateSha256)
            throw Refused("REQUEST_POLICY_MISMATCH", "Recovery request differs from the one fixed recovery policy.");
    }

    private string RequireExactRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(Path.Combine(full, ".git"))) throw Refused("REPOSITORY_MISSING", full);
        return full;
    }

    private void ValidateBuildReceipt(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        RequireJson(o, "TargetVersion", _policy.FailedTargetVersion);
        RequireJson(o, "TargetTag", _policy.FailedTargetTag);
        RequireJson(o, "PredecessorTag", _policy.ExpectedTag);
        RequireJson(o, "PredecessorCommit", _policy.ExpectedHead);
        RequireJson(o, "CandidateExecutableSha256", _policy.FailedCandidateSha256);
        RequireJson(o, "Status", "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED");
        var changes = o.GetProperty("SourceChanges").EnumerateArray().ToArray();
        if (changes.Length != _policy.DirtyPaths.Count) throw Refused("BUILD_SOURCE_SET_MISMATCH", "Build receipt source change count mismatch.");
        foreach (var expected in _policy.DirtyPaths)
        {
            var change = changes.SingleOrDefault(x => GetString(x, "Path") == expected.Path);
            if (change.ValueKind == JsonValueKind.Undefined) throw Refused("BUILD_SOURCE_PATH_MISSING", expected.Path);
            if (GetString(change, "StagedSha256").ToLowerInvariant() != expected.DirtySha256)
                throw Refused("BUILD_SOURCE_HASH_MISMATCH", expected.Path);
        }
    }

    private static void InjectFault(int? after, int count)
    {
        if (after == count) throw new IOException("Injected qualification-only recovery fault.");
    }

    private static string[] ReadStatusPaths(string root)
        => ReadGit(root, "status", "--porcelain=v1", "--untracked-files=all")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim().Trim('"').Replace('\\', '/') : line.Trim())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static string ReadGit(string root, params string[] args)
    {
        var allowed = (args.Length == 2 && args[0] == "rev-parse" && args[1] == "HEAD") ||
                      (args.Length == 4 && args[0] == "rev-list" && args[1] == "-n" && args[2] == "1") ||
                      (args.Length == 3 && args[0] == "status" && args[1] == "--porcelain=v1" && args[2] == "--untracked-files=all");
        if (!allowed) throw new InvalidOperationException("Recovery tool attempted a non-read-only or unapproved git command shape.");
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Unable to start fixed read-only git process.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Fixed read-only git observation timed out.");
        }
        if (process.ExitCode != 0) throw new InvalidDataException("Fixed read-only git observation failed: " + stderr.Trim());
        return stdout;
    }

    private static string ResolveUnderRoot(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw Refused("PATH_ESCAPE_REFUSED", relative);
        return full;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string GetString(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static void RequireJson(JsonElement o, string name, string expected)
    {
        var observed = GetString(o, name);
        if (!observed.Equals(expected, name.Contains("Sha", StringComparison.OrdinalIgnoreCase) || name.Contains("Commit", StringComparison.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw Refused("EVIDENCE_FIELD_MISMATCH", $"{name}: expected={expected}; observed={observed}");
    }

    private static void RequireFalse(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var p) || p.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || p.GetBoolean())
            throw Refused("EVIDENCE_AUTHORITY_MISMATCH", name + " must be false.");
    }

    private static InvalidDataException Refused(string classification, string detail) => new($"{classification}: {detail}");
}

internal static class RecoverySelfTest
{
    public static int Run()
    {
        var tests = new List<(string Name, Action Body)>
        {
            ("happy-path", HappyPath),
            ("extra-dirty-refused", ExtraDirtyRefused),
            ("dirty-hash-refused", DirtyHashRefused),
            ("lease-not-terminal-refused", LeaseNotTerminalRefused),
            ("rollback-restores-exact-dirty-state", RollbackRestoresDirtyState)
        };
        foreach (var test in tests)
        {
            test.Body();
            Console.WriteLine("RECOVERY_SELF_TEST_PASS " + test.Name);
        }
        Console.WriteLine($"RECOVERY_SELF_TESTS_PASS count={tests.Count}");
        return 0;
    }

    private static void HappyPath()
    {
        using var f = TestFixture.Create();
        var engine = new RecoveryEngine(f.Policy);
        var preview = engine.Preview(f.Request);
        var receipt = engine.Apply(f.Request, preview);
        if (!receipt.WorkingTreeCleanAfterRecovery || receipt.HeadAfter != f.Policy.ExpectedHead) throw new Exception("happy path did not restore clean frontier");
    }

    private static void ExtraDirtyRefused()
    {
        using var f = TestFixture.Create();
        File.WriteAllText(Path.Combine(f.Root, "extra.txt"), "extra");
        MustRefuse(() => new RecoveryEngine(f.Policy).Preview(f.Request));
    }

    private static void DirtyHashRefused()
    {
        using var f = TestFixture.Create();
        File.AppendAllText(Path.Combine(f.Root, "PATCH-v0.55.1.md"), "drift");
        MustRefuse(() => new RecoveryEngine(f.Policy).Preview(f.Request));
    }

    private static void LeaseNotTerminalRefused()
    {
        using var f = TestFixture.Create();
        var json = File.ReadAllText(f.Policy.FailedLeasePath).Replace("FAILED_NO_RETRY", "CONSUMING", StringComparison.Ordinal);
        File.WriteAllText(f.Policy.FailedLeasePath, json);
        MustRefuse(() => new RecoveryEngine(f.Policy).Preview(f.Request));
    }

    private static void RollbackRestoresDirtyState()
    {
        using var f = TestFixture.Create();
        var engine = new RecoveryEngine(f.Policy);
        var preview = engine.Preview(f.Request);
        MustRefuse(() => engine.Apply(f.Request, preview, faultAfterMutation: 2));
        _ = new RecoveryEngine(f.Policy).Preview(f.Request); // exact dirty state must be valid again
    }

    private static void MustRefuse(Action action)
    {
        try { action(); }
        catch { return; }
        throw new Exception("Expected fail-closed refusal did not occur.");
    }

    private sealed class TestFixture : IDisposable
    {
        public string Root { get; }
        public RecoveryPolicy Policy { get; }
        public RecoveryRequest Request { get; }

        private TestFixture(string root, RecoveryPolicy policy, RecoveryRequest request)
        {
            Root = root; Policy = policy; Request = request;
        }

        public static TestFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "matawaka-v0551-recovery-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Run(root, "init");
            Run(root, "config", "user.email", "qualification@example.invalid");
            Run(root, "config", "user.name", "Qualification");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\n");
            Directory.CreateDirectory(Path.Combine(root, "src", "Matawaka.Workbench.App"));
            var accepted = Encoding.UTF8.GetBytes("accepted-app\n");
            File.WriteAllBytes(Path.Combine(root, "src", "Matawaka.Workbench.App", "App.xaml.cs"), accepted);
            Run(root, "add", ".gitignore", "src/Matawaka.Workbench.App/App.xaml.cs");
            Run(root, "commit", "-m", "accepted");
            Run(root, "tag", "accepted-v055");
            var head = Run(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();

            var dirty = new[]
            {
                ("PATCH-v0.55.1.md", "dirty-patch\n", "RemoveAdded"),
                ("src/Matawaka.Workbench.App/App.xaml.cs", "dirty-app\n", "RestoreAccepted"),
                ("src/Matawaka.Workbench.App/RealHostModelInvocationAdmissionV0551.cs", "dirty-a\n", "RemoveAdded"),
                ("src/Matawaka.Workbench.App/FixedGitHubPublicationV0551Service.cs", "dirty-b\n", "RemoveAdded"),
                ("src/Matawaka.Workbench.App/LocalCheckpointV0551Service.cs", "dirty-c\n", "RemoveAdded"),
                ("src/Matawaka.Workbench.App/MainWindow.V0551.Acceptance.cs", "dirty-d\n", "RemoveAdded"),
                ("src/Matawaka.Workbench.App/WorkbenchV0551AcceptanceHarness.cs", "dirty-e\n", "RemoveAdded")
            };
            var policies = new List<DirtyPathPolicy>();
            foreach (var (path, text, action) in dirty)
            {
                var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text, new UTF8Encoding(false));
                policies.Add(new DirtyPathPolicy(path, Hash(full), action));
            }

            var artifacts = Path.Combine(root, "artifacts");
            var acceptedExe = Path.Combine(artifacts, "app-v0.55-gui-update", "Matawaka.Workbench.App.exe");
            var failedExe = Path.Combine(artifacts, "app-v0.55.1-gui-update", "Matawaka.Workbench.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(acceptedExe)!);
            Directory.CreateDirectory(Path.GetDirectoryName(failedExe)!);
            File.WriteAllBytes(acceptedExe, Encoding.UTF8.GetBytes("accepted-exe"));
            File.WriteAllBytes(failedExe, Encoding.UTF8.GetBytes("failed-exe"));
            var failedSha = Hash(failedExe);

            var buildDir = Path.Combine(artifacts, "update-applies");
            Directory.CreateDirectory(buildDir);
            var buildPath = Path.Combine(buildDir, "build.json");
            File.WriteAllText(buildPath, JsonSerializer.Serialize(new
            {
                TargetVersion = "0.55.1", TargetTag = "failed-v0551", PredecessorTag = "accepted-v055", PredecessorCommit = head,
                CandidateExecutableSha256 = failedSha, Status = "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED",
                SourceChanges = policies.Select(p => new { p.Path, StagedSha256 = p.DirtySha256 }).ToArray()
            }));
            var buildSha = Hash(buildPath);

            var transitionDir = Path.Combine(artifacts, "transition-bootstrap");
            Directory.CreateDirectory(transitionDir);
            var claimPath = Path.Combine(transitionDir, "lease.json.claim");
            File.WriteAllText(claimPath, "claimed");
            var leasePath = Path.Combine(transitionDir, "lease.json");
            File.WriteAllText(leasePath, JsonSerializer.Serialize(new
            {
                Schema = "matawaka.workbench-transition-bootstrap-lease/v0.40", LeaseId = "lease-test", State = "FAILED_NO_RETRY",
                PredecessorCommit = head, PredecessorTag = "accepted-v055", TargetVersion = "0.55.1", TargetTag = "failed-v0551",
                CandidateExecutableSha256 = failedSha, Failure = "v0.55.1 validation returned Passed=false",
                RetryAuthorized = false, PublishAllowed = false, LifecycleAllowed = false, ClaimPath = claimPath,
                BuildReceiptPath = buildPath, BuildReceiptSha256 = buildSha
            }));

            var policy = new RecoveryPolicy(
                "test-schema", "test-request", root, head, "accepted-v055", "0.55.1", "failed-v0551", "lease-test", leasePath,
                failedSha, acceptedExe, Hash(acceptedExe), accepted, HashBytesLocal(accepted), policies);
            var request = new RecoveryRequest("test-schema", "test-request", root, leasePath, acceptedExe, policy.AcceptedExecutableSha256, failedSha);
            return new TestFixture(root, policy, request);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static string Run(string root, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd(); var stderr = p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception(stderr);
            return stdout;
        }

        private static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
        private static string HashBytesLocal(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
