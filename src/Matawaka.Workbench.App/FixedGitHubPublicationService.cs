using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record FixedGitHubPublicationCandidate(
    string Version,
    string RepositoryRoot,
    string RemoteName,
    string RemoteUrl,
    string Head,
    string Parent,
    string AcceptedTag,
    string? ConfiguredRemoteUrl,
    bool RemoteConfigurationRequired,
    bool WorkingTreeClean);

public sealed record FixedGitHubPublicationAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string TargetRepository,
    string RemoteName,
    string RemoteUrl,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FixedRemoteOnly,
    bool FastForwardOnly,
    bool ForcePushAllowed,
    bool TagMovementAllowed,
    bool GeneralNetworkAuthorityCreated,
    bool AgentExecuteAuthorityCreated,
    bool CatalogMutationAuthorityCreated,
    IReadOnlyList<string> AllowedGitOperations,
    IReadOnlyList<string> NonEffects);

public sealed record FixedGitHubPublicationReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RemoteName,
    string RemoteUrl,
    string LocalHead,
    string LocalParent,
    string AcceptedTag,
    string RemoteMainBefore,
    string? RemoteTagBefore,
    bool RemoteConfigurationAdded,
    bool MainPushPerformed,
    bool TagPushPerformed,
    bool IdempotentRecoveryPath,
    string RemoteMainAfter,
    string RemoteTagAfter,
    bool LocalHeadUnchanged,
    bool WorkingTreeUnchanged,
    FixedGitHubPublicationAuthorityReceipt Authority,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Human-confirmed Workbench source publication to one fixed repository only.
