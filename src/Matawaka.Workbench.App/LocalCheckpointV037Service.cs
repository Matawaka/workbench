using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed class LocalCheckpointV037Service
{
    public const string Version = "0.37.0";
    public const string ExpectedPredecessorCommit = "8f8bac01661c0b5614422c3708f1afb78a483c8b";
    public const string ExpectedPredecessorTag = "workbench-v0.36-accepted";
    public const string TargetTag = "workbench-v0.37-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.37 local app update package builder";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidate> PreviewAsync(
        string workspaceRoot,
        string acceptanceArtifactPath,
        WorkbenchAcceptanceReceipt acceptance,
        CancellationToken cancellationToken)
    {
        if (!acceptance.Passed || acceptance.Version != Version ||
            acceptance.Schema != "matawaka.workbench-acceptance-receipt/v0.37")
            throw new InvalidDataException("A passing Workbench v0.37 Self-test receipt is required before local checkpoint acceptance.");

        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"HEAD {head} is not exact accepted v0.36 predecessor {ExpectedPredecessorCommit}.");
        var predecessorTagHead = RequireSha(
            (await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout,
            ExpectedPredecessorTag);
        if (!head.Equals(predecessorTagHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted v0.36 predecessor tag is not at exact HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.37 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("v0.37 checkpoint contains a forbidden artifacts/.workbench path.");

        var (manifestPath, manifestSha) = ValidateBuildSourceManifest(root, head, changed);
        return new LocalCheckpointCandidate(
            Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifestPath, manifestSha,
            acceptance.AppExecutableSha256, changed);
    }

    public async Task<LocalCheckpointReceipt> AcceptAsync(LocalCheckpointCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version ||
            !candidate.PreviousHead.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match the fixed v0.37 acceptance contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.37 checkpoint preview.");
        var current = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!current.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.37 checkpoint preview.");

        var committed = false;
        var tagged = false;
        try
        {
            await GitAsync(root, cancellationToken, "add", "-A", "--", ".");
            await GitAsync(root, cancellationToken, "commit", "-m", CommitMessage);
            committed = true;
            var newHead = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "new HEAD");
            if (newHead.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Git commit did not advance v0.37 HEAD.");
            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m",
                "Accepted Workbench v0.37: local app update package builder derives predecessor hashes from actual registered bytes and validates generated packages through the existing updater Preview; no update/launch authority");
            tagged = true;
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.37 checkpoint commit.");

            var nonEffects = new[]
            {
                "no local application package build performed by checkpoint",
                "no local application registration/update performed by checkpoint",
                "no git push/fetch or remote mutation",
                "no network access",
                "no application or installer launch",
                "no Agent Execute or ActionPermit",
                "no publication/lifecycle authority inferred from checkpoint",
                "no canonical UU-AAP conformance or Stable Core promotion"
            };
            var authority = new LocalCheckpointAuthorityReceipt(
                "matawaka.workbench-local-checkpoint-authority-receipt/v0.37",
                "human-operator-at-workbench-ui",
                "git.local-checkpoint.accept",
                root,
                "explicit v0.37 Accept checkpoint button + confirmation after passing v0.37 Self-test",
                true, true, false, false, false, false,
                new[] { "git add -A -- .", "git commit -m <fixed-v0.37-message>", "git tag -a workbench-v0.37-accepted" },
                nonEffects);
            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.37",
                Version,
                DateTimeOffset.Now,
                candidate.PreviousHead,
                newHead,
                TargetTag,
                CommitMessage,
                candidate.AcceptanceArtifactPath,
                candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath,
                candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256,
                candidate.ChangedFiles,
                authority,
                true,
                nonEffects,
                "Local Workbench v0.37 acceptance only. Package build, local-app update, publication and lifecycle remain separate decisions.");
        }
        catch
        {
            if (tagged) await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (committed) await GitAsync(root, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceipt receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.37-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string ValidateAcceptanceArtifact(string root, string path, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException("Passing v0.37 acceptance artifact is missing.");
        var full = Path.GetFullPath(path);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact escapes Workbench artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.37 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != "matawaka.workbench-acceptance-receipt/v0.37" || parsed.Version != Version ||
            parsed.RunId != acceptance.RunId || !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match the passing in-memory v0.37 Self-test receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessorHead, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "v0.37.0-source-manifest*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null) throw new InvalidDataException("v0.37 build source manifest is missing.");
        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.37 build source manifest could not be parsed.");
        if (manifest.Schema != "matawaka.workbench-build-source-manifest/v0.37" || manifest.Version != Version ||
            !manifest.PredecessorGitSha.Equals(predecessorHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.37 build source manifest identity/predecessor.");
        var manifestFiles = manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var currentFiles = changed.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!manifestFiles.Select(item => item.Path).SequenceEqual(currentFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Current changed-file set differs from the v0.37 build-bound source set.");
        foreach (var item in manifestFiles)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) ||
                !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.37 source mismatch: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest no longer matches v0.37 Self-test receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string stdout)
        => stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim()).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var p = path.Replace('\\', '/');
        return p.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) || p.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) || p.Contains("../", StringComparison.Ordinal);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static Task<GitResult> GitAsync(string root, CancellationToken token, params string[] args)
        => GitAsync(root, token, false, args);

    private static async Task<GitResult> GitAsync(string root, CancellationToken token, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.37 checkpoint Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("v0.37 checkpoint Git operation timed out.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidDataException($"v0.37 checkpoint Git operation failed: {stderr.Trim()}");
        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
