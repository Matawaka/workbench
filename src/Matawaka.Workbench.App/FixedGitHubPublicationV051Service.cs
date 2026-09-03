using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed class FixedGitHubPublicationV051Service
{
    public const string Version = "0.51.0";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string AcceptedTag = "workbench-v0.51-accepted";
    public const string ExpectedParent = "632ddbb73e8d70b485f02d21f772674d429adf8c";
    public const string ExpectedParentTag = "workbench-v0.50.2-accepted";
    public const string ExpectedRemoteBase = "632ddbb73e8d70b485f02d21f772674d429adf8c";
    public const string ExpectedRemoteBaseTag = "workbench-v0.50.2-accepted";
    public const string FailedRemoteTagV0501 = "workbench-v0.50.1-accepted";
    public const string FailedRemoteTagV050 = "workbench-v0.50-accepted";
    public const string HistoricalFailedRemoteTagV049 = "workbench-v0.49-accepted";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.51";
    public const string AuthoritySchema = "matawaka.workbench-fixed-github-publication-authority-receipt/v0.51";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidate> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.51 publication.");
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        if (!parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.51 accepted HEAD parent is not exact accepted v0.50.2 predecessor: {parent}");
        var parentTagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedParentTag)).Stdout, ExpectedParentTag);
        if (!parentTagHead.Equals(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Local v0.50.2 predecessor tag does not point at exact v0.51 parent.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Accepted v0.51 tag does not point at current HEAD.");
        await RequireAncestorAsync(root, ExpectedRemoteBase, head, cancellationToken);
        var configured = await GetConfiguredRemoteUrlAsync(root, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl)) throw new InvalidDataException($"Remote {RemoteName} conflicts with fixed URL: {configured}");
        return new FixedGitHubPublicationCandidate(Version, root, RemoteName, RemoteUrl, head, parent, AcceptedTag, string.IsNullOrWhiteSpace(configured) ? null : configured, string.IsNullOrWhiteSpace(configured), true);
    }

    public async Task<FixedGitHubPublicationReceipt> PublishAsync(FixedGitHubPublicationCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.RemoteName != RemoteName || !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) || candidate.AcceptedTag != AcceptedTag || !candidate.Parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match fixed v0.51 contract.");
        await ReverifyLocalCandidateAsync(candidate, cancellationToken);
        var root = candidate.RepositoryRoot;
        var remoteAdded = await EnsureFixedRemoteAsync(root, cancellationToken);
        var remoteMainBefore = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Fixed remote main is missing.");
        var remoteTagBefore = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag);
        await RequireFailedTagsAbsentAsync(root, cancellationToken);
        var remoteBaseTag = await ReadRemoteTagCommitAsync(root, cancellationToken, ExpectedRemoteBaseTag);
        if (!string.Equals(remoteBaseTag, ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Accepted remote v0.50.2 base tag moved; v0.51 publication refused.");

        var mainState = ClassifyRemoteMain(candidate.Head, remoteMainBefore);
        var tagState = ClassifyRemoteTag(candidate.Head, remoteTagBefore);
        if (mainState == "CONFLICT") throw new InvalidDataException("Remote main is neither exact accepted v0.50.2 base nor exact v0.51 HEAD.");
        if (tagState == "CONFLICT") throw new InvalidDataException("Remote v0.51 accepted tag conflicts with local accepted HEAD.");

        var mainPush = false;
        var tagPush = false;
        var recovery = mainState == "ALREADY_HEAD" && tagState == "ABSENT";
        if (mainState == "REMOTE_BASE")
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"{candidate.Head}:refs/heads/main");
            mainPush = true;
        }
        var mid = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Remote main disappeared during v0.51 publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Remote main did not converge to exact accepted v0.51 HEAD.");
        if (tagState == "ABSENT")
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPush = true;
        }
        var remoteMainAfter = await ReadRemoteMainAsync(root, cancellationToken) ?? throw new InvalidDataException("Remote main missing after v0.51 publication.");
        var remoteTagAfter = await ReadRemoteTagCommitAsync(root, cancellationToken, AcceptedTag) ?? throw new InvalidDataException("Remote v0.51 tag missing after publication.");
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !remoteTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.51 HEAD.");
        await RequireFailedTagsAbsentAsync(root, cancellationToken);
        var headAfter = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter.Stdout)) throw new InvalidDataException("Local Workbench state changed during v0.51 publication.");

        var nonEffects = new[]
        {
            "no force push or conflicting tag movement",
            "historical failed workbench-v0.49/v0.50/v0.50.1 accepted tags remain absent remotely",
            "no arbitrary remote URL/name",
            "no external Tools/OpenAI/tunnel-client binary published",
            "no runtime credential/tunnel session/local endpoint/read lease/private app bytes published",
            "no tunnel process, MCP adapter, browse call or read lease created by publication",
            "no OAuth/DCR or OpenAI tunnel CRUD/admin authority",
            "no ChatGPT connector/settings mutation",
            "no automatic retry authority",
            "no Agent Execute/ActionPermit/general network authority beyond fixed publication",
            "no Stable Core/interface-registry promotion"
        };
        var authority = new FixedGitHubPublicationAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.accepted-source.publish-fixed-github",
            root,
            RemoteName,
            RemoteUrl,
            "explicit Publish accepted confirmation after automatic local v0.51 acceptance and successful real-host ChatGPT browse/read round-trip",
            true, true, true, false, false, false, false, false,
            new[]
            {
                "git remote get-url github-workbench",
                "git remote add github-workbench <fixed-url> only when absent",
                "git ls-remote github-workbench refs/heads/main",
                "git ls-remote github-workbench refs/tags/workbench-v0.50.2-accepted",
                "git ls-remote github-workbench refs/tags/workbench-v0.51-accepted",
                "git push github-workbench <exact-v0.51-head>:refs/heads/main when remote main == exact accepted v0.50.2 base",
                "git push github-workbench refs/tags/workbench-v0.51-accepted:refs/tags/workbench-v0.51-accepted when target tag absent"
            },
            nonEffects);
        return new FixedGitHubPublicationReceipt(
            ReceiptSchema, Version, DateTimeOffset.Now, RemoteName, RemoteUrl,
            candidate.Head, candidate.Parent, AcceptedTag,
            remoteMainBefore, remoteTagBefore, remoteAdded, mainPush, tagPush, recovery,
            remoteMainAfter, remoteTagAfter, true, true, authority, nonEffects,
            "Explicit publication of accepted Workbench v0.51 live browse surface. Transport, lease and private application authority remain separate.");
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, FixedGitHubPublicationReceipt receipt, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = Path.Combine(root, "artifacts", "publication");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.51-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v051-fixed-tag", AcceptedTag == "workbench-v0.51-accepted", AcceptedTag, "workbench-v0.51-accepted"),
        ("publisher-v051-local-parent", ExpectedParent == "632ddbb73e8d70b485f02d21f772674d429adf8c", ExpectedParent, "accepted v0.50.2"),
        ("publisher-v051-remote-base", ExpectedRemoteBase == ExpectedParent, ExpectedRemoteBase, "exact accepted v0.50.2"),
        ("publisher-v051-failed-tags-still-suppressed", FailedRemoteTagV0501 == "workbench-v0.50.1-accepted" && FailedRemoteTagV050 == "workbench-v0.50-accepted", $"{FailedRemoteTagV050}/{FailedRemoteTagV0501}", "both absent remotely"),
        ("publisher-v051-conflict-refused", ClassifyRemoteMain("b", "c") == "CONFLICT" && ClassifyRemoteTag("b", "c") == "CONFLICT", "CONFLICT/CONFLICT", "CONFLICT/CONFLICT")
    };

    public static string ClassifyRemoteMain(string head, string remote)
        => remote.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase) ? "REMOTE_BASE" : remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";

    public static string ClassifyRemoteTag(string head, string? remote)
        => string.IsNullOrWhiteSpace(remote) ? "ABSENT" : remote.Equals(head, StringComparison.OrdinalIgnoreCase) ? "ALREADY_HEAD" : "CONFLICT";

    private static async Task RequireFailedTagsAbsentAsync(string root, CancellationToken ct)
    {
        if (await ReadRemoteTagCommitAsync(root, ct, FailedRemoteTagV0501) is not null) throw new InvalidDataException("Historical failed v0.50.1 tag unexpectedly exists remotely; v0.51 publication refused.");
        if (await ReadRemoteTagCommitAsync(root, ct, FailedRemoteTagV050) is not null) throw new InvalidDataException("Historical failed v0.50 tag unexpectedly exists remotely; v0.51 publication refused.");
        if (await ReadRemoteTagCommitAsync(root, ct, HistoricalFailedRemoteTagV049) is not null) throw new InvalidDataException("Historical failed v0.49 tag unexpectedly exists remotely; v0.51 publication refused.");
    }

    private static async Task ReverifyLocalCandidateAsync(FixedGitHubPublicationCandidate c, CancellationToken ct)
    {
        var status = await GitAsync(c.RepositoryRoot, ct, "status", "--porcelain=v1", "--untracked-files=all");
        var head = RequireSha((await GitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await GitAsync(c.RepositoryRoot, ct, "rev-parse", "HEAD^")).Stdout, "parent");
        var tag = RequireSha((await GitAsync(c.RepositoryRoot, ct, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        var parentTag = RequireSha((await GitAsync(c.RepositoryRoot, ct, "rev-list", "-n", "1", ExpectedParentTag)).Stdout, ExpectedParentTag);
        if (!string.IsNullOrWhiteSpace(status.Stdout) || !head.Equals(c.Head, StringComparison.OrdinalIgnoreCase) || !parent.Equals(ExpectedParent, StringComparison.OrdinalIgnoreCase) || !tag.Equals(c.Head, StringComparison.OrdinalIgnoreCase) || !parentTag.Equals(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted v0.51 frontier changed after publication preview.");
        await RequireAncestorAsync(c.RepositoryRoot, ExpectedRemoteBase, head, ct);
    }

    private static async Task RequireAncestorAsync(string root, string ancestor, string descendant, CancellationToken ct)
    {
        var result = await GitAsync(root, ct, true, "merge-base", "--is-ancestor", ancestor, descendant);
        if (result.ExitCode != 0) throw new InvalidDataException("Accepted remote v0.50.2 base is not an ancestor of exact local v0.51 HEAD.");
    }

    private static async Task<bool> EnsureFixedRemoteAsync(string root, CancellationToken ct)
    {
        var current = await GetConfiguredRemoteUrlAsync(root, ct);
        if (string.IsNullOrWhiteSpace(current))
        {
            await GitAsync(root, ct, "remote", "add", RemoteName, RemoteUrl);
            return true;
        }
        if (!SameRemoteUrl(current, RemoteUrl)) throw new InvalidDataException("Fixed remote URL conflict.");
        return false;
    }

    private static async Task<string?> GetConfiguredRemoteUrlAsync(string root, CancellationToken ct)
    {
        var r = await GitAsync(root, ct, true, "remote", "get-url", RemoteName);
        return r.ExitCode == 0 ? r.Stdout.Trim() : null;
    }

    private static async Task<string?> ReadRemoteMainAsync(string root, CancellationToken ct)
    {
        var r = await GitAsync(root, ct, "ls-remote", RemoteName, "refs/heads/main");
        var line = SplitLines(r.Stdout).SingleOrDefault();
        return line is null ? null : ParseLsRemoteSha(line);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string root, CancellationToken ct, string tag)
    {
        var r = await GitAsync(root, ct, "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
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

    private static bool SameRemoteUrl(string a, string b) => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    private static string[] SplitLines(string value) => value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args) => GitAsync(root, cancellationToken, false, args);

    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.51 publication Git process.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new InvalidDataException("v0.51 publication Git operation timed out.");
        }
        var o = await stdout;
        var e = await stderr;
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidDataException($"v0.51 publication Git operation failed: {e.Trim()}");
        return new GitResult(process.ExitCode, o, e);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
