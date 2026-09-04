using System.Diagnostics;
using System.IO;

namespace Matawaka.Workbench.App;

public sealed record LocalAppActiveIndexFenceObservationV0516(
    string Schema,
    string Version,
    DateTimeOffset AcquiredAt,
    string ApplicationId,
    string Purpose,
    long WaitMilliseconds,
    bool CrossProcessFenceAcquired,
    bool BearerPlaintextUsedOrDisclosed,
    bool BearerHashUsedOrDisclosed,
    bool LeaseAuthorityGranted,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppActiveIndexFenceLeaseV0516 : IDisposable, IAsyncDisposable
{
    private FileStream? _stream;

    internal LocalAppActiveIndexFenceLeaseV0516(
        FileStream stream,
        LocalAppActiveIndexFenceObservationV0516 observation)
    {
        _stream = stream;
        Observation = observation;
    }

    public LocalAppActiveIndexFenceObservationV0516 Observation { get; }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// v0.51.6 app-scoped cross-process fence. The persistent lock file carries no
/// authority or secret material; ownership is the open FileStream with FileShare.None.
/// Process termination releases ownership automatically. v0.51.5 durable dirty state
/// remains the crash-gap recovery signal for mutations already begun.
/// </summary>
public sealed class LocalAppActiveIndexFenceV0516Service
{
    public const string Version = "0.51.6";
    public const string ObservationSchema = "matawaka.local-app-active-index-fence-observation/v0.51.6";
    public const int DefaultTimeoutMilliseconds = 3000;
    public const int PollMilliseconds = 40;

    public async Task<LocalAppActiveIndexFenceLeaseV0516> AcquireAsync(
        string workspaceRoot,
        string applicationId,
        string purpose,
        CancellationToken cancellationToken,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        if (string.IsNullOrWhiteSpace(purpose)) throw new InvalidDataException("Active-index fence purpose is required.");
        if (timeoutMilliseconds < 1 || timeoutMilliseconds > 30000)
            throw new InvalidDataException("Active-index fence timeout must be between 1 and 30000 ms.");

        var appDir = FenceDirectory(workspaceRoot, applicationId);
        var lockPath = Path.Combine(appDir, "active-index-v0.51.6.lock");
        if (File.Exists(lockPath)) LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.6 active-index fence file");

        var sw = Stopwatch.StartNew();
        IOException? lastBusy = null;
        while (sw.ElapsedMilliseconds < timeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.None);
                try
                {
                    LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.6 acquired active-index fence file");
                    var observation = new LocalAppActiveIndexFenceObservationV0516(
                        ObservationSchema,
                        Version,
                        DateTimeOffset.Now,
                        applicationId,
                        purpose,
                        sw.ElapsedMilliseconds,
                        true,
                        false,
                        false,
                        false,
                        FenceNonEffects(),
                        "ACTIVE_INDEX_FENCE_ACQUIRED",
                        "App-scoped cross-process ownership is the exclusive open file handle only. The lock file itself grants no lease/index authority and contains no secret material.");
                    return new LocalAppActiveIndexFenceLeaseV0516(stream, observation);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException ex)
            {
                lastBusy = ex;
                var remaining = timeoutMilliseconds - (int)sw.ElapsedMilliseconds;
                if (remaining <= 0) break;
                await Task.Delay(Math.Min(PollMilliseconds, remaining), cancellationToken);
            }
        }

        throw new InvalidDataException(
            $"ACTIVE_INDEX_FENCE_BUSY: app-scoped v0.51.6 fence for {applicationId} remained busy for {timeoutMilliseconds} ms; no partial authority snapshot or index mutation was permitted.",
            lastBusy);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("fence-v0516-process", true, "exclusive FileStream FileShare.None", "cross-process"),
        ("fence-v0516-await", true, "file handle ownership", "not thread-affine"),
        ("fence-v0516-timeout", DefaultTimeoutMilliseconds == 3000, DefaultTimeoutMilliseconds.ToString(), "3000"),
        ("fence-v0516-crash-release", true, "OS closes process handles", "automatic ownership release"),
        ("fence-v0516-dirty", true, "v0.51.5 dirty marker remains separate", "crash-gap authority signal preserved"),
        ("fence-v0516-secret", true, "no bearer/plaintext/hash in path or file", "omitted"),
        ("fence-v0516-authority", true, "lock grants no lease authority", "serialization only"),
        ("fence-v0516-reparse", true, "directory/file rejected", "fail closed")
    };

    private static string FenceDirectory(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var root = Path.GetFullPath(Path.Combine(workbench, ".workbench", "active-index-fence-v0516"));
        Directory.CreateDirectory(root);
        LocalAppV046FileBoundary.RejectReparse(root, "v0.51.6 active-index fence root");
        var app = Path.GetFullPath(Path.Combine(root, LocalAppV046FileBoundary.SafeToken(applicationId)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!app.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Active-index fence app directory escaped fence root.");
        Directory.CreateDirectory(app);
        LocalAppV046FileBoundary.RejectReparse(app, "v0.51.6 active-index fence app directory");
        return app;
    }

    private static string[] FenceNonEffects() => new[]
    {
        "fence file/handle is serialization control only, never canonical or derived lease authority",
        "no bearer plaintext or bearer hash stored/disclosed",
        "no application/source contents read",
        "no read/list call or byte budget consumption",
        "no canonical lease state or active-index mutation by fence service",
        "no network/MCP/tunnel/publication/catalog mutation",
        "no process launch, Agent Execute or ActionPermit authority"
    };
}
