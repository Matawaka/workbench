using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record FixedGitHubPublicationCandidateV0552(
    string Version,
    string RepositoryRoot,
    string Head,
    string FirstParent,
    string SecondParent,
    string AcceptedTag,
    string RemoteUrl,
    string ExpectedRemoteBase,
    RealHostModelInvocationAdmissionV0551 Admission,
    WorkbenchConvergenceEvidenceV0552 Convergence,
    IReadOnlyList<string> NonEffects);

public sealed record FixedGitHubPublicationReceiptV0552(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string Head,
    string FirstParent,
    string SecondParent,
    string AcceptedTag,
    string RemoteUrl,
    string RemoteMainBefore,
    string? RemoteAcceptedTagBefore,
    string? RemoteIntermediateV055TagBefore,
    string? RemoteFailedV0551TagBefore,
    bool MainPushPerformed,
    bool TagPushPerformed,
    bool RecoveryMode,
    string RemoteMainAfter,
    string RemoteAcceptedTagAfter,
    string? RemoteIntermediateV055TagAfter,
    string? RemoteFailedV0551TagAfter,
    RealHostModelInvocationAdmissionV0551 Admission,
    WorkbenchConvergenceEvidenceV0552 Convergence,
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

public sealed class FixedGitHubPublicationV0552Service
{
    public const string Version = "0.55.2";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.55.2";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string FirstParentCommit = "02d81b8559bc7c9676949be0557d20ecb50a9890";
    public const string SecondParentCommit = "6111fdf82a9e8947a7722e9b603c1e9268a19105";
    public const string ExpectedLocalPredecessorTag = "workbench-v0.55-accepted";
    public const string FailedV0551Tag = "workbench-v0.55.1-accepted";
    public const string AcceptedTag = "workbench-v0.55.2-accepted";
    public const string ExpectedRemoteBase = SecondParentCommit;

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidateV0552> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.55.2 publication preview.");

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parents = ParseParents((await GitAsync(root, cancellationToken, "rev-list", "--parents", "-n", "1", head)).Stdout);
        if (parents.Length != 2 || !parents[0].Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) || !parents[1].Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.55.2 accepted HEAD must have exact ordered parents: local accepted v0.55 first, imported public #82 second.");

        var predecessorTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedLocalPredecessorTag)).Stdout, ExpectedLocalPredecessorTag);
        if (!predecessorTag.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.55-accepted tag does not point at exact first parent.");
        var targetTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!targetTag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.55.2-accepted tag does not point at current HEAD.");

        var publicAncestor = await GitAsync(root, cancellationToken, true, "merge-base", "--is-ancestor", SecondParentCommit, head);
        var localAncestor = await GitAsync(root, cancellationToken, true, "merge-base", "--is-ancestor", FirstParentCommit, head);
        if (publicAncestor.ExitCode != 0 || localAncestor.ExitCode != 0)
            throw new InvalidDataException("Exact local/public convergence parents are not both ancestors of accepted v0.55.2 HEAD.");

        // Local/evidence only. No ls-remote/fetch/push occurs in PreviewAsync.
        var admission = RealHostModelInvocationAdmissionVerifierV0551.FindExact(workspaceRoot);
        var convergence = WorkbenchConvergenceEvidenceVerifierV0552.FindExact(workspaceRoot);
        return new FixedGitHubPublicationCandidateV0552(
            Version, root, head, FirstParentCommit, SecondParentCommit, AcceptedTag,
            RemoteUrl, ExpectedRemoteBase, admission, convergence, NonEffects());
    }

    public async Task<FixedGitHubPublicationReceiptV0552> PublishAsync(FixedGitHubPublicationCandidateV0552 candidate, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.AcceptedTag != AcceptedTag || candidate.RemoteUrl != RemoteUrl ||
            !candidate.FirstParent.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !candidate.SecondParent.Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !candidate.ExpectedRemoteBase.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match the fixed v0.55.2 contract.");

        var workspace = Directory.GetParent(candidate.RepositoryRoot)?.FullName
            ?? throw new InvalidDataException("Workbench workspace root cannot be resolved from repository root.");
        var reverified = await PreviewAsync(workspace, cancellationToken);
        if (!reverified.Head.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.ExecutionReceiptSha256.Equals(candidate.Admission.ExecutionReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Admission.LeaseStateSha256.Equals(candidate.Admission.LeaseStateSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Convergence.RecoveryReceiptSha256.Equals(candidate.Convergence.RecoveryReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !reverified.Convergence.PublicImportReceiptSha256.Equals(candidate.Convergence.PublicImportReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local accepted head or exact admission/convergence evidence changed after publication preview.");

        var root = candidate.RepositoryRoot;
        var remoteMainBefore = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Fixed remote main is missing.");
        var remoteAcceptedTagBefore = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken);
        var remoteIntermediateV055TagBefore = await ReadRemoteTagCommitAsync(root, ExpectedLocalPredecessorTag, cancellationToken);
        var remoteFailedV0551TagBefore = await ReadRemoteTagCommitAsync(root, FailedV0551Tag, cancellationToken);

        if (!remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase) &&
            !remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Remote main drifted from exact public #82 base before v0.55.2 publication: {remoteMainBefore}");
        if (remoteAcceptedTagBefore is not null && !remoteAcceptedTagBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote workbench-v0.55.2-accepted tag conflicts with exact local accepted HEAD.");
        if (remoteIntermediateV055TagBefore is not null || remoteFailedV0551TagBefore is not null)
            throw new InvalidDataException("Intermediate/failed v0.55 accepted tags unexpectedly exist remotely; publication refuses silent tag promotion/reinterpretation.");

        var mainPush = false;
        var tagPush = false;
        var recovery = remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) && remoteAcceptedTagBefore is null;

        if (remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase))
        {
            // Default Git push is fast-forward-only; no force flag is ever supplied.
            await GitAsync(root, cancellationToken, "push", RemoteUrl, $"{candidate.Head}:refs/heads/main");
            mainPush = true;
        }

        var mid = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main disappeared during publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main did not converge to exact accepted v0.55.2 HEAD.");

        if (remoteAcceptedTagBefore is null)
        {
            await GitAsync(root, cancellationToken, "push", RemoteUrl, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPush = true;
        }

        var remoteMainAfter = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main is missing after publication.");
        var remoteAcceptedTagAfter = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken)
            ?? throw new InvalidDataException("Remote accepted v0.55.2 tag is missing after publication.");
        var remoteIntermediateV055TagAfter = await ReadRemoteTagCommitAsync(root, ExpectedLocalPredecessorTag, cancellationToken);
        var remoteFailedV0551TagAfter = await ReadRemoteTagCommitAsync(root, FailedV0551Tag, cancellationToken);
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !remoteAcceptedTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.55.2 HEAD after publication.");
        if (remoteIntermediateV055TagAfter is not null || remoteFailedV0551TagAfter is not null)
            throw new InvalidDataException("Intermediate/failed accepted tag was published unexpectedly.");

        var headAfter = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = (await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout;
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter))
            throw new InvalidDataException("Local Workbench source state changed during fixed v0.55.2 publication.");

        return new FixedGitHubPublicationReceiptV0552(
            ReceiptSchema, Version, DateTimeOffset.Now, root, candidate.Head, candidate.FirstParent, candidate.SecondParent,
            AcceptedTag, RemoteUrl, remoteMainBefore, remoteAcceptedTagBefore, remoteIntermediateV055TagBefore, remoteFailedV0551TagBefore,
            mainPush, tagPush, recovery, remoteMainAfter, remoteAcceptedTagAfter, remoteIntermediateV055TagAfter, remoteFailedV0551TagAfter,
            candidate.Admission, candidate.Convergence,
            false, false, false, false, false, false, false, false, false,
            candidate.NonEffects, "PUBLISHED_ACCEPTED_V0552",
            "Explicit fixed publication fast-forwarded only the exact two-parent accepted v0.55.2 convergence commit and its current accepted tag. Local v0.55 and failed v0.55.1 tags remain unpublished; historical receipts are not reinterpreted.");
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, FixedGitHubPublicationReceiptV0552 receipt, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = Path.Combine(root, "artifacts", "publication");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.55.2-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v0552-first-parent", FirstParentCommit == WorkbenchConvergenceEvidenceVerifierV0552.LocalAcceptedCommit, FirstParentCommit, "exact local accepted v0.55"),
        ("publisher-v0552-second-parent", SecondParentCommit == WorkbenchConvergenceEvidenceVerifierV0552.PublicMainCommit, SecondParentCommit, "exact public #82 current base"),
        ("publisher-v0552-target-tag", AcceptedTag == "workbench-v0.55.2-accepted", AcceptedTag, "fresh accepted convergence tag"),
        ("publisher-v0552-fixed-remote", RemoteUrl == "https://github.com/Matawaka/workbench.git", RemoteUrl, "fixed only"),
        ("publisher-v0552-preview-network", true, "PreviewAsync uses local git/evidence only; no ls-remote/fetch/push", "no network before explicit confirmation"),
        ("publisher-v0552-push", true, "exact head->main + current v0.55.2 tag only", "no force/arbitrary ref/intermediate tags")
    };

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "Preview != Publish; no remote/network effect before explicit Publish accepted confirmation",
        "exact real-host v0.55 admission, recovery and no-ref import evidence is revalidated locally and not reinterpreted",
        "public #82 provenance-bound runtime-execution lease remains distinct from local v0.55 model-invocation lease",
        "no artifact acquisition, runtime materialization, process execution or model invocation during publication",
        "no benchmark/game/display/send/response/action/successor authority",
        "no catalog mutation or Agent Execute/ActionPermit",
        "no arbitrary Git remote/ref/command and no force push",
        "no automatic retry",
        "intermediate local workbench-v0.55-accepted and failed workbench-v0.55.1-accepted tags remain unpublished"
    };

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string[] ParseParents(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidDataException("Accepted commit parent list is missing.");
        return parts.Skip(1).Select(x => RequireSha(x, "parent")).ToArray();
    }

    private static async Task<string?> ReadRemoteRefAsync(string root, string reference, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, "ls-remote", RemoteUrl, reference);
        var line = SplitLines(result.Stdout).SingleOrDefault();
        return line is null ? null : ParseLsRemoteSha(line);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string root, string tag, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, "ls-remote", RemoteUrl, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
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

    private static string[] SplitLines(string text) => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    private static string RequireSha(string text, string label)
    {
        var sha = text.Trim().ToLowerInvariant();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"Invalid Git SHA for {label}: {text.Trim()}");
        return sha;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => await GitAsync(root, cancellationToken, false, args);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Unable to start fixed Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var wait = process.WaitForExitAsync(cancellationToken);
        if (await Task.WhenAny(wait, Task.Delay(GitTimeout, cancellationToken)) != wait)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Fixed v0.55.2 Git operation timed out.");
        }
        await wait;
        var stdout = await stdoutTask; var stderr = await stderrTask;
        if (!allowFailure && process.ExitCode != 0) throw new InvalidDataException("Fixed v0.55.2 Git operation failed: " + stderr.Trim());
        return (process.ExitCode, stdout, stderr);
    }
}
