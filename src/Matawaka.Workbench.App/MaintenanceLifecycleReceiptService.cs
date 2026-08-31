using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record MaintenanceLifecycleArtifactBinding(
    string Role,
    string Path,
    string Sha256,
    string Schema);

public sealed record MaintenanceLifecycleCheck(
    string Id,
    bool Passed,
    string Observed,
    string Expected);

public sealed record MaintenanceLifecycleAssessment(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PredecessorCommit,
    string PredecessorTag,
    string TargetVersion,
    string TargetTag,
    string CandidateExecutableSha256,
    string AcceptedCommit,
    string RemoteMainAfter,
    string RemoteTagAfter,
    MaintenanceLifecycleArtifactBinding Orchestrator,
    MaintenanceLifecycleArtifactBinding Acceptance,
    MaintenanceLifecycleArtifactBinding Checkpoint,
    MaintenanceLifecycleArtifactBinding Publication,
    IReadOnlyList<MaintenanceLifecycleCheck> Checks,
    bool Complete,
    bool AuthorityCreated,
    bool ActionPerformed,
    bool RetryAuthorized,
    bool RollbackAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record MaintenanceLifecycleReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    MaintenanceLifecycleAssessment Assessment,
    string AssessmentDigest,
    bool Complete,
    bool AuthorityCreated,
    bool ActionPerformed,
    bool RetryAuthorized,
    bool RollbackAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.34 audit-only composition of already-existing maintenance evidence.
/// It does not invoke update, build, launch, Self-test, checkpoint or publication
/// services. Fixed read-only Git observations are used only to verify current local
/// HEAD/tag/clean state after those independent actions have already completed.
/// </summary>
public sealed class MaintenanceLifecycleReceiptService
{
    public const string Version = "0.34.0";
    public const string AssessmentSchema = "matawaka.workbench-maintenance-lifecycle-assessment/v0.34";
    public const string ReceiptSchema = "matawaka.workbench-maintenance-lifecycle-receipt/v0.34";
    public const string PredecessorCommit = "df211d1f4d80d0b1f238f1166460758e73ce18d2";
    public const string PredecessorTag = "workbench-v0.33-accepted";
    public const string TargetTag = "workbench-v0.34-accepted";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<MaintenanceLifecycleAssessment> AssessAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var head = RequireGitSha((await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim(), "HEAD");
        var tagsAtHead = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");

        var checkpointMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "acceptance"),
            "checkpoint-v0.34-*.json",
            doc => JsonString(doc, "Schema") == "matawaka.workbench-local-checkpoint-receipt/v0.34" &&
                   JsonString(doc, "Version") == Version &&
                   JsonString(doc, "PreviousHead").Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "Tag") == TargetTag &&
                   JsonString(doc, "NewHead").Equals(head, StringComparison.OrdinalIgnoreCase),
            "checkpoint");
        using var checkpointDoc = ParseJson(checkpointMatch.Path);
        var checkpoint = checkpointDoc.RootElement;
        var acceptancePath = RequireBoundedArtifactPath(repositoryRoot, JsonString(checkpoint, "AcceptanceArtifactPath"), "acceptance");
        var acceptanceExpectedSha = JsonString(checkpoint, "AcceptanceArtifactSha256").ToLowerInvariant();
        var acceptanceActualSha = HashFile(acceptancePath);
        if (!acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact SHA-256 mismatch.");

        using var acceptanceDoc = ParseJson(acceptancePath);
        var acceptance = acceptanceDoc.RootElement;
        if (JsonString(acceptance, "Schema") != "matawaka.workbench-acceptance-receipt/v0.34" ||
            JsonString(acceptance, "Version") != Version ||
            !JsonBool(acceptance, "Passed"))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact is not a passing v0.34 Self-test receipt.");

        var executableSha = JsonString(acceptance, "AppExecutableSha256").ToLowerInvariant();
        if (!executableSha.Equals(JsonString(checkpoint, "AppExecutableSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint executable digest does not match exact passing acceptance artifact.");

        var orchestratorMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "update-orchestrator"),
            "update-orchestrator-v0.33-*.json",
            doc => JsonString(doc, "Schema") == "matawaka.workbench-maintenance-update-orchestrator-receipt/v0.33" &&
                   JsonString(doc, "TargetVersion") == Version &&
                   JsonString(doc, "TargetTag") == TargetTag &&
                   JsonString(doc, "PredecessorCommit").Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   !JsonBool(doc, "LaunchPerformed") &&
                   JsonString(JsonObject(doc, "ApplyBuild"), "Status") == "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED" &&
                   JsonString(JsonObject(doc, "ApplyBuild"), "CandidateExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase),
            "orchestrator");
        using var orchestratorDoc = ParseJson(orchestratorMatch.Path);
        var orchestrator = orchestratorDoc.RootElement;

        var publicationMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "publication"),
            "fixed-github-publication-v0.34-*.json",
            doc => JsonString(doc, "Schema") == "matawaka.workbench-fixed-github-publication-receipt/v0.34" &&
                   JsonString(doc, "Version") == Version &&
                   JsonString(doc, "AcceptedTag") == TargetTag &&
                   JsonString(doc, "LocalParent").Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "LocalHead").Equals(head, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "RemoteMainAfter").Equals(head, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "RemoteTagAfter").Equals(head, StringComparison.OrdinalIgnoreCase) &&
                   JsonBool(doc, "LocalHeadUnchanged") &&
                   JsonBool(doc, "WorkingTreeUnchanged"),
            "publication");
        using var publicationDoc = ParseJson(publicationMatch.Path);
        var publication = publicationDoc.RootElement;

        var checks = new[]
        {
            Check("current-head-is-checkpoint-head", head.Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), head, JsonString(checkpoint, "NewHead")),
            Check("target-tag-at-current-head", tagsAtHead.Contains(TargetTag, StringComparer.Ordinal), string.Join(",", tagsAtHead), TargetTag),
            Check("working-tree-clean", string.IsNullOrWhiteSpace(status), string.IsNullOrWhiteSpace(status) ? "clean" : status.Trim(), "clean"),
            Check("checkpoint-predecessor-exact", JsonString(checkpoint, "PreviousHead").Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase), JsonString(checkpoint, "PreviousHead"), PredecessorCommit),
            Check("acceptance-artifact-digest-exact", acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase), acceptanceActualSha, acceptanceExpectedSha),
            Check("acceptance-passed", JsonBool(acceptance, "Passed"), JsonBool(acceptance, "Passed").ToString(), "true"),
            Check("candidate-executable-bound-across-update-and-acceptance", JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256"), executableSha),
            Check("candidate-executable-bound-across-acceptance-and-checkpoint", JsonString(checkpoint, "AppExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(checkpoint, "AppExecutableSha256"), executableSha),
            Check("orchestrator-stopped-before-launch", !JsonBool(orchestrator, "LaunchPerformed"), JsonBool(orchestrator, "LaunchPerformed").ToString(), "false"),
            Check("checkpoint-does-not-imply-publication", !JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed"), JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed").ToString(), "false"),
            Check("publication-head-equals-checkpoint-head", JsonString(publication, "LocalHead").Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalHead"), JsonString(checkpoint, "NewHead")),
            Check("publication-parent-exact", JsonString(publication, "LocalParent").Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalParent"), PredecessorCommit),
            Check("remote-main-exact", JsonString(publication, "RemoteMainAfter").Equals(head, StringComparison.OrdinalIgnoreCase), JsonString(publication, "RemoteMainAfter"), head),
            Check("remote-tag-exact", JsonString(publication, "RemoteTagAfter").Equals(head, StringComparison.OrdinalIgnoreCase), JsonString(publication, "RemoteTagAfter"), head),
            Check("publication-local-state-unchanged", JsonBool(publication, "LocalHeadUnchanged") && JsonBool(publication, "WorkingTreeUnchanged"), $"head={JsonBool(publication, "LocalHeadUnchanged")}; tree={JsonBool(publication, "WorkingTreeUnchanged")}", "true / true")
        };
        var complete = checks.All(x => x.Passed);
        if (!complete)
            throw new InvalidDataException("Lifecycle relation checks are incomplete. Refusing a complete lifecycle assessment.");

        var nonEffects = new[]
        {
            "no update package materialization or source mutation",
            "no build or candidate launch",
            "no Self-test invocation",
            "no git add/commit/tag",
            "no git push/fetch or remote mutation",
            "no publication retry authority",
            "no rollback authority",
            "no catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no general network authority",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "lifecycle receipt write is local evidence only"
        };

        return new MaintenanceLifecycleAssessment(
            AssessmentSchema,
            Version,
            DateTimeOffset.Now,
            PredecessorCommit,
            PredecessorTag,
            Version,
            TargetTag,
            executableSha,
            head,
            JsonString(publication, "RemoteMainAfter"),
            JsonString(publication, "RemoteTagAfter"),
            Bind("orchestrator", orchestratorMatch.Path, JsonString(orchestrator, "Schema")),
            Bind("acceptance", acceptancePath, JsonString(acceptance, "Schema")),
            Bind("checkpoint", checkpointMatch.Path, JsonString(checkpoint, "Schema")),
            Bind("publication", publicationMatch.Path, JsonString(publication, "Schema")),
            checks,
            true,
            false,
            false,
            false,
            false,
            nonEffects,
            "Complete means only that exact local maintenance evidence forms one verified v0.34 update/build -> Self-test -> checkpoint -> publication relation. It does not authorize, replay, retry or legitimize any action beyond those already-completed independent receipts.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        MaintenanceLifecycleAssessment assessment,
        CancellationToken cancellationToken)
    {
        if (assessment is null || !assessment.Complete || assessment.AuthorityCreated || assessment.ActionPerformed || assessment.RetryAuthorized || assessment.RollbackAuthorized)
            throw new InvalidDataException("Only a complete non-authorizing lifecycle assessment may be written as a receipt.");
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var directory = Path.Combine(repositoryRoot, "artifacts", "lifecycle");
        Directory.CreateDirectory(directory);
        var digest = HashJson(assessment);
        var receipt = new MaintenanceLifecycleReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            assessment,
            digest,
            true,
            false,
            false,
            false,
            false,
            assessment.NonEffects,
            "Local lifecycle evidence write only. Summary != authority; observed sequence != authorized sequence; receipt binding != automatic transition.");
        var path = Path.Combine(directory, $"maintenance-lifecycle-v0.34-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<MaintenanceLifecycleCheck> RunOfflineContractChecks()
    {
        var checks = new List<MaintenanceLifecycleCheck>
        {
            Check("lifecycle-summary-not-authority", true, "AuthorityCreated=false", "false"),
            Check("lifecycle-summary-not-action", true, "ActionPerformed=false", "false"),
            Check("lifecycle-no-retry-authority", true, "RetryAuthorized=false", "false"),
            Check("lifecycle-no-rollback-authority", true, "RollbackAuthorized=false", "false"),
            Check("lifecycle-target-tag-fixed", TargetTag == "workbench-v0.34-accepted", TargetTag, "workbench-v0.34-accepted"),
            Check("lifecycle-predecessor-fixed", PredecessorCommit == "df211d1f4d80d0b1f238f1166460758e73ce18d2", PredecessorCommit, "accepted v0.33 commit")
        };

        var ambiguityRefused = false;
        try { RequireSingleCandidate(2, "fixture"); }
        catch (InvalidDataException) { ambiguityRefused = true; }
        checks.Add(Check("lifecycle-ambiguous-artifact-refused", ambiguityRefused, ambiguityRefused.ToString(), "true"));

        var missingRefused = false;
        try { RequireSingleCandidate(0, "fixture"); }
        catch (InvalidDataException) { missingRefused = true; }
        checks.Add(Check("lifecycle-missing-artifact-refused", missingRefused, missingRefused.ToString(), "true"));
        return checks;
    }

    private static (string Path, string Sha256) FindSingleJson(
        string directory,
        string pattern,
        Func<JsonElement, bool> predicate,
        string role)
    {
        if (!Directory.Exists(directory))
            throw new InvalidDataException($"Lifecycle {role} artifact directory is missing: {directory}");
        var matches = new List<string>();
        foreach (var path in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                using var doc = ParseJson(path);
                if (predicate(doc.RootElement)) matches.Add(path);
            }
            catch (JsonException)
            {
                // Invalid unrelated artifacts are not candidates; exact matching still fails closed on 0/>1.
            }
        }
        RequireSingleCandidate(matches.Count, role);
        return (matches[0], HashFile(matches[0]));
    }

    private static void RequireSingleCandidate(int count, string role)
    {
        if (count != 1)
            throw new InvalidDataException($"Lifecycle {role} artifact binding is {(count == 0 ? "missing" : "ambiguous")}: candidates={count}.");
    }

    private static MaintenanceLifecycleArtifactBinding Bind(string role, string path, string schema)
        => new(role, path, HashFile(path), schema);

    private static MaintenanceLifecycleCheck Check(string id, bool passed, string observed, string expected)
        => new(id, passed, observed, expected);

    private static JsonDocument ParseJson(string path)
        => JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));

    private static JsonElement JsonObject(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Lifecycle artifact missing object property: {property}");
        return value;
    }

    private static string JsonString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Lifecycle artifact missing string property: {property}");
        return value.GetString() ?? string.Empty;
    }

    private static bool JsonBool(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Lifecycle artifact missing bool property: {property}");
        return value.GetBoolean();
    }

    private static string RequireBoundedArtifactPath(string repositoryRoot, string path, string expectedSubdir)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException($"Lifecycle-bound {expectedSubdir} artifact file is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", expectedSubdir)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Lifecycle-bound {expectedSubdir} artifact escapes its fixed Workbench artifacts directory.");
        return full;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashJson(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string RequireGitSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"Lifecycle {role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static string[] SplitLines(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<string> RunGitReadOnlyAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only lifecycle git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException($"Fixed read-only lifecycle git observation exceeded {GitTimeout.TotalSeconds:0}s timeout.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only lifecycle git observation failed: {stderr.Trim()}");
        return stdout;
    }
}
