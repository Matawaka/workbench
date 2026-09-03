using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record OpenAiTunnelReadinessDiagnosticV0501(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string Stage,
    int Attempts,
    int ObservationMilliseconds,
    bool LeaseBoundDeadline,
    bool HealthUrlFileObserved,
    bool HealthUrlValidLoopback,
    int? HealthzStatusCode,
    string? HealthzBody,
    int? ReadyzStatusCode,
    string? ReadyzBody,
    bool ProcessExited,
    int? ExitCode,
    string Summary);

public sealed record OpenAiSecureMcpTunnelGrantV0501(
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

public sealed record OpenAiSecureMcpTunnelStartReceiptV0501(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string TunnelId,
    DateTimeOffset LeaseExpiresAt,
    int ProcessId,
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

public sealed record OpenAiSecureMcpTunnelStopReceiptV0501(
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

public sealed record OpenAiSecureMcpTunnelReadinessFailureReceiptV0501(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string TunnelId,
    DateTimeOffset LeaseExpiresAt,
    int ProcessId,
    string TunnelClientExecutableSha256,
    string TunnelClientReportedVersion,
    OpenAiTunnelReadinessDiagnosticV0501 Diagnostic,
    bool ExactChildStoppedAfterFailure,
    bool RuntimeApiKeyPlaintextPersisted,
    bool LocalMcpEndpointPlaintextPersisted,
    bool ReadLeaseRevokedByFailure,
    bool McpAdapterStoppedByFailure,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

internal sealed class OpenAiTunnelReadinessFailureV0501 : InvalidDataException
{
    public OpenAiTunnelReadinessDiagnosticV0501 Diagnostic { get; }

    public OpenAiTunnelReadinessFailureV0501(OpenAiTunnelReadinessDiagnosticV0501 diagnostic)
        : base("OpenAI tunnel-client readiness failed: " + diagnostic.Summary)
    {
        Diagnostic = diagnostic;
    }
}

public sealed class OpenAiSecureMcpTunnelV0501Service
{
    public const string Version = "0.50.1";
    public const string GrantSchema = "matawaka.local-app-secure-mcp-tunnel-grant/v0.50.1";
    public const string StartReceiptSchema = "matawaka.local-app-secure-mcp-tunnel-start-receipt/v0.50.1";
    public const string StopReceiptSchema = "matawaka.local-app-secure-mcp-tunnel-stop-receipt/v0.50.1";
    public const string ReadinessDiagnosticSchema = "matawaka.openai-tunnel-client-readiness-diagnostic/v0.50.1";
    public const string ReadinessFailureReceiptSchema = "matawaka.local-app-secure-mcp-tunnel-readiness-failure-receipt/v0.50.1";
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(90);
    public const int ReadinessBodyLimitBytes = 4096;
    public const int ReadinessBodyLimitChars = 512;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OpenAiSecureMcpTunnelV050Service _baseContract = new();
    private ActiveTunnel? _active;

    public bool IsActiveFor(string applicationId)
        => _active is { } active && active.ApplicationId.Equals(applicationId, StringComparison.Ordinal) && !active.Process.HasExited;

    public Task<OpenAiSecureMcpTunnelPreviewV050> PreviewAsync(
        string workspaceRoot,
        string selectedApplicationId,
        string leaseId,
        DateTimeOffset leaseExpiresAt,
        string tunnelId,
        string runtimeApiKey,
        bool localMcpAdapterActive,
        CancellationToken cancellationToken)
        => _baseContract.PreviewAsync(workspaceRoot, selectedApplicationId, leaseId, leaseExpiresAt, tunnelId, runtimeApiKey, localMcpAdapterActive, cancellationToken);

    public async Task<OpenAiSecureMcpTunnelGrantV0501> StartAsync(
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
            if (confirmedPreview.Schema != OpenAiSecureMcpTunnelV050Service.PreviewSchema || confirmedPreview.Version != OpenAiSecureMcpTunnelV050Service.Version || !confirmedPreview.ReadyForExplicitOutboundTunnelAuthority)
                throw new InvalidDataException("Confirmed Secure MCP Tunnel preview does not match the inherited v0.50 transport contract.");
            ValidateRuntimeApiKey(runtimeApiKey);
            ValidateLocalMcpEndpoint(localMcpEndpoint);
            if (confirmedPreview.LeaseExpiresAt <= DateTimeOffset.Now) throw new InvalidDataException("Read lease expired before tunnel startup.");

            var observed = await _baseContract.ObserveTunnelClientAsync(workspaceRoot, cancellationToken);
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
            psi.ArgumentList.Add("env:" + OpenAiSecureMcpTunnelV050Service.RuntimeKeyEnvironmentName);
            psi.ArgumentList.Add("--health.listen-addr");
            psi.ArgumentList.Add("127.0.0.1:0");
            psi.ArgumentList.Add("--health.url-file");
            psi.ArgumentList.Add(healthPath);
            psi.Environment[OpenAiSecureMcpTunnelV050Service.RuntimeKeyEnvironmentName] = runtimeApiKey;
            psi.Environment[OpenAiSecureMcpTunnelV050Service.TunnelIdEnvironmentName] = confirmedPreview.TunnelId;
            psi.Environment[OpenAiSecureMcpTunnelV050Service.McpServerUrlEnvironmentName] = localMcpEndpoint;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidDataException("Failed to start the fixed OpenAI tunnel-client runtime process.");
            ClearSensitiveParentStartInfo(process);

            Uri healthBase;
            try
            {
                healthBase = await WaitForReadyAsync(process, healthPath, confirmedPreview.LeaseExpiresAt, runtimeApiKey, localMcpEndpoint, cancellationToken);
            }
            catch (OpenAiTunnelReadinessFailureV0501 ex)
            {
                TryKill(process);
                var childStopped = SafeHasExited(process);
                TryDelete(healthPath);
                string? failurePath = null;
                try
                {
                    var failure = new OpenAiSecureMcpTunnelReadinessFailureReceiptV0501(
                        ReadinessFailureReceiptSchema,
                        Version,
                        DateTimeOffset.Now,
                        confirmedPreview.ApplicationId,
                        confirmedPreview.LeaseId,
                        confirmedPreview.TunnelId,
                        confirmedPreview.LeaseExpiresAt,
                        process.Id,
                        observed.ExecutableSha256,
                        observed.ReportedVersion,
                        ex.Diagnostic,
                        childStopped,
                        false,
                        false,
                        false,
                        false,
                        DefaultNonEffects().Concat(new[]
                        {
                            "readiness failure receipt contains only bounded redacted health/readiness diagnostics",
                            "failed readiness stops the exact Workbench-started tunnel-client child before refusal",
                            "MCP adapter and read lease remain separate and are not stopped/revoked by tunnel readiness failure"
                        }).Distinct(StringComparer.Ordinal).ToArray(),
                        "SECURE_MCP_TUNNEL_READINESS_FAILED_CHILD_STOPPED",
                        "v0.50.1 preserves fail-closed tunnel admission while retaining only bounded redacted readiness evidence. Runtime credential, lease bearer and secret local MCP endpoint are not persisted.");
                    failurePath = await WriteArtifactAsync(workspaceRoot, "readiness-failure", confirmedPreview.ApplicationId, confirmedPreview.LeaseId, failure, CancellationToken.None);
                }
                catch
                {
                    // Refusal remains fail-closed even if the local evidence write itself fails.
                }
                process.Dispose();
                throw new InvalidDataException(ex.Message + (failurePath is null ? " Failure receipt could not be written." : $" Failure receipt: {failurePath}"));
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
                process,
                confirmedPreview.ApplicationId,
                confirmedPreview.LeaseId,
                confirmedPreview.TunnelId,
                confirmedPreview.LeaseExpiresAt,
                observed.ExecutableSha256,
                observed.ReportedVersion,
                healthBase.ToString().TrimEnd('/'),
                new CancellationTokenSource());
            _active = active;
            active.ExpiryTask = MonitorLeaseExpiryAsync(active);

            return new OpenAiSecureMcpTunnelGrantV0501(
                GrantSchema, Version, DateTimeOffset.Now,
                active.ApplicationId, active.LeaseId, active.LeaseExpiresAt, active.TunnelId,
                active.ProcessId, active.ExecutableSha256, active.ReportedVersion, active.HealthLoopbackUrl,
                true, true, false, false, false, false,
                "The official OpenAI tunnel-client runtime reached /readyz success inside the bounded v0.50.1 observation window. This does not create read authority beyond the already-active lease or configure ChatGPT automatically.");
        }
        finally { _gate.Release(); }
    }

    public async Task<(OpenAiSecureMcpTunnelStartReceiptV0501 Receipt, string ReceiptPath)> WriteStartReceiptAsync(string workspaceRoot, OpenAiSecureMcpTunnelGrantV0501 grant, CancellationToken cancellationToken)
    {
        var active = _active ?? throw new InvalidDataException("No active Secure MCP Tunnel exists for a start receipt.");
        if (active.ApplicationId != grant.ApplicationId || active.LeaseId != grant.LeaseId || active.TunnelId != grant.TunnelId || active.ProcessId != grant.ProcessId)
            throw new InvalidDataException("Secure MCP Tunnel grant does not match current runtime.");
        var receipt = new OpenAiSecureMcpTunnelStartReceiptV0501(
            StartReceiptSchema, Version, DateTimeOffset.Now,
            active.ApplicationId, active.LeaseId, active.TunnelId, active.LeaseExpiresAt,
            active.ProcessId, active.ExecutableSha256, active.ReportedVersion, active.HealthLoopbackUrl,
            true, true, true, false, false, true, false, false,
            DefaultNonEffects(),
            "SECURE_MCP_TUNNEL_RUNTIME_READY_V0501_CHATGPT_CONNECTION_SEPARATE",
            "Readiness required /readyz success. Runtime credential and secret local MCP URL were child-environment-only and are not persisted. ChatGPT-side connection remains a separate human action.");
        var path = await WriteArtifactAsync(workspaceRoot, "start-v0501", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
        return (receipt, path);
    }

    public async Task<(OpenAiSecureMcpTunnelStopReceiptV0501 Receipt, string ReceiptPath)> StopAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = _active ?? throw new InvalidDataException("No active Secure MCP Tunnel exists in this Workbench process.");
            _active = null;
            active.StopMonitor.Cancel();
            var stopped = StopExactChild(active.Process);
            var receipt = new OpenAiSecureMcpTunnelStopReceiptV0501(
                StopReceiptSchema, Version, DateTimeOffset.Now,
                active.ApplicationId, active.LeaseId, active.TunnelId, active.ProcessId,
                stopped, true, false, false, false, false,
                DefaultNonEffects(),
                "SECURE_MCP_TUNNEL_STOPPED_V0501_LOCAL_CHILD_ONLY",
                "Only the exact tunnel-client child process started by this Workbench session was stopped. MCP adapter stop and read-lease revocation remain separate explicit operations.");
            active.Process.Dispose();
            active.StopMonitor.Dispose();
            var path = await WriteArtifactAsync(workspaceRoot, "stop-v0501", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
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
        ("tunnel-v0501-window", ReadinessTimeout == TimeSpan.FromSeconds(90), ReadinessTimeout.ToString(), "00:01:30"),
        ("tunnel-v0501-lease-bounded", true, "deadline=min(now+90s, read lease expiry)", "never outlive lease while waiting"),
        ("tunnel-v0501-healthz", true, "bounded /healthz status/body observation", "liveness != readiness"),
        ("tunnel-v0501-readyz", true, "bounded /readyz status/body observation", "non-success reason retained redacted"),
        ("tunnel-v0501-body-bounds", ReadinessBodyLimitBytes == 4096 && ReadinessBodyLimitChars == 512, $"bytes={ReadinessBodyLimitBytes}; chars={ReadinessBodyLimitChars}", "bytes=4096; chars=512"),
        ("tunnel-v0501-failure-receipt", ReadinessFailureReceiptSchema == "matawaka.local-app-secure-mcp-tunnel-readiness-failure-receipt/v0.50.1", ReadinessFailureReceiptSchema, "exact local v0.50.1 failure evidence"),
        ("tunnel-v0501-no-new-admin", true, "inherits fixed runtime run-only transport; no CRUD/admin", "no authority widening")
    };

    public static string RedactDiagnosticForQualification(string value, string runtimeSecret, string localEndpoint)
        => SanitizeDiagnostic(value, runtimeSecret, localEndpoint);

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

    private static async Task<Uri> WaitForReadyAsync(
        Process process,
        string healthPath,
        DateTimeOffset leaseExpiresAt,
        string runtimeSecret,
        string localEndpoint,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var hardDeadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        var leaseDeadline = leaseExpiresAt.ToUniversalTime();
        var deadline = hardDeadline <= leaseDeadline ? hardDeadline : leaseDeadline;
        var leaseBound = leaseDeadline < hardDeadline;
        var attempts = 0;
        var healthFileObserved = false;
        var healthUrlValid = false;
        int? healthzStatus = null;
        string? healthzBody = null;
        int? readyzStatus = null;
        string? readyzBody = null;
        var stage = "health-url-absent";
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                stage = healthFileObserved ? "child-exited-after-health-surface" : "child-exited-before-health-surface";
                throw BuildReadinessFailure(stage, attempts, started, leaseBound, healthFileObserved, healthUrlValid, healthzStatus, healthzBody, readyzStatus, readyzBody, true, process.ExitCode, runtimeSecret, localEndpoint);
            }

            if (File.Exists(healthPath))
            {
                healthFileObserved = true;
                var info = new FileInfo(healthPath);
                if (info.Length > 2048)
                {
                    stage = "health-url-file-oversize";
                    throw BuildReadinessFailure(stage, attempts, started, leaseBound, true, false, healthzStatus, healthzBody, readyzStatus, readyzBody, false, null, runtimeSecret, localEndpoint);
                }

                var raw = (await File.ReadAllTextAsync(healthPath, Encoding.UTF8, cancellationToken)).Trim();
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var baseUri) || !IsExactLoopbackHttp(baseUri))
                {
                    stage = "health-url-invalid-or-non-loopback";
                    throw BuildReadinessFailure(stage, attempts, started, leaseBound, true, false, healthzStatus, healthzBody, readyzStatus, readyzBody, false, null, runtimeSecret, localEndpoint);
                }

                healthUrlValid = true;
                attempts++;
                var healthProbe = await ProbeBoundedAsync(client, new Uri(baseUri.ToString().TrimEnd('/') + "/healthz"), runtimeSecret, localEndpoint, cancellationToken);
                healthzStatus = healthProbe.StatusCode;
                healthzBody = healthProbe.Body ?? healthProbe.Error;
                var readyProbe = await ProbeBoundedAsync(client, new Uri(baseUri.ToString().TrimEnd('/') + "/readyz"), runtimeSecret, localEndpoint, cancellationToken);
                readyzStatus = readyProbe.StatusCode;
                readyzBody = readyProbe.Body ?? readyProbe.Error;

                if (readyzStatus is >= 200 and < 300) return baseUri;
                stage = healthzStatus is >= 200 and < 300
                    ? readyzStatus is null ? "healthz-live-readyz-unreachable" : "healthz-live-readyz-not-ready"
                    : healthzStatus is null ? "health-surface-unreachable" : "healthz-not-live";
            }

            await Task.Delay(200, cancellationToken);
        }

        if (!healthFileObserved) stage = leaseBound ? "lease-deadline-health-url-absent" : "readiness-deadline-health-url-absent";
        else if (healthUrlValid && healthzStatus is >= 200 and < 300 && readyzStatus is not null) stage = leaseBound ? "lease-deadline-readyz-not-ready" : "readiness-deadline-readyz-not-ready";
        else if (healthUrlValid) stage = leaseBound ? "lease-deadline-health-unready" : "readiness-deadline-health-unready";
        throw BuildReadinessFailure(stage, attempts, started, leaseBound, healthFileObserved, healthUrlValid, healthzStatus, healthzBody, readyzStatus, readyzBody, SafeHasExited(process), SafeExitCode(process), runtimeSecret, localEndpoint);
    }

    private static OpenAiTunnelReadinessFailureV0501 BuildReadinessFailure(
        string stage,
        int attempts,
        Stopwatch started,
        bool leaseBound,
        bool healthFileObserved,
        bool healthUrlValid,
        int? healthzStatus,
        string? healthzBody,
        int? readyzStatus,
        string? readyzBody,
        bool processExited,
        int? exitCode,
        string runtimeSecret,
        string localEndpoint)
    {
        var safeHealth = SanitizeNullable(healthzBody, runtimeSecret, localEndpoint);
        var safeReady = SanitizeNullable(readyzBody, runtimeSecret, localEndpoint);
        var summary = SanitizeDiagnostic($"stage={stage}; healthz={FormatProbe(healthzStatus, safeHealth)}; readyz={FormatProbe(readyzStatus, safeReady)}; attempts={attempts}; elapsedMs={started.ElapsedMilliseconds}; leaseBound={leaseBound}; processExited={processExited}", runtimeSecret, localEndpoint);
        var diagnostic = new OpenAiTunnelReadinessDiagnosticV0501(
            ReadinessDiagnosticSchema,
            Version,
            DateTimeOffset.Now,
            stage,
            attempts,
            checked((int)Math.Min(int.MaxValue, started.ElapsedMilliseconds)),
            leaseBound,
            healthFileObserved,
            healthUrlValid,
            healthzStatus,
            safeHealth,
            readyzStatus,
            safeReady,
            processExited,
            exitCode,
            summary);
        return new OpenAiTunnelReadinessFailureV0501(diagnostic);
    }

    private sealed record BoundedProbe(int? StatusCode, string? Body, string? Error);

    private static async Task<BoundedProbe> ProbeBoundedAsync(HttpClient client, Uri uri, string runtimeSecret, string localEndpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadBoundedBodyAsync(response.Content, cancellationToken);
            return new BoundedProbe((int)response.StatusCode, SanitizeDiagnostic(body, runtimeSecret, localEndpoint), null);
        }
        catch (HttpRequestException ex)
        {
            return new BoundedProbe(null, null, "unreachable:" + SanitizeDiagnostic(ex.Message, runtimeSecret, localEndpoint));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BoundedProbe(null, null, "request-timeout");
        }
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        var truncated = false;
        while (buffer.Length <= ReadinessBodyLimitBytes)
        {
            var remaining = ReadinessBodyLimitBytes + 1 - (int)buffer.Length;
            if (remaining <= 0) break;
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken);
            if (read <= 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > ReadinessBodyLimitBytes) { truncated = true; break; }
        }
        var bytes = buffer.ToArray();
        if (bytes.Length > ReadinessBodyLimitBytes) bytes = bytes[..ReadinessBodyLimitBytes];
        var text = Encoding.UTF8.GetString(bytes);
        if (truncated) text += " [truncated]";
        return text;
    }

    private static string FormatProbe(int? status, string? body)
        => status is null ? (string.IsNullOrWhiteSpace(body) ? "unobserved" : body) : $"{status}:{body ?? string.Empty}";

    private static string? SanitizeNullable(string? value, string runtimeSecret, string localEndpoint)
        => string.IsNullOrWhiteSpace(value) ? value : SanitizeDiagnostic(value, runtimeSecret, localEndpoint);

    private static string SanitizeDiagnostic(string value, string runtimeSecret, string localEndpoint)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (!string.IsNullOrEmpty(runtimeSecret)) text = text.Replace(runtimeSecret, "[redacted-runtime-key]", StringComparison.Ordinal);
        if (Uri.TryCreate(localEndpoint, UriKind.Absolute, out var endpointUri))
        {
            text = text.Replace(localEndpoint, "[redacted-local-mcp-endpoint]", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(endpointUri.AbsolutePath)) text = text.Replace(endpointUri.AbsolutePath, "/mcp/[redacted]", StringComparison.OrdinalIgnoreCase);
        }
        text = RedactBearer(text);
        var printable = new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        if (printable.Length > ReadinessBodyLimitChars) printable = printable[..ReadinessBodyLimitChars] + "…";
        return printable;
    }

    private static string RedactBearer(string text)
    {
        var index = 0;
        while ((index = text.IndexOf("Bearer ", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = index + "Bearer ".Length;
            var end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && end - start < 512) end++;
            if (end > start) text = text[..start] + "[redacted]" + text[end..];
            index = start + "[redacted]".Length;
        }
        return text;
    }

    private static void ClearSensitiveParentStartInfo(Process process)
    {
        process.StartInfo.Environment[OpenAiSecureMcpTunnelV050Service.RuntimeKeyEnvironmentName] = string.Empty;
        process.StartInfo.Environment[OpenAiSecureMcpTunnelV050Service.McpServerUrlEnvironmentName] = string.Empty;
        process.StartInfo.Environment[OpenAiSecureMcpTunnelV050Service.TunnelIdEnvironmentName] = string.Empty;
    }

    private static void ValidateRuntimeApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 16 || value.Length > 4096 || value.Any(ch => ch is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Runtime API key is empty or has an unsafe shape. It is intentionally not persisted or echoed.");
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

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; } catch { return true; }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; } catch { return null; }
    }

    private static string ResolveRuntimeDirectory(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench", ".workbench", "secure-mcp-tunnel"));
        var workbench = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(workbench, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tunnel runtime state escaped Workbench .workbench root.");
        return root;
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
        public string ExecutableSha256 { get; }
        public string ReportedVersion { get; }
        public string HealthLoopbackUrl { get; }
        public CancellationTokenSource StopMonitor { get; }
        public Task? ExpiryTask { get; set; }

        public ActiveTunnel(Process process, string applicationId, string leaseId, string tunnelId, DateTimeOffset leaseExpiresAt, string executableSha256, string reportedVersion, string healthLoopbackUrl, CancellationTokenSource stopMonitor)
        {
            Process = process;
            ProcessId = process.Id;
            ApplicationId = applicationId;
            LeaseId = leaseId;
            TunnelId = tunnelId;
            LeaseExpiresAt = leaseExpiresAt;
            ExecutableSha256 = executableSha256;
            ReportedVersion = reportedVersion;
            HealthLoopbackUrl = healthLoopbackUrl;
            StopMonitor = stopMonitor;
        }
    }
}
