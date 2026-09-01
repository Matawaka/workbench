using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record CandidateLaunchHandoffV039Receipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string CandidateLaunchArtifactPath,
    string CandidateLaunchArtifactSha256,
    string CandidateExecutablePath,
    string CandidateExecutableSha256,
    int ProcessId,
    int AliveObservationMilliseconds,
    bool LaunchReceiptVerified,
    bool CandidateObservedAlive,
    bool PredecessorSelfCloseEligible,
    bool CandidateAcceptanceCreated,
    bool ExternalProcessTerminationAuthorityCreated,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.39 post-launch handoff gate. It consumes an already-persisted successful
/// candidate-launch receipt, rebinds that evidence, waits one short bounded local
/// observation interval, and requires the launched PID to still be alive. It never
/// launches, kills, signals or accepts a process. PASS only makes the current
/// Workbench window eligible to close itself after the receipt has been persisted.
/// </summary>
public sealed class CandidateLaunchHandoffV039Service
{
    public const string Version = "0.39.0";
    public const string ReceiptSchema = "matawaka.workbench-candidate-launch-handoff-receipt/v0.39";
    public const string SuccessStatus = "CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED";
    public const int AliveObservationMilliseconds = 750;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(CandidateLaunchHandoffV039Receipt Receipt, string ArtifactPath)> ObserveAndPersistAsync(
        WorkbenchCandidateLaunchReceipt launchReceipt,
        string launchArtifactPath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (launchReceipt is null ||
            launchReceipt.Status != "CANDIDATE_LAUNCHED_NOT_ACCEPTED" ||
            launchReceipt.ProcessId <= 0)
            throw new InvalidDataException("A successful persisted candidate-launch receipt is required before v0.39 handoff.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var launchPath = ValidateLaunchArtifact(repositoryRoot, launchArtifactPath, launchReceipt);
        var candidatePath = Path.GetFullPath(launchReceipt.CandidateExecutablePath);
        var artifactPrefix = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts")) + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(artifactPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidatePath))
            throw new InvalidDataException("Receipt-bound candidate executable is missing or escapes Workbench artifacts during handoff.");
        if (!HashFile(candidatePath).Equals(launchReceipt.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Receipt-bound candidate executable changed before handoff.");

        await Task.Delay(AliveObservationMilliseconds, cancellationToken);

        bool alive;
        try
        {
            using var process = Process.GetProcessById(launchReceipt.ProcessId);
            process.Refresh();
            alive = !process.HasExited;
        }
        catch (ArgumentException)
        {
            alive = false;
        }
        catch (InvalidOperationException)
        {
            alive = false;
        }

        if (!alive)
            throw new InvalidDataException("Launched candidate exited before the bounded v0.39 handoff observation completed; predecessor remains open.");

        var nonEffects = new[]
        {
            "no candidate process launch performed by handoff observer",
            "no external process kill/termination/signal",
            "no candidate acceptance, checkpoint or publication authority",
            "no Workbench source or catalog mutation",
            "no network, Git or Agent Execute authority",
            "predecessor close eligibility applies only to the current Workbench MainWindow"
        };
        var receipt = new CandidateLaunchHandoffV039Receipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            launchPath,
            HashFile(launchPath),
            candidatePath,
            launchReceipt.CandidateExecutableSha256,
            launchReceipt.ProcessId,
            AliveObservationMilliseconds,
            true,
            true,
            true,
            false,
            false,
            nonEffects,
            SuccessStatus,
            "The existing candidate-launch receipt was rebound to exact local evidence and the launched PID remained alive after one bounded observation interval. This authorizes only the current predecessor Workbench window to close itself; candidate acceptance remains separate.");

        var dir = Path.Combine(repositoryRoot, "artifacts", "update-applies");
        Directory.CreateDirectory(dir);
        var finalPath = Path.Combine(dir, $"candidate-launch-handoff-v0.39-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        var tempPath = finalPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
            var parsed = JsonSerializer.Deserialize<CandidateLaunchHandoffV039Receipt>(
                await File.ReadAllTextAsync(tempPath, Encoding.UTF8, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("Temporary v0.39 handoff receipt could not be parsed.");
            RequireEquivalent(receipt, parsed);
            File.Move(tempPath, finalPath);
            return (receipt, finalPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("handoff-v039-bounded-alive-observation", AliveObservationMilliseconds is >= 250 and <= 2000, AliveObservationMilliseconds.ToString(), "250..2000 ms"),
        ("handoff-v039-launch-success-required", true, "CANDIDATE_LAUNCHED_NOT_ACCEPTED", "successful persisted launch receipt"),
        ("handoff-v039-candidate-acceptance-created", true, "false", "false"),
        ("handoff-v039-external-process-termination-authority", true, "false", "false"),
        ("handoff-v039-self-close-only", true, "current MainWindow Close() after PASS", "self-close only"),
        ("handoff-v039-success-status", SuccessStatus == "CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED", SuccessStatus, "CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED")
    };

    private static string ValidateLaunchArtifact(
        string repositoryRoot,
        string launchArtifactPath,
        WorkbenchCandidateLaunchReceipt expected)
    {
        if (string.IsNullOrWhiteSpace(launchArtifactPath) || !File.Exists(launchArtifactPath))
            throw new InvalidDataException("Candidate-launch artifact is missing before v0.39 handoff.");
        var full = Path.GetFullPath(launchArtifactPath);
        var prefix = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "update-applies")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate-launch artifact escapes Workbench/artifacts/update-applies.");
        var parsed = JsonSerializer.Deserialize<WorkbenchCandidateLaunchReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Candidate-launch artifact could not be parsed before v0.39 handoff.");
        if (parsed.Schema != expected.Schema || parsed.Version != expected.Version || parsed.Status != expected.Status ||
            parsed.ProcessId != expected.ProcessId ||
            !parsed.CandidateExecutablePath.Equals(expected.CandidateExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            !parsed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Persisted candidate-launch artifact differs from the in-memory launch receipt.");
        return full;
    }

    private static void RequireEquivalent(CandidateLaunchHandoffV039Receipt expected, CandidateLaunchHandoffV039Receipt observed)
    {
        if (observed.Schema != expected.Schema || observed.Version != expected.Version || observed.Status != expected.Status ||
            observed.ProcessId != expected.ProcessId || observed.AliveObservationMilliseconds != expected.AliveObservationMilliseconds ||
            !observed.CandidateLaunchArtifactSha256.Equals(expected.CandidateLaunchArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            !observed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
            !observed.LaunchReceiptVerified || !observed.CandidateObservedAlive || !observed.PredecessorSelfCloseEligible ||
            observed.CandidateAcceptanceCreated || observed.ExternalProcessTerminationAuthorityCreated)
            throw new InvalidDataException("Persisted v0.39 handoff receipt differs from the verified in-memory receipt.");
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(root)) throw new InvalidDataException($"Workbench root missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
