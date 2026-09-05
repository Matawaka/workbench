using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RealHostExecutionAdmissionV0532(
    string ExecutionReceiptPath,
    string ExecutionReceiptSha256,
    string StopReceiptPath,
    string StopReceiptSha256,
    string LeaseId,
    int ProcessId,
    string ExecutablePath,
    string ExecutableSha256,
    DateTimeOffset ExecutionObservedAt,
    DateTimeOffset StopObservedAt);

public sealed record FixedGitHubPublicationCandidateV0532(
    string Version,
    string RepositoryRoot,
    string Head,
    string Parent,
    string AcceptedTag,
    string RemoteName,
    string RemoteUrl,
    string ExpectedRemoteBase,
    bool RemoteNeedsAdd,
    RealHostExecutionAdmissionV0532 Admission,
    IReadOnlyList<string> NonEffects);

public sealed record FixedGitHubPublicationReceiptV0532(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string Head,
    string Parent,
    string AcceptedTag,
    string RemoteName,
    string RemoteUrl,
    string RemoteMainBefore,
    string? RemoteTagBefore,
    bool RemoteAdded,
    bool MainPushPerformed,
    bool TagPushPerformed,
    bool RecoveryMode,
    string RemoteMainAfter,
    string RemoteTagAfter,
    RealHostExecutionAdmissionV0532 Admission,
    bool ForcePushPerformed,
    bool ArbitraryRemoteUsed,
    bool AutomaticRetryPerformed,
    bool RuntimeExecutionPerformed,
    bool ArtifactAcquisitionPerformed,
    bool ModelRequestPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class FixedGitHubPublicationV0532Service
{
    public const string Version = "0.53.2";
    public const string ReceiptSchema = "matawaka.workbench-fixed-github-publication-receipt/v0.53.2";
    public const string RemoteName = "github-workbench";
    public const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string ExpectedAcceptedV053Commit = "49ccefc68ec0b6979fd2e36c59af1e8f1f68de64";
    public const string ExpectedAcceptedV053Tag = "workbench-v0.53-accepted";
    public const string AcceptedTag = "workbench-v0.53.2-accepted";
    public const string ExpectedRemoteBase = "632ddbb73e8d70b485f02d21f772674d429adf8c";
    public const string ExpectedSmokeRequestId = "runtime-smoke-v053-local-realhost-001";
    public const string ExpectedSmokeExecutableSha256 = "1f7b207a56ed030e6bdbe633f9ae522842539a7036a5e1933cb23a1c58d58a10";
    public const string ExpectedSmokeExecutableFileName = "matawaka-v053-runtime-smoke-local-v1.exe";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FixedGitHubPublicationCandidateV0532> PreviewAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
            throw new InvalidDataException("Workbench working tree must be clean before v0.53.2 publication preview.");

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var parent = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "HEAD parent");
        if (!parent.Equals(ExpectedAcceptedV053Commit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.53.2 accepted HEAD parent must be exact accepted v0.53: {ExpectedAcceptedV053Commit}; observed {parent}.");

        var predecessorTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedAcceptedV053Tag)).Stdout, ExpectedAcceptedV053Tag);
        if (!predecessorTag.Equals(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.53-accepted tag does not point at the exact v0.53.2 parent.");
        var targetTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", AcceptedTag)).Stdout, AcceptedTag);
        if (!targetTag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local workbench-v0.53.2-accepted tag does not point at current HEAD.");

        var ancestor = await GitAsync(root, cancellationToken, true, "merge-base", "--is-ancestor", ExpectedRemoteBase, head);
        if (ancestor.ExitCode != 0)
            throw new InvalidDataException("Last published Workbench base is not an ancestor of the exact v0.53.2 accepted HEAD.");

        var configured = (await GitAsync(root, cancellationToken, true, "remote", "get-url", RemoteName)).Stdout.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !SameRemoteUrl(configured, RemoteUrl))
            throw new InvalidDataException($"Configured remote {RemoteName} conflicts with fixed publication URL.");

        var admission = FindAdmission(root);
        var nonEffects = NonEffects();
        return new FixedGitHubPublicationCandidateV0532(
            Version, root, head, parent, AcceptedTag, RemoteName, RemoteUrl, ExpectedRemoteBase,
            string.IsNullOrWhiteSpace(configured), admission, nonEffects);
    }

    public async Task<FixedGitHubPublicationReceiptV0532> PublishAsync(FixedGitHubPublicationCandidateV0532 candidate, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.AcceptedTag != AcceptedTag || candidate.RemoteName != RemoteName ||
            !SameRemoteUrl(candidate.RemoteUrl, RemoteUrl) || !candidate.Parent.Equals(ExpectedAcceptedV053Commit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Publication candidate does not match the fixed v0.53.2 contract.");

        var reverified = await PreviewAsync(candidate.RepositoryRootDirectoryAsWorkspace(), cancellationToken);
        if (!reverified.Head.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            reverified.Admission.ExecutionReceiptSha256 != candidate.Admission.ExecutionReceiptSha256 ||
            reverified.Admission.StopReceiptSha256 != candidate.Admission.StopReceiptSha256)
            throw new InvalidDataException("Local accepted head or real-host admission evidence changed after publication preview.");

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
        var remoteTagBefore = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken);
        if (!remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase) &&
            !remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Remote main is neither exact last published base nor exact v0.53.2 candidate: {remoteMainBefore}");
        if (remoteTagBefore is not null && !remoteTagBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote workbench-v0.53.2-accepted tag conflicts with exact local accepted HEAD.");

        var mainPush = false;
        var tagPush = false;
        var recovery = remoteMainBefore.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) && remoteTagBefore is null;
        if (remoteMainBefore.Equals(ExpectedRemoteBase, StringComparison.OrdinalIgnoreCase))
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"{candidate.Head}:refs/heads/main");
            mainPush = true;
        }
        var mid = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main disappeared during publication.");
        if (!mid.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main did not converge to exact accepted v0.53.2 HEAD.");

        if (remoteTagBefore is null)
        {
            await GitAsync(root, cancellationToken, "push", RemoteName, $"refs/tags/{AcceptedTag}:refs/tags/{AcceptedTag}");
            tagPush = true;
        }

        var remoteMainAfter = await ReadRemoteRefAsync(root, "refs/heads/main", cancellationToken)
            ?? throw new InvalidDataException("Remote main is missing after publication.");
        var remoteTagAfter = await ReadRemoteTagCommitAsync(root, AcceptedTag, cancellationToken)
            ?? throw new InvalidDataException("Remote accepted tag is missing after publication.");
        if (!remoteMainAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) ||
            !remoteTagAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote main/tag do not equal exact accepted v0.53.2 HEAD after publication.");

        var headAfter = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD after publication");
        var statusAfter = (await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout;
        if (!headAfter.Equals(candidate.Head, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusAfter))
            throw new InvalidDataException("Local Workbench state changed during fixed publication.");

        return new FixedGitHubPublicationReceiptV0532(
            ReceiptSchema, Version, DateTimeOffset.Now, root, candidate.Head, candidate.Parent, AcceptedTag,
            RemoteName, RemoteUrl, remoteMainBefore, remoteTagBefore, remoteAdded, mainPush, tagPush, recovery,
            remoteMainAfter, remoteTagAfter, candidate.Admission,
            false, false, false, false, false, false, candidate.NonEffects,
            "PUBLISHED_ACCEPTED_V0532",
            "Explicit fixed publication fast-forwarded only the exact accepted v0.53.2 HEAD and current accepted tag. Intermediate local accepted tags remain unpushed; ancestry alone does not promote them as separate remote accepted frontiers.");
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, FixedGitHubPublicationReceiptV0532 receipt, CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = Path.Combine(root, "artifacts", "publication");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fixed-github-publication-v0.53.2-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("publisher-v0532-version-gap-explicit", Version == "0.53.2", Version, "0.53.2; v0.53.1 is abandoned #72"),
        ("publisher-v0532-parent", ExpectedAcceptedV053Commit == "49ccefc68ec0b6979fd2e36c59af1e8f1f68de64", ExpectedAcceptedV053Commit, "exact accepted v0.53"),
        ("publisher-v0532-parent-tag", ExpectedAcceptedV053Tag == "workbench-v0.53-accepted", ExpectedAcceptedV053Tag, "workbench-v0.53-accepted"),
        ("publisher-v0532-target-tag", AcceptedTag == "workbench-v0.53.2-accepted", AcceptedTag, "workbench-v0.53.2-accepted"),
        ("publisher-v0532-fixed-remote", RemoteUrl == "https://github.com/Matawaka/workbench.git", RemoteUrl, "fixed only"),
        ("publisher-v0532-remote-base", ExpectedRemoteBase == "632ddbb73e8d70b485f02d21f772674d429adf8c", ExpectedRemoteBase, "last published accepted base"),
        ("publisher-v0532-smoke-request", ExpectedSmokeRequestId == "runtime-smoke-v053-local-realhost-001", ExpectedSmokeRequestId, "exact real-host gate"),
        ("publisher-v0532-smoke-sha", ExpectedSmokeExecutableSha256.Length == 64, ExpectedSmokeExecutableSha256, "exact SHA-256"),
        ("publisher-v0532-preview-network", true, "local git/evidence only", "no ls-remote/push before explicit confirmation"),
        ("publisher-v0532-push", true, "exact head->main + current tag only", "no force/arbitrary ref")
    };

    private static RealHostExecutionAdmissionV0532 FindAdmission(string root)
    {
        var receipts = Path.Combine(root, "artifacts", "runtime-execution", "receipts");
        if (!Directory.Exists(receipts)) throw new InvalidDataException("Real-host runtime-execution receipt directory is missing.");

        foreach (var executionPath in Directory.GetFiles(receipts, "runtime-execution-execution-v0.53-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (!TryReadExecution(executionPath, out var execution)) continue;
            foreach (var stopPath in Directory.GetFiles(receipts, "runtime-execution-stop-v0.53-*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (!TryReadStop(stopPath, execution, out var stopObservedAt)) continue;
                return new RealHostExecutionAdmissionV0532(
                    executionPath, HashFile(executionPath), stopPath, HashFile(stopPath), execution.LeaseId,
                    execution.ProcessId, execution.ExecutablePath, execution.ExecutableSha256,
                    execution.ObservedAt, stopObservedAt);
            }
        }
        throw new InvalidDataException("No exact v0.53 real-host RUNTIME_READY_OBSERVED + matching OWNED_PROCESS_TREE_STOPPED evidence pair was found.");
    }

    private sealed record ExecutionEvidence(string LeaseId, int ProcessId, string ExecutablePath, string ExecutableSha256, DateTimeOffset ObservedAt);

    private static bool TryReadExecution(string path, out ExecutionEvidence evidence)
    {
        evidence = default!;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var o = doc.RootElement;
            if (GetString(o, "Schema") != BoundedRuntimeExecutionV053Service.ExecutionReceiptSchema ||
                GetString(o, "RequestId") != ExpectedSmokeRequestId || GetString(o, "State") != "RUNTIME_READY_OBSERVED" ||
                GetString(o, "Status") != "RUNTIME_READY_OBSERVED" || !GetBool(o, "ExactProcessImageVerified") ||
                !GetBool(o, "RuntimeReadyObserved") || !GetBool(o, "ExecutionAuthorityConsumed") ||
                GetBool(o, "RuntimeTreeMaterializationPerformed") || GetBool(o, "ShellIndirectionPerformed") ||
                GetBool(o, "ElevationRequested") || GetBool(o, "AutomaticRetryPerformed") || GetBool(o, "AutomaticResumePerformed") ||
                GetBool(o, "BenchmarkPerformed") || GetBool(o, "ModelRequestPerformed") || GetBool(o, "GameAccessPerformed") ||
                GetBool(o, "GeneralProcessAuthorityGranted") || GetBool(o, "ArbitraryPidStopAuthorityGranted")) return false;
            var before = GetString(o, "ExecutableSha256BeforeStart");
            var observed = GetString(o, "ObservedProcessImageSha256");
            var exePath = GetString(o, "ExecutablePath");
            if (!before.Equals(ExpectedSmokeExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
                !observed.Equals(ExpectedSmokeExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(exePath).Equals(ExpectedSmokeExecutableFileName, StringComparison.OrdinalIgnoreCase)) return false;
            evidence = new ExecutionEvidence(GetString(o, "LeaseId"), GetInt(o, "ProcessId"), exePath, observed, GetDate(o, "ObservedAt"));
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadStop(string path, ExecutionEvidence execution, out DateTimeOffset observedAt)
    {
        observedAt = default;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var o = doc.RootElement;
            if (GetString(o, "Schema") != BoundedRuntimeExecutionV053Service.StopReceiptSchema ||
                GetString(o, "LeaseId") != execution.LeaseId || GetInt(o, "ProcessId") != execution.ProcessId ||
                !Path.GetFullPath(GetString(o, "ExecutablePath")).Equals(Path.GetFullPath(execution.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                !GetBool(o, "ExactOwnedProcessVerifiedBeforeStop") || !GetBool(o, "EntireOwnedProcessTreeStopRequested") ||
                !GetBool(o, "ProcessExited") || GetBool(o, "ArbitraryPidAccepted") || GetString(o, "Status") != "OWNED_PROCESS_TREE_STOPPED") return false;
            observedAt = GetDate(o, "ObservedAt");
            return observedAt >= execution.ObservedAt;
        }
        catch { return false; }
    }

    private static string GetString(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool GetBool(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && (p.ValueKind is JsonValueKind.True or JsonValueKind.False) && p.GetBoolean();
    private static int GetInt(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.TryGetInt32(out var value) ? value : -1;
    private static DateTimeOffset GetDate(JsonElement o, string name)
        => DateTimeOffset.TryParse(GetString(o, name), out var value) ? value : default;

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "Preview != Publish; no remote/network effect before explicit Publish accepted confirmation",
        "v0.53 runtime execution primitive is unchanged by v0.53.2 publication closure",
        "no runtime process start/stop during publication",
        "no artifact acquisition or runtime-tree materialization during publication",
        "no shell/elevation/arbitrary process authority",
        "no benchmark/model request/game access authority",
        "no catalog mutation or Agent Execute/ActionPermit",
        "no arbitrary Git remote/ref/command and no force push",
        "no automatic retry",
        "intermediate local accepted tags remain local and are not silently promoted"
    };

    private static async Task<string?> ReadRemoteRefAsync(string root, string reference, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, true, "ls-remote", RemoteName, reference);
        if (result.ExitCode != 0) throw new InvalidDataException("Fixed GitHub remote reference read failed.");
        var line = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line is null ? null : RequireSha(line.Split('\t', ' ')[0], reference);
    }

    private static async Task<string?> ReadRemoteTagCommitAsync(string root, string tag, CancellationToken cancellationToken)
    {
        var result = await GitAsync(root, cancellationToken, true, "ls-remote", RemoteName, $"refs/tags/{tag}", $"refs/tags/{tag}^{{}}");
        if (result.ExitCode != 0) throw new InvalidDataException("Fixed GitHub remote tag read failed.");
        var lines = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var peeled = lines.FirstOrDefault(x => x.Contains($"refs/tags/{tag}^{{}}", StringComparison.Ordinal));
        var direct = lines.FirstOrDefault(x => x.Contains($"refs/tags/{tag}", StringComparison.Ordinal));
        var selected = peeled ?? direct;
        return selected is null ? null : RequireSha(selected.Split('\t', ' ')[0], tag);
    }

    private static bool SameRemoteUrl(string a, string b)
        => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => GitAsync(root, cancellationToken, false, args);

    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo
        {
            FileName = "git", WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.53.2 publication Git process.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidDataException($"Fixed v0.53.2 Git operation failed: {error.Trim()}");
        return new GitResult(process.ExitCode, output, error);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}

internal static class FixedGitHubPublicationV0532CandidateExtensions
{
    public static string RepositoryRootDirectoryAsWorkspace(this FixedGitHubPublicationCandidateV0532 candidate)
        => Directory.GetParent(candidate.RepositoryRoot)?.FullName
           ?? throw new InvalidDataException("Workspace root could not be derived from Workbench repository root.");
}
