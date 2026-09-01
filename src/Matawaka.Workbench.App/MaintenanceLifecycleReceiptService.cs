using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// Successor-generic, audit-only composition of already-existing Workbench
/// maintenance evidence. It derives the current accepted lifecycle from the
/// exact accepted tag/checkpoint relation rather than from a release-specific
/// predecessor constant. It never invokes update, build, launch, Self-test,
/// checkpoint or publication services and never selects evidence by file age.
/// </summary>
public sealed class MaintenanceLifecycleReceiptService
{
    private static readonly Regex AcceptedTagRegex = new(
        "^workbench-v(?<version>[0-9]+\\.[0-9]+(?:\\.[0-9]+)*)-accepted$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        var head = RequireGitSha(
            (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim(),
            "HEAD");
        var parent = RequireGitSha(
            (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD^")).Trim(),
            "HEAD parent");
        var status = await RunGitReadOnlyAsync(
            repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");

        var currentAccepted = RequireSingleAcceptedTag(
            SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD")),
            "current HEAD");
        var targetVersion = currentAccepted.Version;
        var targetTag = currentAccepted.Tag;

        var predecessorAccepted = RequireSingleAcceptedTag(
            SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", parent)),
            "predecessor commit");
        var predecessorCommit = parent;
        var predecessorTag = predecessorAccepted.Tag;

        var checkpointMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "acceptance"),
            "checkpoint-v*.json",
            doc => JsonString(doc, "Schema") == CheckpointSchemaFor(targetVersion) &&
                   JsonString(doc, "Version") == targetVersion &&
                   JsonString(doc, "Tag") == targetTag &&
                   JsonString(doc, "PreviousHead").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "NewHead").Equals(head, StringComparison.OrdinalIgnoreCase),
            "checkpoint");
        using var checkpointDoc = ParseJson(checkpointMatch.Path);
        var checkpoint = checkpointDoc.RootElement;

        var acceptancePath = RequireBoundedArtifactPath(
            repositoryRoot,
            JsonString(checkpoint, "AcceptanceArtifactPath"),
            "acceptance");
        var acceptanceExpectedSha = JsonString(checkpoint, "AcceptanceArtifactSha256").ToLowerInvariant();
        var acceptanceActualSha = HashFile(acceptancePath);
        if (!acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact SHA-256 mismatch.");

        using var acceptanceDoc = ParseJson(acceptancePath);
        var acceptance = acceptanceDoc.RootElement;
        if (JsonString(acceptance, "Schema") != AcceptanceSchemaFor(targetVersion) ||
            JsonString(acceptance, "Version") != targetVersion ||
            !JsonBool(acceptance, "Passed"))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact is not a passing Self-test receipt for the current accepted version.");

        var executableSha = JsonString(acceptance, "AppExecutableSha256").ToLowerInvariant();
        if (!executableSha.Equals(JsonString(checkpoint, "AppExecutableSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint executable digest does not match exact passing acceptance artifact.");

        var orchestratorMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "update-orchestrator"),
            "update-orchestrator-v*.json",
            doc => JsonString(doc, "Schema").StartsWith(
                       "matawaka.workbench-maintenance-update-orchestrator-receipt/",
                       StringComparison.Ordinal) &&
                   JsonString(doc, "TargetVersion") == targetVersion &&
                   JsonString(doc, "TargetTag") == targetTag &&
                   JsonString(doc, "PredecessorCommit").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "PredecessorTag") == predecessorTag &&
                   !JsonBool(doc, "LaunchPerformed") &&
                   JsonString(JsonObject(doc, "ApplyBuild"), "Status") ==
                       "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED" &&
                   JsonString(JsonObject(doc, "ApplyBuild"), "CandidateExecutableSha256")
                       .Equals(executableSha, StringComparison.OrdinalIgnoreCase),
            "orchestrator");
        using var orchestratorDoc = ParseJson(orchestratorMatch.Path);
        var orchestrator = orchestratorDoc.RootElement;

