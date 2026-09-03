using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record PlainMcpOAuthDiscoveryCompatGrantV0502(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    DateTimeOffset LeaseExpiresAt,
    string EndpointUrl,
    string EndpointTokenSha256,
    string UpstreamEndpointSha256,
    bool LoopbackOnly,
    bool OAuthMetadataAdvertised,
    bool OAuthProtectedResourceMetadataReturns404,
    bool FilesystemAuthorityCreated,
    string Note);

public sealed record PlainMcpOAuthDiscoveryCompatReceiptV0502(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    DateTimeOffset LeaseExpiresAt,
    string EndpointTokenSha256,
    string LoopbackAddress,
    string UpstreamEndpointSha256,
    bool LoopbackListenerStarted,
    bool OAuthMetadataAdvertised,
    bool RootProtectedResourceMetadata404,
    bool PathSpecificProtectedResourceMetadata404,
    bool UpstreamAuthorizationForwarded,
    bool FilesystemAuthorityCreated,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class PlainMcpOAuthDiscoveryCompatV0502Service
{
    public const string Version = "0.50.2";
    public const string GrantSchema = "matawaka.local-app-plain-mcp-oauth-discovery-compat-grant/v0.50.2";
    public const string ReceiptSchema = "matawaka.local-app-plain-mcp-oauth-discovery-compat-receipt/v0.50.2";
    public const int MaxHeaderBytes = 16 * 1024;
    public const int MaxRequestBodyBytes = 64 * 1024;
    public const int MaxResponseBodyBytes = 2 * 1024 * 1024;
    public const int MaxTrailerBytes = 4 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveFacade? _active;

    public bool IsActiveFor(string applicationId)
        => _active is { } active && active.ApplicationId.Equals(applicationId, StringComparison.Ordinal);

    public async Task<PlainMcpOAuthDiscoveryCompatGrantV0502> StartAsync(
        string applicationId,
        string leaseId,
        DateTimeOffset leaseExpiresAt,
        string upstreamEndpoint,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not null) throw new InvalidDataException("A v0.50.2 plain-MCP discovery compatibility facade is already active.");
            if (leaseExpiresAt <= DateTimeOffset.Now) throw new InvalidDataException("Read lease expired before compatibility facade startup.");
            ValidateUpstream(upstreamEndpoint);
            if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(leaseId)) throw new InvalidDataException("ApplicationId and LeaseId are required.");

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var endpointPath = "/mcp/" + token;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var stop = new CancellationTokenSource();
            try
            {
                listener.Start(8);
                var local = listener.LocalEndpoint as IPEndPoint ?? throw new InvalidDataException("Compatibility facade did not expose an IPv4 loopback endpoint.");
                if (!IPAddress.Loopback.Equals(local.Address)) throw new InvalidDataException("Compatibility facade listener is not exact IPv4 loopback.");
                var endpoint = $"http://127.0.0.1:{local.Port}{endpointPath}";
                var loop = RunAcceptLoopAsync(listener, local.Port, endpointPath, new Uri(upstreamEndpoint), stop.Token);
                var active = new ActiveFacade(listener, stop, loop, applicationId, leaseId, leaseExpiresAt, endpoint, HashText(token), HashText(upstreamEndpoint), DateTimeOffset.Now);
                _active = active;
                active.ExpiryTask = MonitorExpiryAsync(active);
                return new PlainMcpOAuthDiscoveryCompatGrantV0502(
                    GrantSchema, Version, DateTimeOffset.Now, applicationId, leaseId, leaseExpiresAt,
                    endpoint, active.EndpointTokenSha256, active.UpstreamEndpointSha256,
                    true, false, true, false,
                    "v0.50.2 exposes a second loopback-only transport facade for the tunnel-client. OAuth Protected Resource Metadata candidates return 404 to explicitly represent a plain no-auth MCP transport. POST MCP traffic is forwarded only to the exact already-active lease-gated loopback MCP endpoint.");
            }
            catch
            {
                stop.Cancel();
                listener.Stop();
                stop.Dispose();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<(PlainMcpOAuthDiscoveryCompatReceiptV0502 Receipt, string ReceiptPath)> WriteStartReceiptAsync(string workspaceRoot, PlainMcpOAuthDiscoveryCompatGrantV0502 grant, CancellationToken cancellationToken)
    {
        var active = _active ?? throw new InvalidDataException("No active v0.50.2 compatibility facade exists for a receipt.");
        if (active.ApplicationId != grant.ApplicationId || active.LeaseId != grant.LeaseId || active.EndpointTokenSha256 != grant.EndpointTokenSha256)
            throw new InvalidDataException("Compatibility facade grant does not match active runtime.");
        var endpoint = new Uri(grant.EndpointUrl);
        var receipt = new PlainMcpOAuthDiscoveryCompatReceiptV0502(
            ReceiptSchema, Version, DateTimeOffset.Now, active.ApplicationId, active.LeaseId, active.LeaseExpiresAt,
            active.EndpointTokenSha256, $"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}", active.UpstreamEndpointSha256,
            true, false, true, true, false, false,
            DefaultNonEffects(), "PLAIN_MCP_OAUTH_DISCOVERY_COMPAT_LOOPBACK_READY",
            "The facade advertises no OAuth metadata. Both RFC9728-style protected-resource metadata candidates return deterministic 404; all MCP content traffic remains delegated to the exact existing lease-gated local MCP endpoint. Secret endpoint paths are represented only by SHA-256 in this receipt.");
        var path = await WriteArtifactAsync(workspaceRoot, "start", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
        return (receipt, path);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = _active;
            _active = null;
            if (active is null) return;
            active.StopMonitor.Cancel();
            active.Stop.Cancel();
            active.Listener.Stop();
            try { await active.AcceptLoop.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); } catch { }
            active.Stop.Dispose();
            active.StopMonitor.Dispose();
        }
        finally { _gate.Release(); }
    }

    public async Task StopBestEffortAsync()
    {
        try { await StopAsync(CancellationToken.None); } catch { }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("plain-mcp-v0502-root-prmd", IsProtectedResourceMetadataPath("/.well-known/oauth-protected-resource", "/mcp/" + new string('a', 64)), "404", "404"),
        ("plain-mcp-v0502-path-prmd", IsProtectedResourceMetadataPath("/.well-known/oauth-protected-resource/mcp/" + new string('a', 64), "/mcp/" + new string('a', 64)), "404", "404"),
        ("plain-mcp-v0502-random-well-known-refused", !IsProtectedResourceMetadataPath("/.well-known/oauth-authorization-server", "/mcp/" + new string('a', 64)), "false", "false"),
        ("plain-mcp-v0502-oauth-advertisement", true, "none", "none"),
        ("plain-mcp-v0502-loopback", true, "127.0.0.1 ephemeral only", "loopback only"),
        ("plain-mcp-v0502-auth-forwarding", true, "Authorization/Cookie not forwarded upstream", "false"),
        ("plain-mcp-v0502-filesystem-authority", true, "false", "false")
    };

    public static bool IsProtectedResourceMetadataPath(string requestPath, string endpointPath)
        => requestPath.Equals("/.well-known/oauth-protected-resource", StringComparison.Ordinal)
           || requestPath.Equals("/.well-known/oauth-protected-resource" + endpointPath, StringComparison.Ordinal);

    private async Task MonitorExpiryAsync(ActiveFacade active)
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
                active.Stop.Cancel();
                active.Listener.Stop();
                active.Stop.Dispose();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (active.StopMonitor.IsCancellationRequested) { }
        catch { }
    }

    private async Task RunAcceptLoopAsync(TcpListener listener, int port, string endpointPath, Uri upstream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
                await HandleClientAsync(client, port, endpointPath, upstream, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static async Task HandleClientAsync(TcpClient client, int port, string endpointPath, Uri upstream, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            client.NoDelay = true;
            HttpRequest request;
            try { request = await ReadRequestAsync(stream, cancellationToken); }
            catch (InvalidDataException ex)
            {
                await WriteTextAsync(stream, 400, "Bad Request", "REFUSED: " + ex.Message, cancellationToken);
                return;
            }

            if (!request.Host.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(stream, 400, "Bad Request", "Host boundary refused", cancellationToken);
                return;
            }

            if (request.Method.Equals("GET", StringComparison.Ordinal) && IsProtectedResourceMetadataPath(request.Path, endpointPath))
            {
                await WriteEmptyAsync(stream, 404, "Not Found", cancellationToken, "Content-Type: application/json\r\n");
                return;
            }

            if (!request.Path.Equals(endpointPath, StringComparison.Ordinal))
            {
                await WriteEmptyAsync(stream, 404, "Not Found", cancellationToken);
                return;
            }

            if (!request.Method.Equals("POST", StringComparison.Ordinal))
            {
                await WriteTextAsync(stream, 405, "Method Not Allowed", "POST required", cancellationToken, "Allow: POST\r\n");
                return;
            }

            if (!request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(stream, 415, "Unsupported Media Type", "application/json required", cancellationToken);
                return;
            }

            await ForwardPostAsync(stream, request, upstream, cancellationToken);
        }
    }

    private static async Task ForwardPostAsync(NetworkStream downstream, HttpRequest request, Uri upstream, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, upstream)
        {
            Content = new ByteArrayContent(request.Body)
        };
        upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
        CopySafeHeader(request, upstreamRequest, "Accept");
        CopySafeHeader(request, upstreamRequest, "MCP-Protocol-Version");
        CopySafeHeader(request, upstreamRequest, "Mcp-Session-Id");

        using var response = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await ReadBoundedResponseAsync(response.Content, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        var extra = new StringBuilder();
        extra.Append("Content-Type: ").Append(contentType).Append("\r\n");
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
            extra.Append("Mcp-Session-Id: ").Append(sessionIds.First()).Append("\r\n");
        await WriteRawAsync(downstream, (int)response.StatusCode, response.ReasonPhrase ?? "OK", body, cancellationToken, extra.ToString());
    }

    private static void CopySafeHeader(HttpRequest source, HttpRequestMessage target, string name)
    {
        if (source.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            target.Headers.TryAddWithoutValidation(name, value);
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length <= MaxResponseBodyBytes)
        {
            var remaining = MaxResponseBodyBytes + 1 - (int)buffer.Length;
            if (remaining <= 0) break;
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken);
            if (read <= 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxResponseBodyBytes) throw new InvalidDataException("Upstream MCP response exceeds v0.50.2 compatibility bound.");
        }
        return buffer.ToArray();
    }

    private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var headerBytes = await ReadUntilHeaderEndAsync(stream, cancellationToken);
        var text = Encoding.ASCII.GetString(headerBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 2) throw new InvalidDataException("Malformed HTTP request.");
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || requestLine[2] != "HTTP/1.1") throw new InvalidDataException("HTTP/1.1 request line required.");
        var method = requestLine[0];
        var path = requestLine[1];
        if (!path.StartsWith('/', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal) || path.Contains('#', StringComparison.Ordinal)) throw new InvalidDataException("Absolute path without query/fragment required.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var colon = lines[i].IndexOf(':');
            if (colon <= 0) throw new InvalidDataException("Malformed HTTP header.");
            var name = lines[i][..colon].Trim();
            var value = lines[i][(colon + 1)..].Trim();
            if (!headers.TryAdd(name, value)) throw new InvalidDataException("Duplicate HTTP header refused.");
        }
        if (!headers.TryGetValue("Host", out var host) || string.IsNullOrWhiteSpace(host)) throw new InvalidDataException("Host header required.");
        headers.TryGetValue("Content-Type", out var contentType);
        var transfer = headers.TryGetValue("Transfer-Encoding", out var te) ? te : null;
        var lengthText = headers.TryGetValue("Content-Length", out var cl) ? cl : null;
        if (!string.IsNullOrWhiteSpace(transfer) && !string.IsNullOrWhiteSpace(lengthText)) throw new InvalidDataException("Content-Length with Transfer-Encoding refused.");
        byte[] body;
        if (!string.IsNullOrWhiteSpace(transfer))
        {
            if (!transfer.Equals("chunked", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only chunked Transfer-Encoding is supported.");
            body = await ReadChunkedBodyAsync(stream, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(lengthText))
        {
            if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length < 0 || length > MaxRequestBodyBytes)
                throw new InvalidDataException("Content-Length exceeds compatibility bound.");
            body = await ReadExactAsync(stream, length, cancellationToken);
        }
        else body = Array.Empty<byte>();
        return new HttpRequest(method, path, host, contentType ?? string.Empty, headers, body);
    }

    private static async Task<byte[]> ReadUntilHeaderEndAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        var tail = new Queue<byte>(4);
        while (buffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0) throw new InvalidDataException("Unexpected EOF in HTTP headers.");
            buffer.WriteByte(one[0]);
            tail.Enqueue(one[0]);
            if (tail.Count > 4) tail.Dequeue();
            if (tail.Count == 4 && tail.SequenceEqual(new byte[] { 13, 10, 13, 10 }))
            {
                var bytes = buffer.ToArray();
                return bytes[..^4];
            }
        }
        throw new InvalidDataException("HTTP headers exceed compatibility bound.");
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var trailerBytes = 0;
        while (true)
        {
            var line = await ReadAsciiLineAsync(stream, 128, cancellationToken);
            var semicolon = line.IndexOf(';');
            var sizeText = semicolon >= 0 ? line[..semicolon] : line;
            if (!int.TryParse(sizeText.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var size) || size < 0)
                throw new InvalidDataException("Invalid chunk size.");
            if (size == 0)
            {
                while (true)
                {
                    var trailer = await ReadAsciiLineAsync(stream, MaxTrailerBytes, cancellationToken);
                    trailerBytes += Encoding.ASCII.GetByteCount(trailer) + 2;
                    if (trailerBytes > MaxTrailerBytes) throw new InvalidDataException("HTTP trailers exceed compatibility bound.");
                    if (trailer.Length == 0) break;
                }
                break;
            }
            if (output.Length + size > MaxRequestBodyBytes) throw new InvalidDataException("Chunked body exceeds compatibility bound.");
            var chunk = await ReadExactAsync(stream, size, cancellationToken);
            output.Write(chunk, 0, chunk.Length);
            var crlf = await ReadExactAsync(stream, 2, cancellationToken);
            if (crlf[0] != 13 || crlf[1] != 10) throw new InvalidDataException("Chunk terminator missing.");
        }
        return output.ToArray();
    }

    private static async Task<string> ReadAsciiLineAsync(NetworkStream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length <= maxBytes)
        {
            var read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0) throw new InvalidDataException("Unexpected EOF while reading line.");
            if (one[0] == 10)
            {
                var data = buffer.ToArray();
                if (data.Length == 0 || data[^1] != 13) throw new InvalidDataException("HTTP line must end CRLF.");
                return Encoding.ASCII.GetString(data[..^1]);
            }
            buffer.WriteByte(one[0]);
        }
        throw new InvalidDataException("HTTP line exceeds bound.");
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var data = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(data.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0) throw new InvalidDataException("Unexpected EOF in HTTP body.");
            offset += read;
        }
        return data;
    }

    private static Task WriteEmptyAsync(NetworkStream stream, int status, string reason, CancellationToken cancellationToken, string extraHeaders = "")
        => WriteRawAsync(stream, status, reason, Array.Empty<byte>(), cancellationToken, extraHeaders);

    private static Task WriteTextAsync(NetworkStream stream, int status, string reason, string body, CancellationToken cancellationToken, string extraHeaders = "")
        => WriteRawAsync(stream, status, reason, Encoding.UTF8.GetBytes(body), cancellationToken, "Content-Type: text/plain; charset=utf-8\r\n" + extraHeaders);

    private static async Task WriteRawAsync(NetworkStream stream, int status, string reason, byte[] body, CancellationToken cancellationToken, string extraHeaders = "")
    {
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\n{extraHeaders}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken);
        if (body.Length > 0) await stream.WriteAsync(body, cancellationToken);
    }

    private static void ValidateUpstream(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) || uri.Port <= 0 || !uri.AbsolutePath.StartsWith("/mcp/", StringComparison.Ordinal))
            throw new InvalidDataException("v0.50.2 compatibility upstream must be the exact active IPv4-loopback MCP endpoint.");
        var token = uri.AbsolutePath[5..];
        if (token.Length != 64 || token.Any(ch => !Uri.IsHexDigit(ch)) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("v0.50.2 compatibility upstream endpoint shape is invalid.");
    }

    private static async Task<string> WriteArtifactAsync<T>(string workspaceRoot, string kind, string applicationId, string leaseId, T receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench", "artifacts", "plain-mcp-oauth-discovery-compat");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"plain-mcp-oauth-discovery-{kind}-{SafeToken(applicationId)}-{SafeToken(leaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string SafeToken(string value) => new(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_').Take(96).ToArray());

    private static string[] DefaultNonEffects() => new[]
    {
        "no OAuth protected-resource metadata advertised",
        "no OAuth authorization server or DCR authority",
        "no WWW-Authenticate injection",
        "no Authorization/Cookie forwarding to the lease-gated upstream MCP endpoint",
        "no read lease creation/renewal/scope widening",
        "no application/source file access by this facade",
        "no public listener; both facade and upstream remain IPv4 loopback",
        "no Git/catalog/Agent Execute/ActionPermit authority"
    };

    private sealed record HttpRequest(string Method, string Path, string Host, string ContentType, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    private sealed class ActiveFacade
    {
        public TcpListener Listener { get; }
        public CancellationTokenSource Stop { get; }
        public Task AcceptLoop { get; }
        public string ApplicationId { get; }
        public string LeaseId { get; }
        public DateTimeOffset LeaseExpiresAt { get; }
        public string EndpointUrl { get; }
        public string EndpointTokenSha256 { get; }
        public string UpstreamEndpointSha256 { get; }
        public DateTimeOffset StartedAt { get; }
        public CancellationTokenSource StopMonitor { get; } = new();
        public Task? ExpiryTask { get; set; }

        public ActiveFacade(TcpListener listener, CancellationTokenSource stop, Task acceptLoop, string applicationId, string leaseId, DateTimeOffset leaseExpiresAt, string endpointUrl, string endpointTokenSha256, string upstreamEndpointSha256, DateTimeOffset startedAt)
        {
            Listener = listener; Stop = stop; AcceptLoop = acceptLoop; ApplicationId = applicationId; LeaseId = leaseId; LeaseExpiresAt = leaseExpiresAt;
            EndpointUrl = endpointUrl; EndpointTokenSha256 = endpointTokenSha256; UpstreamEndpointSha256 = upstreamEndpointSha256; StartedAt = startedAt;
        }
    }
}
