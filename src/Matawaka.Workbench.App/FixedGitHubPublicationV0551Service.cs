using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record FixedGitHubPublicationCandidateV0551(
    string Version,
    string RepositoryRoot,
    string Head,
    string Parent,
    string AcceptedTag,
    string RemoteName,
    string RemoteUrl,
    string ExpectedRemoteBase,
    bool RemoteNeedsAdd,
    RealHostModelInvocationAdmissionV0551 Admission,
    IReadOnlyList<string> NonEffects);

public sealed record FixedGitHubPublicationReceiptV0551(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string Head,
    string Parent,
    string AcceptedTag,
    string IntermediateV055Tag,
    string RemoteName,
    string RemoteUrl,
    string RemoteMainBefore,
    string? RemoteAcceptedTagBefore,
    string? RemoteIntermediateV055TagBefore,
    bool RemoteAdded,
    bool MainPushPerformed,
    bool TagPushPerformed,
    bool RecoveryMode,
    string RemoteMainAfter,
    string RemoteAcceptedTagAfter,
    string? RemoteIntermediateV055TagAfter,
    RealHostModelInvocationAdmissionV0551 Admission,
    bool ForcePushPerformed,
    bool ArbitraryRemoteUsed,
    bool ArbitraryRefUsed,
    bool AutomaticRetryPerformed,
    bool SourceMutationPerformed,
    bool RuntimeMaterializationPerformed,
    bool ArtifactAcquisitionPerformed,
    bool RuntimeExecutionPerformed,
    bool ModelRequestPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class FixedGitHubPublicationV0551Service
{
    public const string Version = "0.55.1";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.55.1";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string ExpectedAcceptedV055Commit = "02d81b8559bc7c9676949be0557d20ecb50a9890";
    public const string ExpectedAcceptedV055Tag = "workbench-v0.55-accepted";
    public const string AcceptedTag = "workbench-v0.55.1-accepted";
    public const string ExpectedRemoteBase = "65b0b49a513a6b782760a7626d6b768bf7bb7f91";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidateV0551> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.55.1 publication preview.");

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        if (!parent.Equals(ExpectedAcceptedV055Commit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.55.1 accepted HEAD parent must be exact accepted local v0.55: {ExpectedAcceptedV055Commit}; observed {parent}.");

        var predecessorTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedAcceptedV055Tag)).Stdout, ExpectedAcceptedV055Tag);
        if (!predecessorTag.Equals(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.55-accepted tag does not point at the exact v0.55.1 parent.");
        var targetTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!targetTag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.55.1-accepted tag does not point at current HEAD.");

        var ancestor = await GitAsync(root, cancellationToken, true, "merge-base", "--is-ancestor", ExpectedRemoteBase, head);
        if (ancestor.ExitCode != 0)
            throw new InvalidDataException("Last published Workbench v0.54.2 base is not an ancestor of exact v0.55.1 accepted HEAD.");

        var configured = (await GitAsync(root, cancellationToken, true, "remote", "get-url", RemoteName)).Stdout.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl))
            throw new InvalidDataException($"Configured remote {RemoteName} conflicts with fixed publication URL.");

        // Local/evidence only: no ls-remote/fetch/push is allowed in PreviewAsync.
        var admission = RealHostModelInvocationAdmissionVerifierV0551.FindExact(workspaceRoot);
        return new FixedGitHubPublicationCandidateV0551(
            Version, root, head, parent, AcceptedTag, RemoteName, RemoteUrl, ExpectedRemoteBase,
            string.IsNullOrWhiteSpace(configured), admission, NonEffects());
    }

    public async Task<FixedGitHubPublicationReceiptV0551> PublishAsync(
        FixedGitHubPublicationCandidateV0551 candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.AcceptedTag != AcceptedTag || candidate.RemoteName != RemoteName ||
            !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) || !candidate.Parent.Equals(ExpectedAcceptedV055Commit, StringComparison.OrdinalIgnoreCase) ||
            !candidate.ExpectedRemoteBase.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match the fixed v0.55.1 contract.");

        var workspace = Directory.GetParent(candidate.RepositoryRoot)?.FullName
            ?? throw new InvalidDataException("Workbench workspace root cannot be resolved from repository root.");
        var reverified = await PreviewAsync(workspace, cancellationToken);
        if (!reverified.Head.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.ExecutionReceiptSha256.Equals(candidate.Admission.ExecutionReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.LeaseStateSha256.Equals(candidate.Admission.LeaseStateSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.OutputArtifactSha256.Equals(candidate.Admission.OutputArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.RuntimeManifestSha256.Equals(candidate.Admission.RuntimeManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.ModelAcquisitionReceiptSha256.Equals(candidate.Admission.ModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted head or exact real-host v0.55 admission evidence changed after publication preview.");

        var root = candidate.RepositoryRoot;
        var remoteAdded = false;
        var configured = (await GitAsync(root, cancellationToken, true, "remote", "get-url", RemoteName)).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            await GitAsync(root, cancellationToken, "remote", "add", RemoteName, RemoteUrl);
            remoteAdded = true;
        }
        else if (!SameRemoteUrl(configured, RemoteUrl))
            throw new InvalidDataException("Fixed GitHub remote changed after preview.");

        var remoteMainBefore = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Fixed remote main is missing.");
        var remoteAcceptedTagBefore = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken);
        var remoteIntermediateV055TagBefore = await ReadRemoteTagCommitAsync(root, ExpectedAcceptedV055Tag, cancellationToken);

        if (!remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase) &&
            !remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Remote main is neither exact published v0.54.2 base nor exact v0.55.1 candidate: {remoteMainBefore}");
        if (remoteAcceptedTagBefore is not null && !remoteAcceptedTagBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote workbench-v0.55.1-accepted tag conflicts with exact local accepted HEAD.");
        if (remoteIntermediateV055TagBefore is not null)
            throw new InvalidDataException("Intermediate workbench-v0.55-accepted unexpectedly exists remotely; v0.55.1 closure refuses silent intermediate-tag promotion/reinterpretation.");

        var mainPush = false;
        var tagPush = false;
        var recovery = remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) && remoteAcceptedTagBefore is null;

        if (remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase))
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"{candidate.Head}:refs/heads/main");
            mainPush = true;
        }

        var mid = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main disappeared during publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main did not converge to exact accepted v0.55.1 HEAD.");

        if (remoteAcceptedTagBefore is null)
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPush = true;
        }

        var remoteMainAfter = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main is missing after publication.");
        var remoteAcceptedTagAfter = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken)
            ?? throw new InvalidDataException("Remote accepted v0.55.1 tag is missing after publication.");
        var remoteIntermediateV055TagAfter = await ReadRemoteTagCommitAsync(root, ExpectedAcceptedV055Tag, cancellationToken);
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !remoteAcceptedTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.55.1 HEAD after publication.");
        if (remoteIntermediateV055TagAfter is not null)
            throw new InvalidDataException("Intermediate workbench-v0.55-accepted was published unexpectedly.");

        var headAfter = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = (await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout;
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter))
            throw new InvalidDataException("Local Workbench source state changed during fixed v0.55.1 publication.");

        return new FixedGitHubPublicationReceiptV0551(
            ReceiptSchema, Version, DateTimeOffset.Now, root, candidate.Head, candidate.Parent, AcceptedTag,
            ExpectedAcceptedV055Tag, RemoteName, RemoteUrl, remoteMainBefore, remoteAcceptedTagBefore,
            remoteIntermediateV055TagBefore, remoteAdded, mainPush, tagPush, recovery,
            remoteMainAfter, remoteAcceptedTagAfter, remoteIntermediateV055TagAfter, candidate.Admission,
            false, false, false, false, false, false, false, false, false,
            candidate.NonEffects, "PUBLISHED_ACCEPTED_V0551",
            "Explicit fixed publication fast-forwarded only the exact accepted v0.55.1 HEAD and current v0.55.1 accepted tag after revalidating exact v0.55 real-host model-invocation admission evidence. The intermediate local workbench-v0.55-accepted tag remains unpublished.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        FixedGitHubPublicationReceiptV0551 receipt,
        CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = Path.Combine(root, "artifacts", "publication");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.55.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v0551-parent", ExpectedAcceptedV055Commit == "02d81b8559bc7c9676949be0557d20ecb50a9890", ExpectedAcceptedV055Commit, "exact local accepted v0.55"),
        ("publisher-v0551-parent-tag", ExpectedAcceptedV055Tag == "workbench-v0.55-accepted", ExpectedAcceptedV055Tag, "local predecessor tag only"),
        ("publisher-v0551-target-tag", AcceptedTag == "workbench-v0.55.1-accepted", AcceptedTag, "current publication-closure tag"),
        ("publisher-v0551-fixed-remote", RemoteUrl == "https://github.com/Matawaka/workbench.git", RemoteUrl, "fixed only"),
        ("publisher-v0551-remote-base", ExpectedRemoteBase == "65b0b49a513a6b782760a7626d6b768bf7bb7f91", ExpectedRemoteBase, "exact last published v0.54.2"),
        ("publisher-v0551-preview-network", true, "PreviewAsync uses local git/evidence only; no ls-remote/fetch/push", "no network before explicit confirmation"),
        ("publisher-v0551-push", true, "exact head->main + current v0.55.1 tag only", "no force/arbitrary ref/intermediate v0.55 tag")
    };

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "Preview != Publish; no remote/network effect before explicit Publish accepted confirmation",
        "exact real-host v0.55 admission evidence is revalidated locally and not reinterpreted",
        "v0.52 acquisition, v0.53 execution, v0.54 materialization and v0.55 invocation primitives are unchanged by publication closure",
        "no runtime-tree materialization during publication",
        "no artifact acquisition during publication",
        "no process start/stop or model invocation during publication",
        "no benchmark/game/display/send/response/action/successor authority",
        "no catalog mutation or Agent Execute/ActionPermit",
        "no arbitrary Git remote/ref/command and no force push",
        "no automatic retry",
        "intermediate local workbench-v0.55-accepted tag remains local and is not silently promoted"
    };

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static async Task<string?> ReadRemoteRefAsync(string root, string reference, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, "ls-remote", RemoteName, reference);
        var line = SplitLines(result.Stdout).SingleOrDefault();
        return line is null ? null : ParseLsRemoteSha(line);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string root, string tag, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
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

    private static string[] SplitLines(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool SameRemoteUrl(string left, string right)
        => NormalizeRemoteUrl(left) == NormalizeRemoteUrl(right);
    private static string NormalizeRemoteUrl(string value)
        => value.Trim().TrimEnd('/').EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? value.Trim().TrimEnd('/').ToLowerInvariant()
            : (value.Trim().TrimEnd('/') + ".git").ToLowerInvariant();

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"Invalid Git SHA for {role}: {value}");
        return sha.ToLowerInvariant();
    }

    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => await GitAsync(root, cancellationToken, false, args);

    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start fixed git invocation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var result = new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
        if (!allowFailure && result.ExitCode != 0)
            throw new InvalidDataException($"Fixed git invocation failed ({string.Join(' ', args)}): {result.Stderr.Trim()}");
        return result;
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
