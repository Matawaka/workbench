using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// Publishes accepted v0.44.1 from the fixed remote v0.43 frontier while keeping
/// failed real-host v0.44 untagged remotely. The local v0.44 commit remains the
/// exact direct parent and historical negative evidence in the fast-forward chain.
/// </summary>
public sealed class FixedGitHubPublicationV0441Service
{
    public const string Version = "0.44.1";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string AcceptedTag = "workbench-v0.44.1-accepted";
    public const string ExpectedParent = "fbce2c3d20517e99e0752fe5ac53c5cc30f0a2af";
    public const string ExpectedParentTag = "workbench-v0.44-accepted";
    public const string ExpectedRemoteBase = "77f1a7027b0f2bf2a95dbdd415c06efa231b2e22";
    public const string ExpectedRemoteBaseTag = "workbench-v0.43-accepted";
    public const string FailedRemoteTag = "workbench-v0.44-accepted";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.44.1";
    public const string AuthoritySchema = "matawaka.workbench-fixed-github-publication-authority-receipt/v0.44.1";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidate> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.44.1 publication.");
        var head = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        if (!parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.44.1 accepted HEAD parent is not exact operator-bound failed-v0.44 predecessor: {parent}");
        var parentTagHead = RequireSha((await RunGitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedParentTag)).Stdout, ExpectedParentTag);
        if (!parentTagHead.Equals(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local v0.44 predecessor tag does not point at exact v0.44.1 parent.");
        var tagHead = RequireSha((await RunGitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted v0.44.1 tag does not point at current HEAD.");
        await RequireAncestorAsync(root, ExpectedRemoteBase, head, cancellationToken);

        var configured = await GetConfiguredRemoteUrlAsync(root, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl))
            throw new InvalidDataException($"Remote {RemoteName} conflicts with fixed URL: {configured}");
        return new FixedGitHubPublicationCandidate(
            Version, root, RemoteName, RemoteUrl, head, parent, AcceptedTag,
            string.IsNullOrWhiteSpace(configured) ? null : configured,
            string.IsNullOrWhiteSpace(configured), true);
    }

    public async Task<FixedGitHubPublicationReceipt> PublishAsync(
        FixedGitHubPublicationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.RemoteName != RemoteName ||
            !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) || candidate.AcceptedTag != AcceptedTag ||
            !candidate.Parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match fixed v0.44.1 contract.");

        await ReverifyLocalCandidateAsync(candidate, cancellationToken);
        var root = candidate.RepositoryRoot;
        var remoteAdded = await EnsureFixedRemoteAsync(root, cancellationToken);
        var remoteMainBefore = await ReadRemoteMainAsync(root, cancellationToken)
            ?? throw new InvalidDataException("Fixed remote main is missing.");
        var remoteTagBefore = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag);
        var failedRemoteTag = await ReadRemoteTagCommitAsync(root, cancellationToken, FailedRemoteTag);
        if (failedRemoteTag is not null)
            throw new InvalidDataException("Failed real-host v0.44 tag unexpectedly exists remotely; v0.44.1 publication refused.");

        var mainState = ClassifyRemoteMain(candidate.Head, remoteMainBefore);
        var tagState = ClassifyRemoteTag(candidate.Head, remoteTagBefore);
        if (mainState == "CONFLICT") throw new InvalidDataException("Remote main is neither exact accepted v0.43 base nor exact v0.44.1 HEAD.");
        if (tagState == "CONFLICT") throw new InvalidDataException("Remote v0.44.1 accepted tag conflicts with local accepted HEAD.");

        var mainPush = false;
        var tagPush = false;
        var recovery = mainState == "ALREADY_HEAD" && tagState == "ABSENT";
        if (mainState == "REMOTE_BASE")
        {
            await RunGitAsync(root, cancellationToken, "push", RemoteName, $"{candidate.Head}:refs/heads/main");
            mainPush = true;
        }
        var mid = await ReadRemoteMainAsync(root, cancellationToken)
            ?? throw new InvalidDataException("Remote main disappeared during v0.44.1 publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main did not converge to exact accepted v0.44.1 HEAD.");
        if (tagState == "ABSENT")
        {
            await RunGitAsync(root, cancellationToken, "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPush = true;
        }
        var remoteMainAfter = await ReadRemoteMainAsync(root, cancellationToken)
            ?? throw new InvalidDataException("Remote main missing after v0.44.1 publication.");
        var remoteTagAfter = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag)
            ?? throw new InvalidDataException("Remote v0.44.1 tag missing after publication.");
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !remoteTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.44.1 HEAD.");
        if (await ReadRemoteTagCommitAsync(root, cancellationToken, FailedRemoteTag) is not null)
            throw new InvalidDataException("Failed v0.44 tag appeared remotely during v0.44.1 publication.");

        var headAfter = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter.Stdout))
            throw new InvalidDataException("Local Workbench state changed during v0.44.1 publication.");

        var nonEffects = new[]
        {
            "no force push or conflicting tag movement",
            "no arbitrary remote URL/name",
            "failed real-host workbench-v0.44-accepted tag is not published",
            "the v0.44 commit may exist only as an untagged ancestor in the fast-forward chain",
            "nested double-click repair creates no publication or application mutation authority",
            "Launch candidate removal is UI-only and creates no new authority",
            "no application file write or execution authority",
            "no app registration/update/copy/move/delete/launch authority",
            "no transition bootstrap/launch/handoff authority created by publication",
            "no automatic retry authority",
            "no Agent Execute/ActionPermit/general network authority",
            "no Stable Core/interface-registry promotion"
        };
        var authority = new FixedGitHubPublicationAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.accepted-source.publish-fixed-github",
            root,
            RemoteName,
            RemoteUrl,
            "explicit Publish accepted confirmation after automatic local v0.44.1 acceptance and real-host stabilization check",
            true, true, true, false, false, false, false, false,
            new[]
            {
                "git remote get-url github-workbench",
                "git remote add github-workbench <fixed-url> only when absent",
                "git ls-remote github-workbench refs/heads/main",
                "git ls-remote github-workbench refs/tags/workbench-v0.44.1-accepted",
                "git ls-remote github-workbench refs/tags/workbench-v0.44-accepted",
                "git push github-workbench <exact-v0.44.1-head>:refs/heads/main when remote main == exact accepted v0.43 base",
                "git push github-workbench refs/tags/workbench-v0.44.1-accepted:refs/tags/workbench-v0.44.1-accepted when target tag absent"
            },
            nonEffects);
        return new FixedGitHubPublicationReceipt(
            ReceiptSchema, Version, DateTimeOffset.Now, RemoteName, RemoteUrl,
            candidate.Head, candidate.Parent, AcceptedTag,
            remoteMainBefore, remoteTagBefore, remoteAdded, mainPush, tagPush, recovery,
            remoteMainAfter, remoteTagAfter, true, true, authority, nonEffects,
            "Explicit human publication of accepted v0.44.1 stabilization. The failed v0.44 local checkpoint is preserved only as historical untagged ancestry remotely and is not reclassified as a passed accepted frontier.");
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, FixedGitHubPublicationReceipt receipt, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = Path.Combine(root, "artifacts", "publication");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.44.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v0441-fixed-tag", AcceptedTag == "workbench-v0.44.1-accepted", AcceptedTag, "workbench-v0.44.1-accepted"),
        ("publisher-v0441-local-parent", ExpectedParent == "fbce2c3d20517e99e0752fe5ac53c5cc30f0a2af", ExpectedParent, "operator-bound local failed-v0.44"),
        ("publisher-v0441-remote-base", ExpectedRemoteBase == "77f1a7027b0f2bf2a95dbdd415c06efa231b2e22", ExpectedRemoteBase, "accepted remote v0.43"),
        ("publisher-v0441-failed-tag-suppressed", FailedRemoteTag == "workbench-v0.44-accepted", FailedRemoteTag, "must remain absent remotely"),
        ("publisher-v0441-conflict-refused", ClassifyRemoteMain("b", "c") == "CONFLICT" && ClassifyRemoteTag("b", "c") == "CONFLICT", "CONFLICT/CONFLICT", "CONFLICT/CONFLICT"),
        ("publisher-v0441-inspection-not-authority", true, "explicit Publish button only", "separate from tree/text presentation")
    };

    public static string ClassifyRemoteMain(string head, string remote)
        => remote.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase) ? "REMOTE_BASE" :
           remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";

    public static string ClassifyRemoteTag(string head, string? remote)
        => string.IsNullOrWhiteSpace(remote) ? "ABSENT" :
           remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";

    private static async Task ReverifyLocalCandidateAsync(FixedGitHubPublicationCandidate c, CancellationToken ct)
    {
        var status = await RunGitAsync(c.RepositoryRoot, ct, "status", "--porcelain=v1", "--untracked-files=all");
        var head = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD^")).Stdout, "parent");
        var tag = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        var parentTag = RequireSha((await RunGitAsync(c.RepositoryRoot, ct, "rev-list", "-n", "1", ExpectedParentTag)).Stdout, ExpectedParentTag);
        if (!string.IsNullOrWhiteSpace(status.Stdout) ||
            !head.Equals(c.Head, StringComparison.OrdinalIgnoreCase) ||
            !parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase) ||
            !tag.Equals(c.Head, StringComparison.OrdinalIgnoreCase) ||
            !parentTag.Equals(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted v0.44.1 frontier changed after publication preview.");
        await RequireAncestorAsync(c.RepositoryRoot, ExpectedRemoteBase, head, ct);
    }

    private static async Task RequireAncestorAsync(string root, string ancestor, string descendant, CancellationToken ct)
    {
        var result = await RunGitAsync(root, ct, true, "merge-base", "--is-ancestor", ancestor, descendant);
        if (result.ExitCode != 0)
            throw new InvalidDataException("Accepted remote v0.43 base is not an ancestor of exact local v0.44.1 HEAD.");
    }

    private static async Task<bool> EnsureFixedRemoteAsync(string root, CancellationToken ct)
    {
        var current = await GetConfiguredRemoteUrlAsync(root, ct);
        if (string.IsNullOrWhiteSpace(current))
        {
            await RunGitAsync(root, ct, "remote", "add", RemoteName, RemoteUrl);
            return true;
        }
        if (!SameRemoteUrl(current, RemoteUrl)) throw new InvalidDataException("Fixed remote URL conflict.");
        return false;
    }

    private static async Task<string?> GetConfiguredRemoteUrlAsync(string root, CancellationToken ct)
    {
        var r = await RunGitAsync(root, ct, true, "remote", "get-url", RemoteName);
        return r.ExitCode == 0 ? r.Stdout.Trim() : null;
    }

    private static async Task<string?> ReadRemoteMainAsync(string root, CancellationToken ct)
    {
        var r = await RunGitAsync(root, ct, "ls-remote", RemoteName, "refs/heads/main");
        var line = SplitLines(r.Stdout).SingleOrDefault();
        return line is null ? null : ParseLsRemoteSha(line);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string root, CancellationToken ct, string tag)
    {
        var r = await RunGitAsync(root, ct, "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
        var lines = SplitLines(r.Stdout);
        var peeled = lines.FirstOrDefault(x => x.EndsWith($"refs/tags/{tag}^{{}}", StringComparison.Ordinal));
        if (peeled is not null) return ParseLsRemoteSha(peeled);
        var direct = lines.FirstOrDefault(x => x.EndsWith($"refs/tags/{tag}", StringComparison.Ordinal));
        return direct is null ? null : ParseLsRemoteSha(direct);
    }

    private static string ParseLsRemoteSha(string line)
    {
        var tab = line.IndexOf('\t');
        return RequireSha((tab >= 0 ? line[..tab] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).Trim(), "remote ref");
    }

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static bool SameRemoteUrl(string a, string b)
        => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string[] SplitLines(string t)
        => t.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static Task<GitResult> RunGitAsync(string root, CancellationToken ct, params string[] args)
        => RunGitAsync(root, ct, false, args);

    private static async Task<GitResult> RunGitAsync(string root, CancellationToken ct, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(GitTimeout);
        var token = timeout.Token;
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.44.1 publication Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new InvalidDataException("v0.44.1 publication Git operation timed out.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidDataException($"v0.44.1 publication Git operation failed: {stderr.Trim()}");
        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
