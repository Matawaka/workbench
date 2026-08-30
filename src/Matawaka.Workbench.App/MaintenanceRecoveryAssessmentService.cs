using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record MaintenanceEvidenceItem(
    string Kind,
    string Path,
    string? Schema,
    string? Status,
    string? TargetVersion,
    DateTimeOffset LastWriteTime);

public sealed record MaintenanceRecoveryAssessmentReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string CurrentHead,
    IReadOnlyList<string> CurrentTags,
    bool WorkingTreeClean,
    IReadOnlyList<string> DirtyPaths,
    IReadOnlyList<string> SourceBackupRoots,
    IReadOnlyList<string> CandidateRoots,
    IReadOnlyList<MaintenanceEvidenceItem> RecentMaintenanceEvidence,
    string Classification,
    bool RecoveryRequired,
    bool RecoveryActionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Observation-only recovery surface. It inspects the Workbench repository and
/// local maintenance artifacts to classify interrupted/stale maintenance state.
/// It does not repair, delete, restore, build, checkpoint, fetch, or execute an agent.
/// </summary>
public sealed class MaintenanceRecoveryAssessmentService
{
    public const string Version = "0.16.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<MaintenanceRecoveryAssessmentReceipt> AssessAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (string.IsNullOrWhiteSpace(currentHead))
            throw new InvalidDataException("Workbench Git repository has no HEAD.");

        var tags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        var dirtyPaths = ParseStatusPaths(status);

        var backupBase = Path.Combine(repositoryRoot, ".workbench", "update-source-backups");
        var backupRoots = Directory.Exists(backupBase)
            ? Directory.GetDirectories(backupBase).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(20).ToArray()
            : Array.Empty<string>();

        var candidateRoots = Directory.Exists(Path.Combine(repositoryRoot, "artifacts"))
            ? Directory.GetDirectories(Path.Combine(repositoryRoot, "artifacts"), "app-v*-gui-update", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Directory.GetLastWriteTimeUtc).Take(20).ToArray()
            : Array.Empty<string>();

        var evidence = ReadRecentEvidence(repositoryRoot);
        var expectedDirty = ExtractExpectedDirtyPaths(repositoryRoot, evidence);
        var dirtyIsBounded = dirtyPaths.Count > 0 && expectedDirty.Count > 0 && dirtyPaths.All(expectedDirty.Contains);

        var classification = dirtyPaths.Count switch
        {
            0 when candidateRoots.Length == 0 && backupRoots.Length == 0 => "CLEAN_ACCEPTED",
            0 => "CLEAN_ACCEPTED_WITH_STALE_MAINTENANCE_EVIDENCE",
            _ when dirtyIsBounded => "BOUNDED_DIRTY_UPDATE_CANDIDATE",
            _ => "UNKNOWN_DIRTY_WORKTREE"
        };
        var recoveryRequired = dirtyPaths.Count > 0;

        var nonEffects = new[]
        {
            "no source file mutation",
            "no source restore or rollback",
            "no file or directory deletion",
            "no dotnet build or publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "assessment artifact write is limited to Workbench/artifacts/recovery-assessments"
        };

        return new MaintenanceRecoveryAssessmentReceipt(
            "matawaka.workbench-maintenance-recovery-assessment/v0.16",
            Version,
            DateTimeOffset.Now,
            repositoryRoot,
            currentHead,
            tags,
            dirtyPaths.Count == 0,
            dirtyPaths,
            backupRoots,
            candidateRoots,
            evidence,
            classification,
            recoveryRequired,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            nonEffects,
            "Observation is not recovery authority. Bounded candidate evidence may support a later explicit recovery plan, but this receipt cannot restore, delete, apply, build, checkpoint, publish, mutate the catalog, access the network, or grant Agent Execute.");
    }

    private static IReadOnlyList<MaintenanceEvidenceItem> ReadRecentEvidence(string repositoryRoot)
    {
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "artifacts", "update-applies"),
            Path.Combine(repositoryRoot, "artifacts", "update-apply-plans"),
            Path.Combine(repositoryRoot, "artifacts", "update-materializations"),
            Path.Combine(repositoryRoot, "artifacts", "update-plans")
        };
        var files = roots.Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(30)
            .ToArray();

        var items = new List<MaintenanceEvidenceItem>();
        foreach (var file in files)
        {
            string? schema = null;
            string? status = null;
            string? targetVersion = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                var root = doc.RootElement;
                schema = ReadString(root, "Schema");
                status = ReadString(root, "Status");
                targetVersion = ReadString(root, "TargetVersion");
            }
            catch
            {
                schema = "unreadable-json";
            }
            items.Add(new MaintenanceEvidenceItem(
                ClassifyEvidence(file),
                file,
                schema,
                status,
                targetVersion,
                new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)));
        }
        return items;
    }

    private static HashSet<string> ExtractExpectedDirtyPaths(string repositoryRoot, IReadOnlyList<MaintenanceEvidenceItem> evidence)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in evidence.Where(x => x.Kind == "apply-build-receipt").Take(5))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item.Path, Encoding.UTF8));
                var root = doc.RootElement;
                foreach (var propertyName in new[] { "SourceChanges", "AppliedChanges", "Changes" })
                {
                    if (!root.TryGetProperty(propertyName, out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                    foreach (var change in changes.EnumerateArray())
                    {
                        var path = ReadString(change, "Path");
                        if (!string.IsNullOrWhiteSpace(path)) expected.Add(NormalizeRelativePath(path));
                    }
                }
            }
            catch
            {
                // Unreadable evidence cannot enlarge the bounded expected set.
            }
        }
        return expected;
    }

    private static string ClassifyEvidence(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("apply-build-", StringComparison.OrdinalIgnoreCase)) return "apply-build-receipt";
        if (name.Contains("authority", StringComparison.OrdinalIgnoreCase)) return "authority-receipt";
        if (name.Contains("materialization", StringComparison.OrdinalIgnoreCase)) return "materialization-receipt";
        if (name.Contains("apply-plan", StringComparison.OrdinalIgnoreCase)) return "apply-plan-receipt";
        return "update-receipt";
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            path = path.Trim('"');
            paths.Add(NormalizeRelativePath(path));
        }
        return paths.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot, "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "rev-parse", "tag", "status" };
        if (args.Length == 0 || !allowed.Contains(args[0])) throw new InvalidDataException("Recovery assessment attempted a non-allowlisted Git operation.");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Read-only Git recovery assessment timed out after 10 seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException($"Read-only Git recovery assessment failed: {stderr.Trim()}");
        return stdout;
    }
}