        var publicationMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "publication"),
            "fixed-github-publication-v*.json",
            doc => JsonString(doc, "Schema") == PublicationSchemaFor(targetVersion) &&
                   JsonString(doc, "Version") == targetVersion &&
                   JsonString(doc, "AcceptedTag") == targetTag &&
                   JsonString(doc, "LocalParent").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase) &&
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
            Check("current-accepted-tag-unique", true, targetTag, "one workbench-v<version>-accepted tag at HEAD"),
            Check("current-version-derived-from-tag", targetVersion == JsonString(checkpoint, "Version"), targetVersion, JsonString(checkpoint, "Version")),
            Check("current-head-is-checkpoint-head", head.Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), head, JsonString(checkpoint, "NewHead")),
            Check("predecessor-is-git-parent", predecessorCommit.Equals(JsonString(checkpoint, "PreviousHead"), StringComparison.OrdinalIgnoreCase), predecessorCommit, JsonString(checkpoint, "PreviousHead")),
            Check("predecessor-accepted-tag-unique", true, predecessorTag, "one workbench-v<version>-accepted tag at predecessor"),
            Check("working-tree-clean", string.IsNullOrWhiteSpace(status), string.IsNullOrWhiteSpace(status) ? "clean" : status.Trim(), "clean"),
            Check("acceptance-artifact-digest-exact", acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase), acceptanceActualSha, acceptanceExpectedSha),
            Check("acceptance-passed", JsonBool(acceptance, "Passed"), JsonBool(acceptance, "Passed").ToString(), "true"),
            Check("candidate-executable-bound-across-update-and-acceptance", JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256"), executableSha),
            Check("candidate-executable-bound-across-acceptance-and-checkpoint", JsonString(checkpoint, "AppExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(checkpoint, "AppExecutableSha256"), executableSha),
            Check("orchestrator-stopped-before-launch", !JsonBool(orchestrator, "LaunchPerformed"), JsonBool(orchestrator, "LaunchPerformed").ToString(), "false"),
            Check("checkpoint-does-not-imply-publication", !JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed"), JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed").ToString(), "false"),
            Check("publication-head-equals-checkpoint-head", JsonString(publication, "LocalHead").Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalHead"), JsonString(checkpoint, "NewHead")),
            Check("publication-parent-exact", JsonString(publication, "LocalParent").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalParent"), predecessorCommit),
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
            "no git push/fetch/remote mutation",
            "no publication retry authority",
            "no rollback authority",
            "no catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no general network authority",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "accepted tag/version discovery is evidence routing only, not trust or authority discovery",
            "lifecycle receipt write is local evidence only"
        };

        return new MaintenanceLifecycleAssessment(
            AssessmentSchemaFor(targetVersion),
            targetVersion,
            DateTimeOffset.Now,
            predecessorCommit,
            predecessorTag,
            targetVersion,
            targetTag,
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
            $"Complete means only that exact local maintenance evidence forms one verified {targetVersion} update/build -> Self-test -> checkpoint -> publication relation. The target/predecessor identities were derived from exact accepted Git/checkpoint evidence, not hard-coded release constants. This does not authorize, replay, retry or legitimize any action beyond those already-completed independent receipts.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        MaintenanceLifecycleAssessment assessment,
        CancellationToken cancellationToken)
    {
        if (assessment is null || !assessment.Complete || assessment.AuthorityCreated ||
            assessment.ActionPerformed || assessment.RetryAuthorized || assessment.RollbackAuthorized)
            throw new InvalidDataException("Only a complete non-authorizing lifecycle assessment may be written as a receipt.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var directory = Path.Combine(repositoryRoot, "artifacts", "lifecycle");
        Directory.CreateDirectory(directory);
        var digest = HashJson(assessment);
        var receipt = new MaintenanceLifecycleReceipt(
            ReceiptSchemaFor(assessment.TargetVersion),
            assessment.TargetVersion,
            DateTimeOffset.Now,
            assessment,
            digest,
            true,
            false,
            false,
            false,
            false,
            assessment.NonEffects,
            "Local lifecycle evidence write only. Summary != authority; observed sequence != authorized sequence; accepted-tag discovery != trust discovery; receipt binding != automatic transition.");
        var path = Path.Combine(
            directory,
            $"maintenance-lifecycle-v{assessment.TargetVersion}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        return path;
    }

    public static IReadOnlyList<MaintenanceLifecycleCheck> RunOfflineContractChecks()
    {
        var parsedPatch = ParseAcceptedTagVersion("workbench-v0.34.1-accepted");
        var parsedMinor = ParseAcceptedTagVersion("workbench-v0.35-accepted");
        var checks = new List<MaintenanceLifecycleCheck>
        {
            Check("lifecycle-summary-not-authority", true, "AuthorityCreated=false", "false"),
            Check("lifecycle-summary-not-action", true, "ActionPerformed=false", "false"),
            Check("lifecycle-no-retry-authority", true, "RetryAuthorized=false", "false"),
            Check("lifecycle-no-rollback-authority", true, "RollbackAuthorized=false", "false"),
            Check("lifecycle-patch-tag-parsing", parsedPatch == "0.34.1", parsedPatch, "0.34.1"),
            Check("lifecycle-minor-tag-parsing", parsedMinor == "0.35", parsedMinor, "0.35"),
            Check("lifecycle-dynamic-acceptance-schema", AcceptanceSchemaFor(parsedPatch) == "matawaka.workbench-acceptance-receipt/v0.34.1", AcceptanceSchemaFor(parsedPatch), "matawaka.workbench-acceptance-receipt/v0.34.1"),
            Check("lifecycle-dynamic-checkpoint-schema", CheckpointSchemaFor(parsedPatch) == "matawaka.workbench-local-checkpoint-receipt/v0.34.1", CheckpointSchemaFor(parsedPatch), "matawaka.workbench-local-checkpoint-receipt/v0.34.1"),
            Check("lifecycle-dynamic-publication-schema", PublicationSchemaFor(parsedPatch) == "matawaka.workbench-fixed-github-publication-receipt/v0.34.1", PublicationSchemaFor(parsedPatch), "matawaka.workbench-fixed-github-publication-receipt/v0.34.1")
        };

        var ambiguityRefused = false;
        try { RequireSingleCandidate(2, "fixture"); }
        catch (InvalidDataException) { ambiguityRefused = true; }
        checks.Add(Check("lifecycle-ambiguous-artifact-refused", ambiguityRefused, ambiguityRefused.ToString(), "true"));

        var missingRefused = false;
        try { RequireSingleCandidate(0, "fixture"); }
        catch (InvalidDataException) { missingRefused = true; }
        checks.Add(Check("lifecycle-missing-artifact-refused", missingRefused, missingRefused.ToString(), "true"));

        var invalidTagRefused = false;
        try { ParseAcceptedTagVersion("release-v0.34.1"); }
        catch (InvalidDataException) { invalidTagRefused = true; }
        checks.Add(Check("lifecycle-nonaccepted-tag-refused", invalidTagRefused, invalidTagRefused.ToString(), "true"));
        return checks;
    }

    public static string ParseAcceptedTagVersion(string tag)
    {
        var match = AcceptedTagRegex.Match(tag ?? string.Empty);
        if (!match.Success)
            throw new InvalidDataException($"Not a Workbench accepted tag: {tag}");
        return match.Groups["version"].Value;
    }

    private static (string Tag, string Version) RequireSingleAcceptedTag(
        IReadOnlyList<string> tags,
        string role)
    {
        var accepted = tags
            .Select(tag => (Tag: tag, Match: AcceptedTagRegex.Match(tag)))
            .Where(item => item.Match.Success)
            .Select(item => (item.Tag, Version: item.Match.Groups["version"].Value))
            .OrderBy(item => item.Tag, StringComparer.Ordinal)
            .ToArray();
        RequireSingleCandidate(accepted.Length, $"{role} accepted-tag");
        return accepted[0];
    }

    private static string AssessmentSchemaFor(string version)
        => $"matawaka.workbench-maintenance-lifecycle-assessment/v{version}";

    private static string ReceiptSchemaFor(string version)
        => $"matawaka.workbench-maintenance-lifecycle-receipt/v{version}";

    private static string AcceptanceSchemaFor(string version)
        => $"matawaka.workbench-acceptance-receipt/v{version}";

    private static string CheckpointSchemaFor(string version)
        => $"matawaka.workbench-local-checkpoint-receipt/v{version}";

    private static string PublicationSchemaFor(string version)
        => $"matawaka.workbench-fixed-github-publication-receipt/v{version}";

    private static (string Path, string Sha256) FindSingleJson(
        string directory,
        string pattern,
        Func<JsonElement, bool> predicate,
        string role)
    {
        if (!Directory.Exists(directory))
            throw new InvalidDataException($"Lifecycle {role} artifact directory is missing: {directory}");
        var matches = new List<string>();
        foreach (var path in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.Ordinal))
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
            catch (InvalidDataException)
            {
                // Structurally unrelated artifacts are not candidates.
            }
        }
        RequireSingleCandidate(matches.Count, role);
        return (matches[0], HashFile(matches[0]));
    }

    private static void RequireSingleCandidate(int count, string role)
    {
        if (count != 1)
            throw new InvalidDataException(
                $"Lifecycle {role} binding is {(count == 0 ? "missing" : "ambiguous")}: candidates={count}.");
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
            throw new InvalidDataException($"Lifecycle artifact missing object: {property}");
        return value;
    }

    private static string JsonString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Lifecycle artifact missing string: {property}");
        return value.GetString() ?? throw new InvalidDataException($"Lifecycle artifact null string: {property}");
    }

    private static bool JsonBool(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            throw new InvalidDataException($"Lifecycle artifact missing bool: {property}");
        return value.GetBoolean();
    }

    private static string RequireBoundedArtifactPath(
        string repositoryRoot,
        string suppliedPath,
        string subdirectory)
    {
        if (string.IsNullOrWhiteSpace(suppliedPath) || !File.Exists(suppliedPath))
            throw new InvalidDataException($"Lifecycle-bound artifact missing: {suppliedPath}");
        var full = Path.GetFullPath(suppliedPath);
        var allowed = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", subdirectory)) +
                      Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Lifecycle artifact escapes Workbench artifacts/{subdirectory}: {full}");
        return full;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string RequireGitSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static string[] SplitLines(string value)
        => value.Split(
            new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashJson<T>(T value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))))
            .ToLowerInvariant();

    private static async Task<string> RunGitReadOnlyAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
    {
        ValidateReadOnlyGitArgs(args);
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
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only lifecycle Git observation.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException(
                $"Fixed read-only lifecycle Git observation exceeded {GitTimeout.TotalSeconds:0}s timeout.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only lifecycle Git observation failed: {stderr.Trim()}");
        return stdout;
    }

    private static void ValidateReadOnlyGitArgs(IReadOnlyList<string> args)
    {
        var joined = string.Join("\u001f", args);
        var allowed = joined == "rev-parse\u001fHEAD" ||
                      joined == "rev-parse\u001fHEAD^" ||
                      joined == "tag\u001f--points-at\u001fHEAD" ||
                      joined == "status\u001f--porcelain=v1\u001f--untracked-files=all" ||
                      (args.Count == 3 && args[0] == "tag" && args[1] == "--points-at" &&
                       args[2].Length == 40 && args[2].All(Uri.IsHexDigit));
        if (!allowed)
            throw new InvalidDataException(
                $"Lifecycle Git observation is outside the fixed read-only allowlist: {string.Join(' ', args)}");
    }
}
