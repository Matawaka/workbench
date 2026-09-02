using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record OpenAiTunnelClientObservationV050(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    string ReportedVersion,
    string ExpectedVersion,
    bool FixedToolPath,
    bool ReparseFree,
    bool VersionMatched,
    string Note);

public sealed record OpenAiSecureMcpTunnelPreviewV050(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    DateTimeOffset LeaseExpiresAt,
    string TunnelId,
    string TunnelClientExecutablePath,
    string TunnelClientExecutableSha256,
    string TunnelClientReportedVersion,
    bool LocalMcpAdapterActive,
    bool RuntimeApiKeyValidatedInMemory,
    bool ContainsRuntimeApiKey,
    bool ContainsLocalMcpEndpoint,
    bool ReadyForExplicitOutboundTunnelAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record OpenAiSecureMcpTunnelGrantV050(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    DateTimeOffset LeaseExpiresAt,
    string TunnelId,
    int ProcessId,
    string TunnelClientExecutableSha256,
    string TunnelClientReportedVersion,
    string HealthLoopbackUrl,
    bool Ready,
    bool OutboundNetworkAccessPerformed,
    bool PublicListenerStarted,
    bool RuntimeApiKeyPlaintextPersisted,
    bool LocalMcpEndpointPlaintextPersisted,
    bool ChatGptConnectionConfigured,
    string Note);

public sealed record OpenAiSecureMcpTunnelStartReceiptV050(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string TunnelId,
    DateTimeOffset LeaseExpiresAt,
    int ProcessId,
    string TunnelClientExecutablePath,
    string TunnelClientExecutableSha256,
    string TunnelClientReportedVersion,
    string HealthLoopbackUrl,
    bool TunnelClientReady,
    bool RuntimeApiKeyPassedByChildEnvironmentOnly,
    bool LocalMcpEndpointPassedByChildEnvironmentOnly,
    bool RuntimeApiKeyPlaintextPersisted,
    bool LocalMcpEndpointPlaintextPersisted,
    bool OutboundNetworkAccessPerformed,
    bool PublicListenerStarted,
    bool AutomaticChatGptConfigurationPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record OpenAiSecureMcpTunnelStopReceiptV050(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string TunnelId,
    int ProcessId,
    bool ProcessStopped,
    bool ExactWorkbenchChildOnly,
    bool RuntimeApiKeyPlaintextPersisted,
    bool LocalMcpEndpointPlaintextPersisted,
    bool ReadLeaseRevokedByTunnelStop,
    bool McpAdapterStoppedByTunnelStop,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class OpenAiSecureMcpTunnelV050Service
{
    public const string Version = "0.50.0";
    public const string ExpectedTunnelClientVersion = "0.0.14";
    public const string ToolObservationSchema = "matawaka.openai-tunnel-client-observation/v0.50";
    public const string PreviewSchema = "matawaka.local-app-secure-mcp-tunnel-preview/v0.50";
    public const string GrantSchema = "matawaka.local-app-secure-mcp-tunnel-grant/v0.50";
    public const string StartReceiptSchema = "matawaka.local-app-secure-mcp-tunnel-start-receipt/v0.50";
    public const string StopReceiptSchema = "matawaka.local-app-secure-mcp-tunnel-stop-receipt/v0.50";
    public const string RuntimeKeyEnvironmentName = "CONTROL_PLANE_API_KEY";
    public const string TunnelIdEnvironmentName = "CONTROL_PLANE_TUNNEL_ID";
    public const string McpServerUrlEnvironmentName = "MCP_SERVER_URL";
    public const string RelativeToolPath = "Tools/OpenAI/tunnel-client/tunnel-client-runtime.exe";
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveTunnel? _active;

    public bool IsActiveFor(string applicationId)
        => _active is { } active && active.ApplicationId.Equals(applicationId, StringComparison.Ordinal) && !active.Process.HasExited;

    public async Task<OpenAiTunnelClientObservationV050> ObserveTunnelClientAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var workspace = Path.GetFullPath(workspaceRoot.Trim());
        var toolPath = Path.GetFullPath(Path.Combine(workspace, RelativeToolPath.Replace('/', Path.DirectorySeparatorChar)));
        var expected = Path.GetFullPath(Path.Combine(workspace, "Tools", "OpenAI", "tunnel-client", "tunnel-client-runtime.exe"));
        if (!toolPath.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("OpenAI tunnel-client path is not the fixed Workbench Tools path.");
        if (!File.Exists(toolPath))
            throw new InvalidDataException($"OpenAI tunnel-client runtime is missing. Manually place the verified official {ExpectedTunnelClientVersion} Windows amd64 runtime binary at: {toolPath}");
        RejectReparseChain(workspace, toolPath);
        var info = new FileInfo(toolPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("OpenAI tunnel-client executable is a reparse point.");

        var reported = await ReadVersionAsync(toolPath, cancellationToken);
        var matched = reported.Equals(ExpectedTunnelClientVersion, StringComparison.Ordinal) || reported.StartsWith(ExpectedTunnelClientVersion + " ", StringComparison.Ordinal);
        if (!matched) throw new InvalidDataException($"OpenAI tunnel-client runtime version is {reported}; v0.50 is pinned to {ExpectedTunnelClientVersion}.");

        return new OpenAiTunnelClientObservationV050(
            ToolObservationSchema, Version, DateTimeOffset.Now, toolPath, HashFile(toolPath), info.Length,
            reported, ExpectedTunnelClientVersion, true, true, true,
            "Workbench observes a fixed-path runtime-only OpenAI tunnel-client binary, its exact SHA-256 and self-reported version. The operator remains responsible for obtaining the binary from the official OpenAI release and verifying the release archive against OpenAI SHA256SUMS before placement.");
    }

    public async Task<OpenAiSecureMcpTunnelPreviewV050> PreviewAsync(
        string workspaceRoot,
        string selectedApplicationId,
        string leaseId,
        DateTimeOffset leaseExpiresAt,
        string tunnelId,
        string runtimeApiKey,
        bool localMcpAdapterActive,
        CancellationToken cancellationToken)
    {
        if (!localMcpAdapterActive) throw new InvalidDataException("Start the lease-gated local MCP adapter before starting Secure MCP Tunnel.");
        if (!SafeTunnelId(tunnelId)) throw new InvalidDataException("Tunnel ID must be exactly tunnel_ followed by 32 lowercase hexadecimal characters.");
        ValidateRuntimeApiKey(runtimeApiKey);
        if (leaseExpiresAt <= DateTimeOffset.Now) throw new InvalidDataException("The bound read lease has already expired.");
        if (leaseExpiresAt - DateTimeOffset.Now > TimeSpan.FromMinutes(16)) throw new InvalidDataException("Unexpected lease expiry horizon; v0.50 tunnel must remain bounded to the active read lease.");
        if (string.IsNullOrWhiteSpace(selectedApplicationId) || string.IsNullOrWhiteSpace(leaseId)) throw new InvalidDataException("Selected app and lease id are required.");
        var observed = await ObserveTunnelClientAsync(workspaceRoot, cancellationToken);
        return new OpenAiSecureMcpTunnelPreviewV050(
            PreviewSchema, Version, DateTimeOffset.Now, selectedApplicationId, leaseId, leaseExpiresAt, tunnelId,
            observed.ExecutablePath, observed.ExecutableSha256, observed.ReportedVersion,
            true, true, false, false, true, DefaultNonEffects(),
            "Preview contains no API key and no secret local MCP endpoint. Explicit confirmation is still required before an outbound OpenAI Secure MCP Tunnel process is started.");
    }

    public async Task<OpenAiSecureMcpTunnelGrantV050> StartAsync(
        string workspaceRoot,
        OpenAiSecureMcpTunnelPreviewV050 confirmedPreview,
        string runtimeApiKey,
        string localMcpEndpoint,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not null && !_active.Process.HasExited) throw new InvalidDataException("A Secure MCP Tunnel process is already active in this Workbench process.");
            if (confirmedPreview.Schema != PreviewSchema || confirmedPreview.Version != Version || !confirmedPreview.ReadyForExplicitOutboundTunnelAuthority)
                throw new InvalidDataException("Confirmed Secure MCP Tunnel preview does not match v0.50 contract.");
            ValidateRuntimeApiKey(runtimeApiKey);
            ValidateLocalMcpEndpoint(localMcpEndpoint);
            if (confirmedPreview.LeaseExpiresAt <= DateTimeOffset.Now) throw new InvalidDataException("Read lease expired before tunnel startup.");
            var observed = await ObserveTunnelClientAsync(workspaceRoot, cancellationToken);
            if (!observed.ExecutableSha256.Equals(confirmedPreview.TunnelClientExecutableSha256, StringComparison.OrdinalIgnoreCase) || observed.ReportedVersion != confirmedPreview.TunnelClientReportedVersion)
                throw new InvalidDataException("OpenAI tunnel-client binary changed after preview.");

            var runtimeDir = ResolveRuntimeDirectory(workspaceRoot);
            Directory.CreateDirectory(runtimeDir);
            var healthPath = Path.Combine(runtimeDir, "health-" + Guid.NewGuid().ToString("N") + ".url");
            var psi = new ProcessStartInfo
            {
                FileName = observed.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(observed.ExecutablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--control-plane.api-key");
            psi.ArgumentList.Add("env:" + RuntimeKeyEnvironmentName);
            psi.ArgumentList.Add("--health.listen-addr");
            psi.ArgumentList.Add("127.0.0.1:0");
            psi.ArgumentList.Add("--health.url-file");
            psi.ArgumentList.Add(healthPath);
            psi.Environment[RuntimeKeyEnvironmentName] = runtimeApiKey;
            psi.Environment[TunnelIdEnvironmentName] = confirmedPreview.TunnelId;
            psi.Environment[McpServerUrlEnvironmentName] = localMcpEndpoint;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidDataException("Failed to start the fixed OpenAI tunnel-client runtime process.");
            ClearSensitiveParentStartInfo(process);

            Uri healthBase;
            try
            {
                healthBase = await WaitForReadyAsync(process, healthPath, cancellationToken);
            }
            catch
            {
                TryKill(process);
                TryDelete(healthPath);
                process.Dispose();
                throw;
            }
            TryDelete(healthPath);

            var active = new ActiveTunnel(
                process, confirmedPreview.ApplicationId, confirmedPreview.LeaseId, confirmedPreview.TunnelId,
                confirmedPreview.LeaseExpiresAt, observed.ExecutablePath, observed.ExecutableSha256, observed.ReportedVersion,
                healthBase.ToString().TrimEnd('/'), new CancellationTokenSource());
            _active = active;
            active.ExpiryTask = MonitorLeaseExpiryAsync(active);

            return new OpenAiSecureMcpTunnelGrantV050(
                GrantSchema, Version, DateTimeOffset.Now, active.ApplicationId, active.LeaseId, active.LeaseExpiresAt,
                active.TunnelId, process.Id, active.ExecutableSha256, active.ReportedVersion, active.HealthLoopbackUrl,
                true, true, false, false, false, false,
                "The official OpenAI tunnel-client runtime reported local readiness. This proves a running outbound tunnel process, not ChatGPT-side connector activation and not authority beyond the existing read lease.");
        }
        finally { _gate.Release(); }
    }

    public async Task<(OpenAiSecureMcpTunnelStartReceiptV050 Receipt, string ReceiptPath)> WriteStartReceiptAsync(string workspaceRoot, OpenAiSecureMcpTunnelGrantV050 grant, CancellationToken cancellationToken)
    {
        var active = _active ?? throw new InvalidDataException("No active Secure MCP Tunnel exists for a start receipt.");
        if (active.ApplicationId != grant.ApplicationId || active.LeaseId != grant.LeaseId || active.TunnelId != grant.TunnelId || active.Process.Id != grant.ProcessId)
            throw new InvalidDataException("Secure MCP Tunnel grant does not match current runtime.");
        var receipt = new OpenAiSecureMcpTunnelStartReceiptV050(
            StartReceiptSchema, Version, DateTimeOffset.Now, active.ApplicationId, active.LeaseId, active.TunnelId,
            active.LeaseExpiresAt, active.Process.Id, active.ExecutablePath, active.ExecutableSha256, active.ReportedVersion,
            active.HealthLoopbackUrl, true, true, true, false, false, true, false, false,
            DefaultNonEffects(), "SECURE_MCP_TUNNEL_RUNTIME_READY_CHATGPT_CONNECTION_SEPARATE",
            "Runtime API key and secret local MCP URL were passed only through the child process environment and immediately removed from the parent ProcessStartInfo. They are not persisted in this receipt. ChatGPT-side connection remains a separate human action.");
        var path = await WriteArtifactAsync(workspaceRoot, "start", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
        return (receipt, path);
    }

    public async Task<(OpenAiSecureMcpTunnelStopReceiptV050 Receipt, string ReceiptPath)> StopAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = _active ?? throw new InvalidDataException("No active Secure MCP Tunnel exists in this Workbench process.");
            _active = null;
            active.StopMonitor.Cancel();
            var stopped = StopExactChild(active.Process);
            var receipt = new OpenAiSecureMcpTunnelStopReceiptV050(
                StopReceiptSchema, Version, DateTimeOffset.Now, active.ApplicationId, active.LeaseId, active.TunnelId,
                active.ProcessId, stopped, true, false, false, false, false,
                DefaultNonEffects(), "SECURE_MCP_TUNNEL_STOPPED_LOCAL_CHILD_ONLY",
                "Only the exact tunnel-client child process started by this Workbench session was stopped. MCP adapter stop and read-lease revocation remain separate explicit operations.");
            active.Process.Dispose();
            active.StopMonitor.Dispose();
            var path = await WriteArtifactAsync(workspaceRoot, "stop", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
            return (receipt, path);
        }
        finally { _gate.Release(); }
    }

    public async Task StopBestEffortAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var active = _active;
            _active = null;
            if (active is null) return;
            active.StopMonitor.Cancel();
            try { StopExactChild(active.Process); } catch { }
            try { active.Process.Dispose(); } catch { }
            try { active.StopMonitor.Dispose(); } catch { }
        }
        finally { _gate.Release(); }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("tunnel-v050-fixed-runtime-tool", RelativeToolPath == "Tools/OpenAI/tunnel-client/tunnel-client-runtime.exe", RelativeToolPath, "fixed runtime-only tool path"),
        ("tunnel-v050-pinned-client", ExpectedTunnelClientVersion == "0.0.14", ExpectedTunnelClientVersion, "0.0.14"),
        ("tunnel-v050-key-env-only", RuntimeKeyEnvironmentName == "CONTROL_PLANE_API_KEY", RuntimeKeyEnvironmentName, "child environment only"),
        ("tunnel-v050-mcp-url-env-only", McpServerUrlEnvironmentName == "MCP_SERVER_URL", McpServerUrlEnvironmentName, "child environment only"),
        ("tunnel-v050-tunnel-id-env", TunnelIdEnvironmentName == "CONTROL_PLANE_TUNNEL_ID", TunnelIdEnvironmentName, "session environment"),
        ("tunnel-v050-health-loopback", true, "--health.listen-addr 127.0.0.1:0 + /readyz", "loopback readiness required"),
        ("tunnel-v050-no-admin", true, "run only; no admin/init/runtimes connect", "no tunnel CRUD/admin authority"),
        ("tunnel-v050-chatgpt-config", true, "not modified by Workbench", "separate human action"),
        ("tunnel-v050-expiry", true, "child stopped at lease expiry", "transport lifetime <= lease lifetime")
    };

    private async Task MonitorLeaseExpiryAsync(ActiveTunnel active)
    {
        try
        {
            var delay = active.LeaseExpiresAt - DateTimeOffset.Now;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, active.StopMonitor.Token);
            if (active.StopMonitor.IsCancellationRequested) return;
            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(_active, active)) return;
                _active = null;
                TryKill(active.Process);
                active.Process.Dispose();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (active.StopMonitor.IsCancellationRequested) { }
        catch { }
    }

    private static async Task<Uri> WaitForReadyAsync(Process process, string healthPath, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(750) };
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidDataException($"OpenAI tunnel-client exited before readiness; exitCode={process.ExitCode}.");
            if (File.Exists(healthPath))
            {
                var info = new FileInfo(healthPath);
                if (info.Length > 2048) throw new InvalidDataException("tunnel-client health URL file exceeds bounded size.");
                var raw = (await File.ReadAllTextAsync(healthPath, Encoding.UTF8, cancellationToken)).Trim();
                if (Uri.TryCreate(raw, UriKind.Absolute, out var baseUri) && IsExactLoopbackHttp(baseUri))
                {
                    var ready = new Uri(baseUri.ToString().TrimEnd('/') + "/readyz");
                    try
                    {
                        using var response = await client.GetAsync(ready, cancellationToken);
                        if ((int)response.StatusCode is >= 200 and < 300) return baseUri;
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                }
            }
            await Task.Delay(75, cancellationToken);
        }
        throw new InvalidDataException("Timed out waiting for the OpenAI tunnel-client loopback /readyz endpoint.");
    }

    private static async Task<string> ReadVersionAsync(string toolPath, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var psi = new ProcessStartInfo
        {
            FileName = toolPath,
            WorkingDirectory = Path.GetDirectoryName(toolPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--version");
        psi.Environment.Remove(RuntimeKeyEnvironmentName);
        psi.Environment.Remove(TunnelIdEnvironmentName);
        psi.Environment.Remove(McpServerUrlEnvironmentName);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to execute tunnel-client --version.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new InvalidDataException("tunnel-client --version timed out.");
        }
        var output = (await stdout).Trim();
        var error = (await stderr).Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) throw new InvalidDataException($"tunnel-client --version failed: {error}");
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    }

    private static void ClearSensitiveParentStartInfo(Process process)
    {
        process.StartInfo.Environment[RuntimeKeyEnvironmentName] = string.Empty;
        process.StartInfo.Environment[McpServerUrlEnvironmentName] = string.Empty;
        process.StartInfo.Environment[TunnelIdEnvironmentName] = string.Empty;
    }

    private static bool StopExactChild(Process process)
    {
        if (process.HasExited) return true;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { return true; }
        if (!process.WaitForExit(5000)) throw new InvalidDataException("Exact tunnel-client child process did not stop within 5 seconds.");
        return process.HasExited;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        try { process.WaitForExit(2000); } catch { }
    }

    private static void ValidateLocalMcpEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) || uri.Port <= 0 || !uri.AbsolutePath.StartsWith("/mcp/", StringComparison.Ordinal))
            throw new InvalidDataException("Secure MCP Tunnel target must be the exact active v0.49.1 IPv4-loopback MCP endpoint.");
        var token = uri.AbsolutePath[5..];
        if (token.Length != 64 || token.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("Local MCP endpoint token shape is invalid.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Local MCP endpoint must not contain query, fragment or userinfo.");
    }

    private static bool IsExactLoopbackHttp(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp && uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) && uri.Port > 0 && string.IsNullOrEmpty(uri.UserInfo);

    private static void ValidateRuntimeApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 16 || value.Length > 4096 || value.Any(ch => ch is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Runtime API key is empty or has an unsafe shape. It is intentionally not persisted or echoed.");
    }

    public static bool SafeTunnelId(string value)
        => value.Length == 39 && value.StartsWith("tunnel_", StringComparison.Ordinal) && value[7..].All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ResolveRuntimeDirectory(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench", ".workbench", "secure-mcp-tunnel"));
        var workbench = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(workbench, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tunnel runtime state escaped Workbench .workbench root.");
        return root;
    }

    private static void RejectReparseChain(string workspace, string target)
    {
        var root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new DirectoryInfo(Path.GetDirectoryName(target)!);
        while (current is not null && current.FullName.Length >= root.Length)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Reparse directory refused in tunnel-client tool path: {current.FullName}");
            if (current.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
    }

    private static async Task<string> WriteArtifactAsync<T>(string workspaceRoot, string kind, string applicationId, string leaseId, T receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench", "artifacts", "secure-mcp-tunnel");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"secure-mcp-tunnel-{kind}-{SafeToken(applicationId)}-{SafeToken(leaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string SafeToken(string value) => new(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_').Take(96).ToArray());
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static string[] DefaultNonEffects() => new[]
    {
        "no tunnel creation/deletion/admin authority",
        "no automatic ChatGPT connector/settings mutation",
        "no public inbound listener; local MCP and health remain IPv4 loopback",
        "no runtime API key plaintext persistence",
        "no local MCP endpoint plaintext persistence in receipt",
        "no read lease creation/renewal/scope widening",
        "no application/source mutation",
        "no arbitrary process execution; exact fixed tunnel-client runtime child only",
        "no Git/catalog/Agent Execute/ActionPermit authority"
    };

    private sealed class ActiveTunnel
    {
        public Process Process { get; }
        public int ProcessId { get; }
        public string ApplicationId { get; }
        public string LeaseId { get; }
        public string TunnelId { get; }
        public DateTimeOffset LeaseExpiresAt { get; }
        public string ExecutablePath { get; }
        public string ExecutableSha256 { get; }
        public string ReportedVersion { get; }
        public string HealthLoopbackUrl { get; }
        public CancellationTokenSource StopMonitor { get; }
        public Task? ExpiryTask { get; set; }

        public ActiveTunnel(Process process, string applicationId, string leaseId, string tunnelId, DateTimeOffset leaseExpiresAt, string executablePath, string executableSha256, string reportedVersion, string healthLoopbackUrl, CancellationTokenSource stopMonitor)
        {
            Process = process; ProcessId = process.Id; ApplicationId = applicationId; LeaseId = leaseId; TunnelId = tunnelId;
            LeaseExpiresAt = leaseExpiresAt; ExecutablePath = executablePath; ExecutableSha256 = executableSha256; ReportedVersion = reportedVersion;
            HealthLoopbackUrl = healthLoopbackUrl; StopMonitor = stopMonitor;
        }
    }
}