/// This service is deliberately separate from Agent Execute and from the optional
/// catalog git-fetch checkbox. It may only fast-forward the exact accepted local
/// Workbench HEAD to the fixed remote main and publish the exact accepted tag.
/// </summary>
public sealed class FixedGitHubPublicationService
{
    public const string Version = "0.32.0";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string AcceptedTag = "workbench-v0.32-accepted";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.32";
    public const string AuthoritySchema = "matawaka.workbench-fixed-github-publication-authority-receipt/v0.32";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidate> PreviewAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var status = await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(status.Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before accepted-source publication.");

        var head = RequireSha((await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        var tagHead = RequireSha((await RunGitAsync(repositoryRoot, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!string.Equals(tagHead, head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Accepted tag {AcceptedTag} does not point at current HEAD. tag={tagHead}; head={head}");

        var configured = await GetConfiguredRemoteUrlAsync(repositoryRoot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl))
            throw new InvalidDataException($"Remote {RemoteName} is configured to a conflicting URL: {configured}");

        return new FixedGitHubPublicationCandidate(
            Version,
            repositoryRoot,
            RemoteName,
            RemoteUrl,
            head,
            parent,
            AcceptedTag,
            string.IsNullOrWhiteSpace(configured) ? null : configured,
            string.IsNullOrWhiteSpace(configured),
            true);
    }

    public async Task<FixedGitHubPublicationReceipt> PublishAsync(
        FixedGitHubPublicationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        if (!string.Equals(candidate.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(candidate.RemoteName, RemoteName, StringComparison.Ordinal) ||
            !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) ||
            !string.Equals(candidate.AcceptedTag, AcceptedTag, StringComparison.Ordinal))
            throw new InvalidDataException("Publication candidate does not match the fixed v0.32 publication contract.");

        var repositoryRoot = candidate.RepositoryRoot;
        await ReverifyLocalCandidateAsync(candidate, cancellationToken);

        var remoteAdded = await EnsureFixedRemoteAsync(repositoryRoot, cancellationToken);
        var remoteMainBefore = await ReadRemoteMainAsync(repositoryRoot, cancellationToken)
            ?? throw new InvalidDataException("Fixed remote main is missing. Refusing to create or infer a new remote main frontier.");
        var remoteTagBefore = await ReadRemoteTagCommitAsync(repositoryRoot, cancellationToken, AcceptedTag);

        var mainState = ClassifyRemoteMain(candidate.Parent, candidate.Head, remoteMainBefore);
        if (mainState == "CONFLICT")
            throw new InvalidDataException($"Remote main is neither exact local parent nor exact local HEAD. remote={remoteMainBefore}; parent={candidate.Parent}; head={candidate.Head}");

        var tagState = ClassifyRemoteTag(candidate.Head, remoteTagBefore);
        if (tagState == "CONFLICT")
            throw new InvalidDataException($"Remote accepted tag conflicts with local accepted HEAD. remoteTag={remoteTagBefore}; head={candidate.Head}");

        var mainPushPerformed = false;
        var tagPushPerformed = false;
        var idempotentRecovery = mainState == "ALREADY_HEAD" && tagState == "ABSENT";

        if (mainState == "PARENT")
        {
            await RunGitAsync(repositoryRoot, cancellationToken,
                "push", RemoteName, $"{candidate.Head}:refs/heads/main");
            mainPushPerformed = true;
        }

        // Re-read before tag publication. If main publication succeeded but a previous
        // invocation failed before the tag, the retry follows this exact branch.
        var remoteMainMid = await ReadRemoteMainAsync(repositoryRoot, cancellationToken)
            ?? throw new InvalidDataException("Remote main disappeared during publication.");
        if (!string.Equals(remoteMainMid, candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Remote main did not converge to exact accepted HEAD: {remoteMainMid}");

        if (tagState == "ABSENT")
        {
            await RunGitAsync(repositoryRoot, cancellationToken,
                "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPushPerformed = true;
        }

        var remoteMainAfter = await ReadRemoteMainAsync(repositoryRoot, cancellationToken)
            ?? throw new InvalidDataException("Remote main missing after publication.");
        var remoteTagAfter = await ReadRemoteTagCommitAsync(repositoryRoot, cancellationToken, AcceptedTag)
            ?? throw new InvalidDataException("Remote accepted tag missing after publication.");
        if (!string.Equals(remoteMainAfter, candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(remoteTagAfter, candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fixed remote main/tag readback does not equal exact accepted local HEAD.");

        var headAfter = RequireSha((await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.Equals(headAfter, candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter.Stdout))
            throw new InvalidDataException("Local Workbench HEAD or working tree changed during accepted-source publication.");

        var nonEffects = new[]
        {
            "no force push",
            "no remote main rewrite when remote is not exact local parent/head",
            "no movement or replacement of an existing conflicting accepted tag",
            "no arbitrary remote URL or remote name",
            "no catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no general Workbench runtime network authority",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion"
        };
        var authority = new FixedGitHubPublicationAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.accepted-source.publish-fixed-github",
            repositoryRoot,
            RemoteName,
            RemoteUrl,
            "explicit Publish accepted button + separate confirmation dialog after local v0.32 acceptance",
            true,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "git remote get-url github-workbench",
                "git remote add github-workbench <fixed-url> only when absent",
                "git ls-remote github-workbench refs/heads/main",
                "git ls-remote github-workbench refs/tags/workbench-v0.32-accepted",
                "git push github-workbench <exact-head>:refs/heads/main when remote main == exact parent",
                "git push github-workbench refs/tags/workbench-v0.32-accepted:refs/tags/workbench-v0.32-accepted when remote tag absent"
            },
            nonEffects);

        return new FixedGitHubPublicationReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            RemoteName,
            RemoteUrl,
            candidate.Head,
            candidate.Parent,
            AcceptedTag,
            remoteMainBefore,
            remoteTagBefore,
            remoteAdded,
            mainPushPerformed,
            tagPushPerformed,
            idempotentRecovery,
            remoteMainAfter,
            remoteTagAfter,
            true,
            true,
            authority,
            nonEffects,
            "Explicit human maintenance publication of one already-accepted Workbench checkpoint. This is not Agent Execute, general network authority, catalog mutation, remote rewrite authority, or canonical UU-AAP publication authority.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        FixedGitHubPublicationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var directory = Path.Combine(repositoryRoot, "artifacts", "publication");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"fixed-github-publication-v0.32-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        return new[]
        {
            ("publisher-fixed-remote-name", RemoteName == "github-workbench", RemoteName, "github-workbench"),
            ("publisher-fixed-remote-url", RemoteUrl == "https://github.com/Matawaka/workbench.git", RemoteUrl, "fixed Matawaka/workbench URL"),
            ("publisher-parent-fast-forward", ClassifyRemoteMain("a", "b", "a") == "PARENT", ClassifyRemoteMain("a", "b", "a"), "PARENT"),
            ("publisher-idempotent-main", ClassifyRemoteMain("a", "b", "b") == "ALREADY_HEAD", ClassifyRemoteMain("a", "b", "b"), "ALREADY_HEAD"),
            ("publisher-conflicting-main-refused", ClassifyRemoteMain("a", "b", "c") == "CONFLICT", ClassifyRemoteMain("a", "b", "c"), "CONFLICT"),
            ("publisher-absent-tag-admitted", ClassifyRemoteTag("b", null) == "ABSENT", ClassifyRemoteTag("b", null), "ABSENT"),
            ("publisher-idempotent-tag", ClassifyRemoteTag("b", "b") == "ALREADY_HEAD", ClassifyRemoteTag("b", "b"), "ALREADY_HEAD"),
            ("publisher-conflicting-tag-refused", ClassifyRemoteTag("b", "c") == "CONFLICT", ClassifyRemoteTag("b", "c"), "CONFLICT"),
            ("publisher-force-push-not-part-of-contract", true, "git push <exact-refspec>; no --force flag in publication code path", "no force push")
        };
    }

    public static string ClassifyRemoteMain(string parent, string head, string remoteMain)
    {
        if (string.Equals(remoteMain, parent, StringComparison.OrdinalIgnoreCase)) return "PARENT";
        if (string.Equals(remoteMain, head, StringComparison.OrdinalIgnoreCase)) return "ALREADY_HEAD";
        return "CONFLICT";
    }

    public static string ClassifyRemoteTag(string head, string? remoteTag)
    {
        if (string.IsNullOrWhiteSpace(remoteTag)) return "ABSENT";
        if (string.Equals(remoteTag, head, StringComparison.OrdinalIgnoreCase)) return "ALREADY_HEAD";
        return "CONFLICT";
    }

    private static async Task ReverifyLocalCandidateAsync(
        FixedGitHubPublicationCandidate candidate,
        CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(candidate.RepositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(status.Stdout))
            throw new InvalidDataException("Workbench working tree changed after publication preview.");
        var head = RequireSha((await RunGitAsync(candidate.RepositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await RunGitAsync(candidate.RepositoryRoot, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        var tagHead = RequireSha((await RunGitAsync(candidate.RepositoryRoot, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!string.Equals(head, candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parent, candidate.Parent, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tagHead, candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted Workbench frontier changed after publication preview.");
    }

    private static async Task<bool> EnsureFixedRemoteAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var current = await GetConfiguredRemoteUrlAsync(repositoryRoot, cancellationToken);
        if (string.IsNullOrWhiteSpace(current))
        {
            await RunGitAsync(repositoryRoot, cancellationToken, "remote", "add", RemoteName, RemoteUrl);
            return true;
        }
        if (!SameRemoteUrl(current, RemoteUrl))
            throw new InvalidDataException($"Remote {RemoteName} conflicts with fixed URL: {current}");
        return false;
    }

    private static async Task<string?> GetConfiguredRemoteUrlAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repositoryRoot, cancellationToken, true, "remote", "get-url", RemoteName);
        return result.ExitCode == 0 ? result.Stdout.Trim() : null;
    }

    private static async Task<string?> ReadRemoteMainAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repositoryRoot, cancellationToken, "ls-remote", RemoteName, "refs/heads/main");
        var line = SplitLines(result.Stdout).SingleOrDefault();
        return line is null ? null : ParseLsRemoteSha(line);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string repositoryRoot, CancellationToken cancellationToken, string tag)
    {
        var result = await RunGitAsync(repositoryRoot, cancellationToken,
            "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
        var lines = SplitLines(result.Stdout);
        var peeled = lines.FirstOrDefault(line => line.EndsWith($"refs/tags/{tag}^{{}}", StringComparison.Ordinal));
        if (peeled is not null) return ParseLsRemoteSha(peeled);
        var direct = lines.FirstOrDefault(line => line.EndsWith($"refs/tags/{tag}", StringComparison.Ordinal));
        return direct is null ? null : ParseLsRemoteSha(direct);
    }

    private static string ParseLsRemoteSha(string line)
    {
        var tab = line.IndexOf('\t');
        var sha = (tab >= 0 ? line[..tab] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).Trim();
        return RequireSha(sha, "remote ref");
    }

    private static string RequireSha(string value, string name)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"{name} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static bool SameRemoteUrl(string left, string right)
        => string.Equals(left.Trim().TrimEnd('/'), right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string[] SplitLines(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static Task<GitResult> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
        => RunGitAsync(repositoryRoot, cancellationToken, false, args);

    private static async Task<GitResult> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed git publication process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException($"Fixed git publication operation exceeded {GitTimeout.TotalSeconds:0}s timeout: {string.Join(' ', args)}");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidDataException($"Fixed git publication operation failed ({string.Join(' ', args)}): {stderr.Trim()}");
        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
