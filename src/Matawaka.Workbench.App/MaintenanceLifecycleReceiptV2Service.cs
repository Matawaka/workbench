using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

/// <summary>
/// Successor-generic lifecycle evidence adapter v2.
/// It preserves the accepted audit-only lifecycle contract while explicitly
/// separating the accepted tag/schema token (for example 0.35) from the full
/// semantic Workbench Version in receipts (for example 0.35.0).
/// </summary>
public sealed class MaintenanceLifecycleReceiptV2Service
{
    public const string AdapterVersion = "2";

    private static readonly Regex AcceptedTagRegex = new(
        "^workbench-v(?<token>[0-9]+\\.[0-9]+(?:\\.[0-9]+)*)-accepted$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SemanticVersionRegex = new(
        "^(?<major>[0-9]+)\\.(?<minor>[0-9]+)\\.(?<patch>[0-9]+)$",
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
        var targetTagToken = currentAccepted.Token;
        var targetTag = currentAccepted.Tag;

        var predecessorAccepted = RequireSingleAcceptedTag(
            SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", parent)),
            "predecessor commit");
        var predecessorCommit = parent;
        var predecessorTag = predecessorAccepted.Tag;

        // Important: checkpoint candidate selection is bound by tag/schema/head/parent,
        // not by semantic Version equality with the tag token. This is the v0.35 fix.
        var checkpointMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "acceptance"),
            "checkpoint-v*.json",
            doc => JsonString(doc, "Schema") == CheckpointSchemaForToken(targetTagToken) &&
                   JsonString(doc, "Tag") == targetTag &&
                   JsonString(doc, "PreviousHead").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase) &&
                   JsonString(doc, "NewHead").Equals(head, StringComparison.OrdinalIgnoreCase),
            "checkpoint");
        using var checkpointDoc = ParseJson(checkpointMatch.Path);
        var checkpoint = checkpointDoc.RootElement;

        var semanticTargetVersion = JsonString(checkpoint, "Version");
        var normalizedTargetToken = NormalizeSemanticVersionForAcceptedToken(semanticTargetVersion);
        if (!string.Equals(normalizedTargetToken, targetTagToken, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Lifecycle checkpoint semantic version does not normalize to accepted tag/schema token. version={semanticTargetVersion}; token={targetTagToken}");

        var acceptancePath = RequireBoundedArtifactPath(
            repositoryRoot,
            JsonString(checkpoint, "AcceptanceArtifactPath"),
            "acceptance");
        var acceptanceExpectedSha = RequireSha256(
            JsonString(checkpoint, "AcceptanceArtifactSha256"),
            "checkpoint acceptance SHA-256");
        var acceptanceActualSha = HashFile(acceptancePath);
        if (!acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact SHA-256 mismatch.");

        using var acceptanceDoc = ParseJson(acceptancePath);
        var acceptance = acceptanceDoc.RootElement;
        if (JsonString(acceptance, "Schema") != AcceptanceSchemaForToken(targetTagToken) ||
            JsonString(acceptance, "Version") != semanticTargetVersion ||
            !JsonBool(acceptance, "Passed"))
            throw new InvalidDataException("Checkpoint-bound acceptance artifact is not a passing Self-test receipt for the accepted tag/schema token and semantic version.");

        var executableSha = RequireSha256(
            JsonString(acceptance, "AppExecutableSha256"),
            "acceptance executable SHA-256");
        if (!executableSha.Equals(JsonString(checkpoint, "AppExecutableSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint executable digest does not match exact passing acceptance artifact.");

        var orchestratorMatch = FindSingleJson(
            Path.Combine(repositoryRoot, "artifacts", "update-orchestrator"),
            "update-orchestrator-v*.json",
            doc => JsonString(doc, "Schema").StartsWith(
                       "matawaka.workbench-maintenance-update-orchestrator-receipt/",
                       StringComparison.Ordinal) &&
                   JsonString(doc, "TargetVersion") == semanticTargetVersion &&
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
            doc => JsonString(doc, "Schema") == PublicationSchemaForToken(targetTagToken) &&
                   JsonString(doc, "Version") == semanticTargetVersion &&
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
            Check("current-accepted-tag-unique", true, targetTag, "one workbench-v<token>-accepted tag at HEAD"),
            Check("tag-schema-token-derived", true, targetTagToken, "accepted tag token"),
            Check("semantic-version-normalizes-to-tag-token", normalizedTargetToken == targetTagToken, semanticTargetVersion, targetTagToken),
            Check("current-head-is-checkpoint-head", head.Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), head, JsonString(checkpoint, "NewHead")),
            Check("predecessor-is-git-parent", predecessorCommit.Equals(JsonString(checkpoint, "PreviousHead"), StringComparison.OrdinalIgnoreCase), predecessorCommit, JsonString(checkpoint, "PreviousHead")),
            Check("predecessor-accepted-tag-unique", true, predecessorTag, "one workbench-v<token>-accepted tag at predecessor"),
            Check("working-tree-clean", string.IsNullOrWhiteSpace(status), string.IsNullOrWhiteSpace(status) ? "clean" : status.Trim(), "clean"),
            Check("acceptance-artifact-digest-exact", acceptanceActualSha.Equals(acceptanceExpectedSha, StringComparison.OrdinalIgnoreCase), acceptanceActualSha, acceptanceExpectedSha),
            Check("acceptance-passed", JsonBool(acceptance, "Passed"), JsonBool(acceptance, "Passed").ToString(), "true"),
            Check("acceptance-semantic-version-exact", JsonString(acceptance, "Version") == semanticTargetVersion, JsonString(acceptance, "Version"), semanticTargetVersion),
            Check("candidate-executable-bound-across-update-and-acceptance", JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(JsonObject(orchestrator, "ApplyBuild"), "CandidateExecutableSha256"), executableSha),
            Check("candidate-executable-bound-across-acceptance-and-checkpoint", JsonString(checkpoint, "AppExecutableSha256").Equals(executableSha, StringComparison.OrdinalIgnoreCase), JsonString(checkpoint, "AppExecutableSha256"), executableSha),
            Check("orchestrator-stopped-before-launch", !JsonBool(orchestrator, "LaunchPerformed"), JsonBool(orchestrator, "LaunchPerformed").ToString(), "false"),
            Check("checkpoint-does-not-imply-publication", !JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed"), JsonBool(JsonObject(checkpoint, "Authority"), "RemotePushAllowed").ToString(), "false"),
            Check("publication-semantic-version-exact", JsonString(publication, "Version") == semanticTargetVersion, JsonString(publication, "Version"), semanticTargetVersion),
            Check("publication-head-equals-checkpoint-head", JsonString(publication, "LocalHead").Equals(JsonString(checkpoint, "NewHead"), StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalHead"), JsonString(checkpoint, "NewHead")),
            Check("publication-parent-exact", JsonString(publication, "LocalParent").Equals(predecessorCommit, StringComparison.OrdinalIgnoreCase), JsonString(publication, "LocalParent"), predecessorCommit),
            Check("remote-main-exact", JsonString(publication, "RemoteMainAfter").Equals(head, StringComparison.OrdinalIgnoreCase), JsonString(publication, "RemoteMainAfter"), head),
            Check("remote-tag-exact", JsonString(publication, "RemoteTagAfter").Equals(head, StringComparison.OrdinalIgnoreCase), JsonString(publication, "RemoteTagAfter"), head),
            Check("publication-local-state-unchanged", JsonBool(publication, "LocalHeadUnchanged") && JsonBool(publication, "WorkingTreeUnchanged"), $"head={JsonBool(publication, "LocalHeadUnchanged")}; tree={JsonBool(publication, "WorkingTreeUnchanged")}", "true / true")
        };
        if (!checks.All(x => x.Passed))
            throw new InvalidDataException("Lifecycle v2 relation checks are incomplete. Refusing a complete lifecycle assessment.");

        var nonEffects = new[]
        {
            "no update package materialization or source mutation",
            "no build or candidate launch",
            "no Self-test invocation",
            "no git add/commit/tag",
            "no git push/fetch/remote mutation",
            "no publication retry authority",
            "no rollback authority",
            "no local application update",
            "no catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no general network authority",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "accepted tag/schema token discovery is evidence routing only, not semantic-version authority or trust discovery",
            "lifecycle receipt write is local evidence only"
        };

        return new MaintenanceLifecycleAssessment(
            AssessmentSchemaForToken(targetTagToken),
            semanticTargetVersion,
            DateTimeOffset.Now,
            predecessorCommit,
            predecessorTag,
            semanticTargetVersion,
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
            $"Complete means only that exact local maintenance evidence forms one verified semantic Workbench {semanticTargetVersion} lifecycle under accepted tag/schema token {targetTagToken}. Tag/schema token and semantic Version were bound explicitly rather than assumed equal. No action, retry, rollback, trust or authority is created.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        MaintenanceLifecycleAssessment assessment,
        CancellationToken cancellationToken)
    {
        if (assessment is null || !assessment.Complete || assessment.AuthorityCreated ||
            assessment.ActionPerformed || assessment.RetryAuthorized || assessment.RollbackAuthorized)
            throw new InvalidDataException("Only a complete non-authorizing lifecycle v2 assessment may be written as a receipt.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var directory = Path.Combine(repositoryRoot, "artifacts", "lifecycle");
        Directory.CreateDirectory(directory);
        var semanticVersion = assessment.TargetVersion;
        var token = NormalizeSemanticVersionForAcceptedToken(semanticVersion);
        var digest = HashJson(assessment);
        var receipt = new MaintenanceLifecycleReceipt(
            ReceiptSchemaForToken(token),
            semanticVersion,
            DateTimeOffset.Now,
            assessment,
            digest,
            true,
            false,
            false,
            false,
            false,
            assessment.NonEffects,
            "Local lifecycle v2 evidence write only. Accepted tag/schema token != semantic runtime Version; summary != authority; observed sequence != authorized sequence.");
        var path = Path.Combine(
            directory,
            $"maintenance-lifecycle-v{token}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        return path;
    }

    public static IReadOnlyList<MaintenanceLifecycleCheck> RunOfflineContractChecks()
    {
        var checks = new List<MaintenanceLifecycleCheck>();
        AddNormalizationCheck(checks, "lifecycle-v2-normalize-patch", "0.34.1", "0.34.1");
        AddNormalizationCheck(checks, "lifecycle-v2-normalize-minor-semver", "0.35.0", "0.35");
        AddNormalizationCheck(checks, "lifecycle-v2-normalize-nonzero-patch", "0.35.1", "0.35.1");

        var mismatchRefused = false;
        try { RequireSemanticVersionMatchesToken("0.35.1", "0.35"); }
        catch (InvalidDataException) { mismatchRefused = true; }
        checks.Add(Check("lifecycle-v2-nonzero-patch-not-collapsed", mismatchRefused, mismatchRefused.ToString(), "true"));

        var minorSchema = CheckpointSchemaForToken(NormalizeSemanticVersionForAcceptedToken("0.35.0"));
        checks.Add(Check(
            "lifecycle-v2-semantic-0350-checkpoint-schema",
            minorSchema == "matawaka.workbench-local-checkpoint-receipt/v0.35",
            minorSchema,
            "matawaka.workbench-local-checkpoint-receipt/v0.35"));

        var acceptanceSchema = AcceptanceSchemaForToken(NormalizeSemanticVersionForAcceptedToken("0.35.0"));
        checks.Add(Check(
            "lifecycle-v2-semantic-0350-acceptance-schema",
            acceptanceSchema == "matawaka.workbench-acceptance-receipt/v0.35",
            acceptanceSchema,
            "matawaka.workbench-acceptance-receipt/v0.35"));

        var publicationSchema = PublicationSchemaForToken(NormalizeSemanticVersionForAcceptedToken("0.35.0"));
        checks.Add(Check(
            "lifecycle-v2-semantic-0350-publication-schema",
            publicationSchema == "matawaka.workbench-fixed-github-publication-receipt/v0.35",
            publicationSchema,
            "matawaka.workbench-fixed-github-publication-receipt/v0.35"));

        var missingRefused = false;
        try { RequireSingleCandidate(0, "fixture"); }
        catch (InvalidDataException) { missingRefused = true; }
        checks.Add(Check("lifecycle-v2-missing-artifact-refused", missingRefused, missingRefused.ToString(), "true"));

        var ambiguousRefused = false;
        try { RequireSingleCandidate(2, "fixture"); }
        catch (InvalidDataException) { ambiguousRefused = true; }
        checks.Add(Check("lifecycle-v2-ambiguous-artifact-refused", ambiguousRefused, ambiguousRefused.ToString(), "true"));

        return checks;
    }

    public static string NormalizeSemanticVersionForAcceptedToken(string semanticVersion)
    {
        if (string.IsNullOrWhiteSpace(semanticVersion))
            throw new InvalidDataException("Semantic Workbench version is required.");
        var match = SemanticVersionRegex.Match(semanticVersion.Trim());
        if (!match.Success)
            throw new InvalidDataException($"Unsupported semantic Workbench version: {semanticVersion}");
        var major = match.Groups["major"].Value;
        var minor = match.Groups["minor"].Value;
        var patch = match.Groups["patch"].Value;
        return patch == "0" ? $"{major}.{minor}" : $"{major}.{minor}.{patch}";
    }

    public static void RequireSemanticVersionMatchesToken(string semanticVersion, string token)
    {
        var normalized = NormalizeSemanticVersionForAcceptedToken(semanticVersion);
        if (!string.Equals(normalized, token, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Semantic Workbench version does not match accepted tag/schema token after normalization. version={semanticVersion}; normalized={normalized}; token={token}");
    }

    private static void AddNormalizationCheck(
        ICollection<MaintenanceLifecycleCheck> checks,
        string id,
        string semanticVersion,
        string expectedToken)
    {
        var observed = NormalizeSemanticVersionForAcceptedToken(semanticVersion);
        checks.Add(Check(id, observed == expectedToken, observed, expectedToken));
    }

    private static (string Tag, string Token) RequireSingleAcceptedTag(
        IReadOnlyList<string> tags,
        string role)
    {
        var accepted = tags
            .Select(tag => (Tag: tag, Match: AcceptedTagRegex.Match(tag)))
            .Where(item => item.Match.Success)
            .Select(item => (item.Tag, Token: item.Match.Groups["token"].Value))
            .OrderBy(item => item.Tag, StringComparer.Ordinal)
            .ToArray();
        RequireSingleCandidate(accepted.Length, $"{role} accepted-tag");
        return accepted[0];
    }

    private static string AssessmentSchemaForToken(string token)
        => $"matawaka.workbench-maintenance-lifecycle-assessment/v{token}";

    private static string ReceiptSchemaForToken(string token)
        => $"matawaka.workbench-maintenance-lifecycle-receipt/v{token}";

    private static string AcceptanceSchemaForToken(string token)
        => $"matawaka.workbench-acceptance-receipt/v{token}";

    private static string CheckpointSchemaForToken(string token)
        => $"matawaka.workbench-local-checkpoint-receipt/v{token}";

    private static string PublicationSchemaForToken(string token)
        => $"matawaka.workbench-fixed-github-publication-receipt/v{token}";

    private static (string Path, string Sha256) FindSingleJson(
        string directory,
        string pattern,
        Func<JsonElement, bool> predicate,
        string role)
    {
        if (!Directory.Exists(directory))
            throw new InvalidDataException($"Lifecycle v2 {role} artifact directory is missing: {directory}");
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
                // Unrelated invalid artifacts are not candidates.
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
                $"Lifecycle v2 {role} binding is {(count == 0 ? "missing" : "ambiguous")}: candidates={count}.");
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
            throw new InvalidDataException($"Lifecycle v2 artifact missing object: {property}");
        return value;
    }

    private static string JsonString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Lifecycle v2 artifact missing string: {property}");
        return value.GetString() ?? throw new InvalidDataException($"Lifecycle v2 artifact null string: {property}");
    }

    private static bool JsonBool(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            throw new InvalidDataException($"Lifecycle v2 artifact missing bool: {property}");
        return value.GetBoolean();
    }

    private static string RequireBoundedArtifactPath(
        string repositoryRoot,
        string suppliedPath,
        string subdirectory)
    {
        if (string.IsNullOrWhiteSpace(suppliedPath) || !File.Exists(suppliedPath))
            throw new InvalidDataException($"Lifecycle v2 bound artifact missing: {suppliedPath}");
        var full = Path.GetFullPath(suppliedPath);
        var allowed = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", subdirectory)) +
                      Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Lifecycle v2 artifact escapes Workbench artifacts/{subdirectory}: {full}");
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

    private static string RequireSha256(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 64 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"{role} is not SHA-256: {sha}");
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
            throw new InvalidDataException("Failed to start fixed read-only lifecycle v2 Git observation.");
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
                $"Fixed read-only lifecycle v2 Git observation exceeded {GitTimeout.TotalSeconds:0}s timeout.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only lifecycle v2 Git observation failed: {stderr.Trim()}");
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
                $"Lifecycle v2 Git observation is outside fixed read-only allowlist: {string.Join(' ', args)}");
    }
}
