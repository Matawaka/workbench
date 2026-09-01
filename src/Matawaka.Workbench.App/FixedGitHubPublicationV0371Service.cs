using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed class FixedGitHubPublicationV0371Service
{
    public const string Version = "0.37.1";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string AcceptedTag = "workbench-v0.37.1-accepted";
    public const string ExpectedParent = "0d20e3bbe7c28b48cac3ef97b903b4a3a6176521";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.37.1";
    public const string AuthoritySchema = "matawaka.workbench-fixed-github-publication-authority-receipt/v0.37.1";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidate> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.37.1 publication.");
        var head = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        if (!parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.37.1 accepted HEAD parent is not exact accepted v0.37: {parent}");
        var tagHead = RequireSha((await RunGitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Accepted v0.37.1 tag does not point at current HEAD.");
        var configured = await GetConfiguredRemoteUrlAsync(root, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl)) throw new InvalidDataException($"Remote {RemoteName} conflicts with fixed URL: {configured}");
        return new FixedGitHubPublicationCandidate(Version, root, RemoteName, RemoteUrl, head, parent, AcceptedTag,
            string.IsNullOrWhiteSpace(configured) ? null : configured, string.IsNullOrWhiteSpace(configured), true);
    }

    public async Task<FixedGitHubPublicationReceipt> PublishAsync(FixedGitHubPublicationCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.RemoteName != RemoteName || !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) ||
            candidate.AcceptedTag != AcceptedTag || !candidate.Parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match fixed v0.37.1 contract.");
        await ReverifyLocalCandidateAsync(candidate, cancellationToken);
        var root = candidate.RepositoryRoot;
        var remoteAdded = await EnsureFixedRemoteAsync(root, cancellationToken);
        var remoteMainBefore = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Fixed remote main is missing.");
        var remoteTagBefore = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag);
        var mainState = ClassifyRemoteMain(candidate.Parent, candidate.Head, remoteMainBefore);
        var tagState = ClassifyRemoteTag(candidate.Head, remoteTagBefore);
        if (mainState == "CONFLICT") throw new InvalidDataException("Remote main is neither exact local parent nor exact local HEAD.");
        if (tagState == "CONFLICT") throw new InvalidDataException("Remote accepted tag conflicts with local accepted HEAD.");
        var mainPush = false; var tagPush = false; var recovery = mainState == "ALREADY_HEAD" && tagState == "ABSENT";
        if (mainState == "PARENT") { await RunGitAsync(root, cancellationToken, "push", RemoteName, $"{candidate.Head}:refs/heads/main"); mainPush = true; }
        var mid = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Remote main disappeared during publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Remote main did not converge to exact accepted HEAD.");
        if (tagState == "ABSENT") { await RunGitAsync(root, cancellationToken, "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}"); tagPush = true; }
        var remoteMainAfter = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Remote main missing after publication.");
        var remoteTagAfter = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag) ?? throw new InvalidDataException("Remote tag missing after publication.");
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !remoteTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.37.1 HEAD.");
        var headAfter = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter.Stdout)) throw new InvalidDataException("Local Workbench state changed during publication.");

        var nonEffects = new[]
        {
            "no force push or conflicting tag movement",
            "no arbitrary remote URL/name",
            "no candidate import/copy/move authority created by publication",
            "no local-app registration/update/package-build authority created by publication",
            "no Agent Execute/ActionPermit/general network authority",
            "no Stable Core/interface-registry promotion"
        };
        var authority = new FixedGitHubPublicationAuthorityReceipt(
            AuthoritySchema, "human-operator-at-workbench-ui", "workbench.accepted-source.publish-fixed-github", root,
            RemoteName, RemoteUrl, "explicit Publish accepted confirmation after local v0.37.1 acceptance",
            true, true, true, false, false, false, false, false,
            new[]
            {
                "git remote get-url github-workbench",
                "git remote add github-workbench <fixed-url> only when absent",
                "git ls-remote github-workbench refs/heads/main",
                "git ls-remote github-workbench refs/tags/workbench-v0.37.1-accepted",
                "git push github-workbench <exact-head>:refs/heads/main when remote main == exact parent",
                "git push github-workbench refs/tags/workbench-v0.37.1-accepted:refs/tags/workbench-v0.37.1-accepted when remote tag absent"
            }, nonEffects);
        return new FixedGitHubPublicationReceipt(
            ReceiptSchema, Version, DateTimeOffset.Now, RemoteName, RemoteUrl, candidate.Head, candidate.Parent, AcceptedTag,
            remoteMainBefore, remoteTagBefore, remoteAdded, mainPush, tagPush, recovery, remoteMainAfter, remoteTagAfter,
            true, true, authority, nonEffects,
            "Explicit human publication of accepted v0.37.1 role-separation stabilization. No candidate import/local-app/lifecycle/Agent Execute/general network authority is created.");
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, FixedGitHubPublicationReceipt receipt, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot); var dir = Path.Combine(root, "artifacts", "publication"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.37.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken); return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v0371-fixed-tag", AcceptedTag == "workbench-v0.37.1-accepted", AcceptedTag, "workbench-v0.37.1-accepted"),
        ("publisher-v0371-fixed-parent", ExpectedParent == "0d20e3bbe7c28b48cac3ef97b903b4a3a6176521", ExpectedParent, "accepted v0.37"),
        ("publisher-v0371-conflict-refused", ClassifyRemoteMain("a","b","c") == "CONFLICT" && ClassifyRemoteTag("b","c") == "CONFLICT", "CONFLICT/CONFLICT", "CONFLICT/CONFLICT")
    };
    public static string ClassifyRemoteMain(string parent, string head, string remote) => remote.Equals(parent, StringComparison.OrdinalIgnoreCase) ? "PARENT" : remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";
    public static string ClassifyRemoteTag(string head, string? remote) => string.IsNullOrWhiteSpace(remote) ? "ABSENT" : remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";

    private static async Task ReverifyLocalCandidateAsync(FixedGitHubPublicationCandidate c, CancellationToken ct)
    {
        var status = await RunGitAsync(c.RepositoryRoot, ct, "status", "--porcelain=v1", "--untracked-files=all");
        var head = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD^")).Stdout, "parent");
        var tag = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!string.IsNullOrWhiteSpace(status.Stdout) || !head.Equals(c.Head, StringComparison.OrdinalIgnoreCase) || !parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase) || !tag.Equals(c.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted v0.37.1 frontier changed after publication preview.");
    }
    private static async Task<bool> EnsureFixedRemoteAsync(string root, CancellationToken ct) { var current = await GetConfiguredRemoteUrlAsync(root, ct); if (string.IsNullOrWhiteSpace(current)) { await RunGitAsync(root, ct, "remote", "add", RemoteName, RemoteUrl); return true; } if (!SameRemoteUrl(current, RemoteUrl)) throw new InvalidDataException("Fixed remote URL conflict."); return false; }
    private static async Task<string?> GetConfiguredRemoteUrlAsync(string root, CancellationToken ct) { var r = await RunGitAsync(root, ct, true, "remote", "get-url", RemoteName); return r.ExitCode == 0 ? r.Stdout.Trim() : null; }
    private static async Task<string?> ReadRemoteMainAsync(string root, CancellationToken ct) { var r = await RunGitAsync(root, ct, "ls-remote", RemoteName, "refs/heads/main"); var line = SplitLines(r.Stdout).SingleOrDefault(); return line is null ? null : ParseLsRemoteSha(line); }
    private static async Task<string?> ReadRemoteTagCommitAsync(string root, CancellationToken ct, string tag) { var r = await RunGitAsync(root, ct, "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}"); var lines = SplitLines(r.Stdout); var peeled = lines.FirstOrDefault(x => x.EndsWith($"refs/tags/{tag}^{{}}", StringComparison.Ordinal)); if (peeled is not null) return ParseLsRemoteSha(peeled); var direct = lines.FirstOrDefault(x => x.EndsWith($"refs/tags/{tag}", StringComparison.Ordinal)); return direct is null ? null : ParseLsRemoteSha(direct); }
    private static string ParseLsRemoteSha(string line) { var tab = line.IndexOf('\t'); return RequireSha((tab >= 0 ? line[..tab] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).Trim(), "remote ref"); }
    private static string RequireSha(string value, string role) { var sha = value.Trim(); if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}"); return sha.ToLowerInvariant(); }
    private static bool SameRemoteUrl(string a, string b) => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    private static string[] SplitLines(string t) => t.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string ResolveRepositoryRoot(string workspaceRoot) { var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench")); if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}"); return root; }
    private static Task<GitResult> RunGitAsync(string root, CancellationToken ct, params string[] args) => RunGitAsync(root, ct, false, args);
    private static async Task<GitResult> RunGitAsync(string root, CancellationToken ct, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat"; psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi }; if (!process.Start()) throw new InvalidDataException("Failed to start v0.37.1 publication Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { try { process.Kill(true); } catch { } throw new InvalidDataException("v0.37.1 publication Git operation timed out."); }
        var stdout = await stdoutTask; var stderr = await stderrTask; if (process.ExitCode != 0 && !allowFailure) throw new InvalidDataException($"v0.37.1 publication Git operation failed: {stderr.Trim()}"); return new GitResult(process.ExitCode, stdout, stderr);
    }
    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
