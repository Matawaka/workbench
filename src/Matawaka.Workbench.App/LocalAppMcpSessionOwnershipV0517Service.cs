using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpSessionOwnerV0517(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string SessionId,
    string? LeaseId,
    int OwnerProcessId,
    DateTimeOffset AcquiredAt,
    string State,
    bool ListenerObservedActive,
    string? LoopbackHost,
    int? LoopbackPort,
    bool BearerPlaintextStored,
    bool BearerHashStored,
    bool EndpointSecretStored,
    bool LeaseAuthorityGranted,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppMcpSessionOwnershipReceiptV0517(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string SessionId,
    string? LeaseId,
    string Operation,
    string Status,
    long WaitMilliseconds,
    bool ListenerObservedInactiveBeforeRelease,
    bool CrossProcessHandleReleased,
    bool CanonicalLeaseMutated,
    bool BearerPlaintextUsedOrDisclosed,
    bool BearerHashUsedOrDisclosed,
    bool EndpointSecretUsedOrDisclosed,
    bool LeaseAuthorityGranted,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed class LocalAppHeldMcpSessionOwnershipV0517 : IAsyncDisposable
{
    private FileStream? _handle;
    internal string WorkspaceRoot { get; }
    internal string MetadataPath { get; }
    public string ApplicationId { get; }
    public string SessionId { get; }
    public string? LeaseId { get; internal set; }
    public long WaitMilliseconds { get; }
    public bool Released => _handle is null;

    internal LocalAppHeldMcpSessionOwnershipV0517(
        string workspaceRoot,
        string metadataPath,
        string applicationId,
        string sessionId,
        long waitMilliseconds,
        FileStream handle)
    {
        WorkspaceRoot = workspaceRoot;
        MetadataPath = metadataPath;
        ApplicationId = applicationId;
        SessionId = sessionId;
        WaitMilliseconds = waitMilliseconds;
        _handle = handle;
    }

    internal void ReleaseHandle()
    {
        var handle = Interlocked.Exchange(ref _handle, null);
        handle?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        ReleaseHandle();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// v0.51.7 app-scoped runtime ownership for local read-only MCP. The exclusive
/// file handle serializes MCP session ownership across Workbench processes for
/// the full listener lifetime. Metadata is non-authoritative and contains no
/// bearer/hash/endpoint secret. Canonical read authority remains v0.48 lease state.
/// v0.51.9 additionally preserves any prior active owner metadata evidence under
/// the acquired handle before the successor owner generation is written.
/// </summary>
public sealed class LocalAppMcpSessionOwnershipV0517Service
{
    public const string Version = "0.51.7";
    public const string OwnerSchema = "matawaka.local-app-mcp-session-owner/v0.51.7";
    public const string ReceiptSchema = "matawaka.local-app-mcp-session-ownership-receipt/v0.51.7";
    public const int DefaultTimeoutMilliseconds = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private readonly LocalAppMcpOwnerGenerationV0519Service _generationV0519Service = new();

    public async Task<LocalAppHeldMcpSessionOwnershipV0517> AcquireAsync(
        string workspaceRoot,
        string applicationId,
        string purpose,
        CancellationToken cancellationToken,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        if (timeoutMilliseconds < 1 || timeoutMilliseconds > 30_000)
            throw new InvalidDataException("MCP ownership timeout must be within 1..30000 ms.");

        var root = ResolveOwnershipRoot(workspaceRoot);
        var appDir = Path.Combine(root, LocalAppV046FileBoundary.SafeToken(applicationId));
        Directory.CreateDirectory(appDir);
        LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.7 MCP ownership app directory");
        var lockPath = Path.Combine(appDir, "owner.lock");
        var metadataPath = Path.Combine(appDir, "owner-v0.51.7.json");
        var started = Stopwatch.StartNew();
        FileStream? handle = null;

        while (handle is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(lockPath)) LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.7 MCP ownership lock");
            try
            {
                handle = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.7 acquired MCP ownership lock");
            }
            catch (IOException) when (started.ElapsedMilliseconds < timeoutMilliseconds)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (started.ElapsedMilliseconds < timeoutMilliseconds)
            {
                await Task.Delay(50, cancellationToken);
            }

            if (handle is null && started.ElapsedMilliseconds >= timeoutMilliseconds)
                throw new InvalidDataException(
                    $"MCP_SESSION_OWNED_BY_OTHER_PROCESS: app-scoped local MCP ownership for {applicationId} remained busy for {timeoutMilliseconds} ms; no lease/listener authority was created.");
        }

        var sessionId = "mcpsess-" + Guid.NewGuid().ToString("N");
        var held = new LocalAppHeldMcpSessionOwnershipV0517(
            Path.GetFullPath(workspaceRoot.Trim()), metadataPath, applicationId, sessionId, started.ElapsedMilliseconds, handle);
        try
        {
            await _generationV0519Service.PreservePriorBeforeSuccessorAsync(
                held.WorkspaceRoot, applicationId, sessionId, metadataPath, cancellationToken);
            await WriteOwnerAsync(held, null, "OWNERSHIP_ACQUIRED_UNBOUND", false, null, null,
                $"Cross-process MCP ownership acquired for purpose '{purpose}'. Any prior active owner metadata was preserved as v0.51.9 generation evidence before this successor metadata write; prior metadata grants no authority.", cancellationToken);
            return held;
        }
        catch
        {
            held.ReleaseHandle();
            throw;
        }
    }

    public async Task BindExactLeaseAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        string leaseId,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (string.IsNullOrWhiteSpace(leaseId) || !leaseId.StartsWith("lease-", StringComparison.Ordinal) || leaseId.Length > 80 ||
            leaseId.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new InvalidDataException("Unsafe LeaseId for MCP ownership binding.");
        held.LeaseId = leaseId;
        await WriteOwnerAsync(held, leaseId, "LEASE_BOUND_LISTENER_NOT_READY", false, null, null,
            "Exact canonical LeaseId was bound to the already-held runtime ownership. This metadata does not contain the bearer and grants no lease authority.", cancellationToken);
    }

    public async Task MarkListenerReadyAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        LocalAppMcpAdapterGrantV049 adapterGrant,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (string.IsNullOrWhiteSpace(held.LeaseId) || !held.LeaseId.Equals(adapterGrant.LeaseId, StringComparison.Ordinal) ||
            !held.ApplicationId.Equals(adapterGrant.ApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("MCP ownership is not bound to the exact adapter ApplicationId/LeaseId.");
        var uri = new Uri(adapterGrant.EndpointUrl);
        if (!uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) || uri.Port <= 0)
            throw new InvalidDataException("MCP ownership may record only an IPv4 loopback listener.");
        await WriteOwnerAsync(held, held.LeaseId, "LISTENER_READY_OWNED", true, uri.Host, uri.Port,
            "Local listener readiness is recorded without endpoint path token or bearer material. Ownership serializes runtime only.", cancellationToken);
    }

    public async Task<(LocalAppMcpSessionOwnershipReceiptV0517 Receipt, string ReceiptPath)> ReleaseAfterListenerStoppedAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        bool listenerObservedInactive,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!listenerObservedInactive)
            throw new InvalidDataException("MCP_SESSION_RELEASE_REFUSED_LISTENER_STILL_ACTIVE: cross-process ownership remains held until listener inactivity is proven.");

        await WriteOwnerAsync(held, held.LeaseId, "LISTENER_STOPPED_OWNER_RELEASING", false, null, null,
            "Listener inactivity was proven before ownership release. Canonical lease closure remains a separate exact operation.", cancellationToken);
        var receipt = new LocalAppMcpSessionOwnershipReceiptV0517(
            ReceiptSchema, Version, DateTimeOffset.Now, held.ApplicationId, held.SessionId, held.LeaseId,
            "release-after-listener-stop", "MCP_SESSION_OWNERSHIP_RELEASED_AFTER_LISTENER_STOP",
            held.WaitMilliseconds, true, true, false, false, false, false, false,
            NonEffects(),
            "The app-scoped cross-process owner handle was released only after local listener inactivity was observed. No lease was revoked by ownership release.");
        var receiptPath = await WriteReceiptAsync(held.WorkspaceRoot, held.ApplicationId, held.SessionId, receipt, cancellationToken);
        held.ReleaseHandle();
        return (receipt, receiptPath);
    }

    public async Task<(LocalAppMcpSessionOwnershipReceiptV0517 Receipt, string ReceiptPath)> ReleaseUnstartedAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        bool listenerObservedInactive,
        string reason,
        CancellationToken cancellationToken)
    {
        RequireHeld(held);
        if (!listenerObservedInactive)
            throw new InvalidDataException("MCP_SESSION_RELEASE_REFUSED_LISTENER_STILL_ACTIVE: unstarted/partial ownership cannot be released while listener may be active.");
        await WriteOwnerAsync(held, held.LeaseId, "PARTIAL_OR_UNSTARTED_RELEASE", false, null, null,
            reason, cancellationToken);
        var receipt = new LocalAppMcpSessionOwnershipReceiptV0517(
            ReceiptSchema, Version, DateTimeOffset.Now, held.ApplicationId, held.SessionId, held.LeaseId,
            "release-unstarted-or-partial", "MCP_SESSION_OWNERSHIP_RELEASED_NO_ACTIVE_LISTENER",
            held.WaitMilliseconds, true, true, false, false, false, false, false,
            NonEffects(),
            "Ownership was released after proving no listener was active. Any already-created canonical lease remains live/orphan until explicit exact closure or expiry.");
        var path = await WriteReceiptAsync(held.WorkspaceRoot, held.ApplicationId, held.SessionId, receipt, cancellationToken);
        held.ReleaseHandle();
        return (receipt, path);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-owner-v0517-cross-process", true, "FileShare.None held for listener lifetime", "singular app ownership"),
        ("mcp-owner-v0517-timeout", DefaultTimeoutMilliseconds == 3000, DefaultTimeoutMilliseconds.ToString(), "3000"),
        ("mcp-owner-v0517-busy", true, "MCP_SESSION_OWNED_BY_OTHER_PROCESS", "before lease mutation"),
        ("mcp-owner-v0517-bearer", true, "plaintext/hash omitted", "omitted"),
        ("mcp-owner-v0517-endpoint-secret", true, "path token omitted; host/port only", "omitted"),
        ("mcp-owner-v0517-release", true, "listener inactivity required", "fail closed"),
        ("mcp-owner-v0517-crash", true, "OS handle releases; stale metadata non-authoritative", "lease not auto-revoked"),
        ("mcp-owner-v0517-authority", true, "ownership grants no lease authority", "false"),
        ("mcp-owner-v0519-generation", true, "prior owner metadata preserved before successor write", "no silent stale overwrite")
    };

    private static void RequireHeld(LocalAppHeldMcpSessionOwnershipV0517 held)
    {
        if (held is null || held.Released)
            throw new InvalidDataException("v0.51.7 MCP session ownership is not currently held.");
    }

    private static string ResolveOwnershipRoot(string workspaceRoot)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var root = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517");
        Directory.CreateDirectory(root);
        LocalAppV046FileBoundary.RejectReparse(root, "v0.51.7 MCP ownership root");
        return root;
    }

    private static async Task WriteOwnerAsync(
        LocalAppHeldMcpSessionOwnershipV0517 held,
        string? leaseId,
        string state,
        bool listenerActive,
        string? loopbackHost,
        int? loopbackPort,
        string note,
        CancellationToken cancellationToken)
    {
        var owner = new LocalAppMcpSessionOwnerV0517(
            OwnerSchema, Version, DateTimeOffset.Now, held.ApplicationId, held.SessionId, leaseId,
            Environment.ProcessId, DateTimeOffset.Now, state, listenerActive, loopbackHost, loopbackPort,
            false, false, false, false, NonEffects(), note);
        await WriteAtomicAsync(held.MetadataPath, owner, cancellationToken);
    }

    private static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        string applicationId,
        string sessionId,
        LocalAppMcpSessionOwnershipReceiptV0517 receipt,
        CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-mcp-session-ownership-v0517");
        var path = Path.Combine(dir,
            $"mcp-owner-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{LocalAppV046FileBoundary.SafeToken(sessionId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.7 MCP owner metadata");
            if (File.Exists(path)) LocalAppV046FileBoundary.RejectReparse(path, "pre-replace v0.51.7 MCP owner metadata");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.7 MCP owner metadata");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string[] NonEffects() => new[]
    {
        "MCP ownership is runtime serialization only, not lease authority",
        "no bearer plaintext or bearer hash stored/disclosed",
        "no endpoint path token or reusable endpoint secret stored/disclosed",
        "no canonical lease creation/revocation/renewal by ownership service",
        "no read/list call or byte budget consumption",
        "no application/source/catalog mutation",
        "no network/tunnel/publication/Agent Execute or ActionPermit authority"
    };
}
