using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpAdapterPreviewV049(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    DateTimeOffset ExpiresAt,
    int RemainingCalls,
    long RemainingBytes,
    int MaxBytesPerRead,
    string BearerSha256,
    bool BearerVerified,
    bool ContainsFileContents,
    bool ReadyForExplicitLoopbackAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppMcpAdapterGrantV049(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string EndpointUrl,
    string EndpointTokenSha256,
    DateTimeOffset LeaseExpiresAt,
    IReadOnlyList<string> Tools,
    bool LoopbackOnly,
    bool PublicNetworkExposurePerformed,
    bool SecureMcpTunnelStarted,
    string Note);

public sealed record LocalAppMcpAdapterStartReceiptV049(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string EndpointTokenSha256,
    string LoopbackAddress,
    DateTimeOffset LeaseExpiresAt,
    IReadOnlyList<string> Tools,
    bool EndpointClipboardWritePerformed,
    bool BearerPlaintextPersisted,
    bool LoopbackListenerStarted,
    bool PublicNetworkExposurePerformed,
    bool OutboundNetworkAccessPerformed,
    bool SecureMcpTunnelStarted,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppMcpAdapterStopReceiptV049(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    string EndpointTokenSha256,
    bool ListenerStopped,
    bool InMemoryBearerReferenceCleared,
    bool PublicNetworkExposurePerformed,
    bool OutboundNetworkAccessPerformed,
    bool SecureMcpTunnelStopped,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppMcpReadResponseV049(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string Role,
    string RelativePath,
    long FileBytes,
    string FileSha256,
    long Offset,
    int ReturnedBytes,
    bool EndOfFile,
    string ContentBase64,
    string? Utf8Text,
    int RemainingCalls,
    long RemainingBytes,
    DateTimeOffset LeaseExpiresAt,
    string Note);

public sealed record LocalAppMcpListResponseV051(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string Role,
    string RelativeDirectory,
    int TotalEntries,
    int StartIndex,
    int ReturnedEntries,
    int? NextStartIndex,
    int DisclosureBytes,
    IReadOnlyList<LocalAppLeaseListEntryV051> Entries,
    int RemainingCalls,
    long RemainingBytes,
    DateTimeOffset LeaseExpiresAt,
    string Note);

internal sealed class LocalAppMcpAdapterSessionV049
{
    public string WorkspaceRoot { get; }
    public string ApplicationId { get; }
    public string LeaseId { get; }
    public string Bearer { get; private set; }

    public LocalAppMcpAdapterSessionV049(string workspaceRoot, string applicationId, string leaseId, string bearer)
    {
        WorkspaceRoot = workspaceRoot;
        ApplicationId = applicationId;
        LeaseId = leaseId;
        Bearer = bearer;
    }

    public void ClearBearerReference() => Bearer = string.Empty;
}

public sealed class LocalAppMcpReadAdapterV049Service
{
    public const string Version = "0.49.0";
    public const string ToolSurfaceVersionV051 = "0.51.0";
    public const string PreviewSchema = "matawaka.local-app-mcp-read-adapter-preview/v0.49";
    public const string GrantSchema = "matawaka.local-app-mcp-read-adapter-grant/v0.49";
    public const string StartReceiptSchema = "matawaka.local-app-mcp-read-adapter-start-receipt/v0.49";
    public const string StopReceiptSchema = "matawaka.local-app-mcp-read-adapter-stop-receipt/v0.49";
    public const string ReadResponseSchema = "matawaka.local-app-mcp-read-response/v0.49";
    public const string ListResponseSchemaV051 = "matawaka.local-app-mcp-list-response/v0.51";
    public const string RuntimeProtocolImplementation = "allowlisted-mcp-jsonrpc-streamable-http-subset-base-dotnet-tcp";
    public const string QualificationClientPackage = "ModelContextProtocol";
    public const string QualificationClientVersion = "2.2.0";
    public const int MaxProtocolRequestBytes = 64 * 1024;
    public const int MaxHttpHeaderBytes = 16 * 1024;
    public const int MaxHttpTrailerBytes = 4 * 1024;
    private const string LegacyProtocolVersion = "2025-11-25";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LocalAppReadLeaseV048Service _leases = new();
    private ActiveAdapter? _active;

    public LocalAppMcpAdapterPreviewV049 PreviewFromGrantJson(string workspaceRoot, string selectedApplicationId, string grantJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(grantJson)) throw new InvalidDataException("v0.48 read lease grant JSON is empty.");
        LocalAppReadLeaseGrantV048 grant;
        try
        {
            grant = JsonSerializer.Deserialize<LocalAppReadLeaseGrantV048>(grantJson, JsonOptions)
                ?? throw new InvalidDataException("v0.48 read lease grant JSON could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("v0.48 read lease grant JSON is invalid.", ex);
        }
        return Preview(workspaceRoot, selectedApplicationId, grant, cancellationToken);
    }

    public LocalAppMcpAdapterPreviewV049 Preview(string workspaceRoot, string selectedApplicationId, LocalAppReadLeaseGrantV048 grant, CancellationToken cancellationToken)
    {
        if (grant.Schema != LocalAppReadLeaseV048Service.GrantSchema || grant.Version != LocalAppReadLeaseV048Service.Version)
            throw new InvalidDataException("Exact v0.48 read lease grant is required.");
        if (!string.Equals(grant.ApplicationId, selectedApplicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Grant ApplicationId does not match the explicitly selected registered app.");
        if (!SafeLeaseId(grant.LeaseId)) throw new InvalidDataException("Unsafe LeaseId in grant.");
        if (grant.Bearer.Length != 64 || grant.Bearer.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException("Grant bearer must be exactly 64 hex characters.");
        cancellationToken.ThrowIfCancellationRequested();

        var state = _leases.ListActive(workspaceRoot, selectedApplicationId)
            .SingleOrDefault(x => x.LeaseId.Equals(grant.LeaseId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("The supplied grant does not reference one currently active read lease for the selected app.");
        var bearerSha = HashText(grant.Bearer);
        var observed = Convert.FromHexString(bearerSha);
        var expected = Convert.FromHexString(state.BearerSha256);
        if (!CryptographicOperations.FixedTimeEquals(observed, expected))
            throw new InvalidDataException("Read lease grant bearer does not match current hash-only lease state.");
        if (state.ExpiresAt <= DateTimeOffset.Now || state.Revoked || state.RemainingCalls <= 0 || state.RemainingBytes <= 0)
            throw new InvalidDataException("Read lease is not active for MCP adapter startup.");

        return new LocalAppMcpAdapterPreviewV049(
            PreviewSchema, Version, DateTimeOffset.Now, selectedApplicationId, state.LeaseId, state.Scopes,
            state.ExpiresAt, state.RemainingCalls, state.RemainingBytes, state.MaxBytesPerRead,
            state.BearerSha256, true, false, true, DefaultNonEffects(),
            "Preview validates the selected app, active v0.48 lease and bearer hash only. It creates no listener, tunnel, file read or directory listing. Explicit loopback-listener confirmation is still required.");
    }

    public async Task<LocalAppMcpAdapterGrantV049> StartAsync(string workspaceRoot, string selectedApplicationId, LocalAppMcpAdapterPreviewV049 confirmedPreview, string grantJson, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not null) throw new InvalidDataException("A v0.49 read-only MCP adapter is already active in this Workbench process. Stop it before starting another.");
            var fresh = PreviewFromGrantJson(workspaceRoot, selectedApplicationId, grantJson, cancellationToken);
            RequireSamePreview(confirmedPreview, fresh);
            var grant = JsonSerializer.Deserialize<LocalAppReadLeaseGrantV048>(grantJson, JsonOptions)
                ?? throw new InvalidDataException("Grant disappeared before adapter start.");

            var endpointToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var endpointTokenSha = HashText(endpointToken);
            var endpointPath = "/mcp/" + endpointToken;
            var session = new LocalAppMcpAdapterSessionV049(Path.GetFullPath(workspaceRoot.Trim()), selectedApplicationId, fresh.LeaseId, grant.Bearer);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var stop = new CancellationTokenSource();

            try
            {
                listener.Start(8);
                var local = listener.LocalEndpoint as IPEndPoint
                    ?? throw new InvalidDataException("MCP adapter did not expose an IPv4 loopback endpoint.");
                if (!IPAddress.Loopback.Equals(local.Address)) throw new InvalidDataException("MCP adapter listener is not exact IPv4 loopback.");
                var endpoint = $"http://127.0.0.1:{local.Port}{endpointPath}";
                var acceptLoop = RunAcceptLoopAsync(listener, endpointPath, local.Port, session, stop.Token);
                _active = new ActiveAdapter(listener, stop, acceptLoop, session, selectedApplicationId, fresh.LeaseId, endpoint, endpointTokenSha, fresh.ExpiresAt, DateTimeOffset.Now);
                return new LocalAppMcpAdapterGrantV049(
                    GrantSchema, Version, DateTimeOffset.Now, selectedApplicationId, fresh.LeaseId, endpoint,
                    endpointTokenSha, fresh.ExpiresAt, new[] { "read_local_app_chunk", "list_local_app_entries" }, true, false, false,
                    "This is a local loopback MCP Streamable HTTP endpoint implemented over base .NET TcpListener only. v0.51 extends the tool surface with lease-gated non-recursive directory metadata listing; no new root, lease or tunnel authority is created.");
            }
            catch
            {
                session.ClearBearerReference();
                stop.Cancel();
                listener.Stop();
                stop.Dispose();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<(LocalAppMcpAdapterStartReceiptV049 Receipt, string ReceiptPath)> WriteStartReceiptAsync(string workspaceRoot, LocalAppMcpAdapterGrantV049 grant, bool endpointClipboardWritePerformed, CancellationToken cancellationToken)
    {
        var active = _active ?? throw new InvalidDataException("No active v0.49 MCP adapter exists for a start receipt.");
        if (active.ApplicationId != grant.ApplicationId || active.LeaseId != grant.LeaseId || active.EndpointTokenSha256 != grant.EndpointTokenSha256)
            throw new InvalidDataException("MCP adapter grant does not match active runtime.");
        var uri = new Uri(grant.EndpointUrl);
        var receipt = new LocalAppMcpAdapterStartReceiptV049(
            StartReceiptSchema, Version, DateTimeOffset.Now, grant.ApplicationId, grant.LeaseId,
            grant.EndpointTokenSha256, $"{uri.Scheme}://{uri.Host}:{uri.Port}", grant.LeaseExpiresAt,
            grant.Tools, endpointClipboardWritePerformed, false, true, false, false, false,
            DefaultNonEffects(), "MCP_READ_ADAPTER_LOOPBACK_READY_NO_TUNNEL",
            "The adapter is listening only on IPv4 loopback and delegates every file read and directory listing to the active v0.48 lease state. Endpoint token plaintext and bearer plaintext are not persisted in this receipt.");
        var path = await WriteArtifactAsync(workspaceRoot, "start", grant.ApplicationId, grant.LeaseId, receipt, cancellationToken);
        return (receipt, path);
    }

    public async Task<(LocalAppMcpAdapterStopReceiptV049 Receipt, string ReceiptPath)> StopAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = _active ?? throw new InvalidDataException("No active v0.49 MCP adapter exists in this Workbench process.");
            _active = null;
            var stopped = await StopRuntimeAsync(active, cancellationToken);
            var receipt = new LocalAppMcpAdapterStopReceiptV049(
                StopReceiptSchema, Version, DateTimeOffset.Now, active.ApplicationId, active.LeaseId,
                active.EndpointTokenSha256, stopped, true, false, false, false, DefaultNonEffects(),
                "MCP_READ_ADAPTER_STOPPED_LOCAL_ONLY",
                "The local loopback listener stopped and the Workbench-held plaintext bearer reference was cleared. This is reference clearing, not a claim of managed-memory zeroization.");
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
            try { await StopRuntimeAsync(active, CancellationToken.None); } catch { }
        }
        finally { _gate.Release(); }
    }

    public bool IsActiveFor(string applicationId)
        => _active is { } active && active.ApplicationId.Equals(applicationId, StringComparison.Ordinal);

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-v049-runtime-protocol", RuntimeProtocolImplementation == "allowlisted-mcp-jsonrpc-streamable-http-subset-base-dotnet-tcp", RuntimeProtocolImplementation, "base .NET allowlisted subset"),
        ("mcp-v049-official-qualification-client", QualificationClientPackage == "ModelContextProtocol" && QualificationClientVersion == "2.2.0", $"{QualificationClientPackage} {QualificationClientVersion}", "official client 2.2.0"),
        ("mcp-v049-product-nuget", true, "no MCP/AspNetCore product runtime package dependency", "offline-update compatible"),
        ("mcp-v049-read-tool-preserved", true, "read_local_app_chunk", "read_local_app_chunk"),
        ("mcp-v049-bound-authority", true, "ApplicationId/LeaseId/bearer fixed in runtime session", "not MCP arguments"),
        ("mcp-v049-loopback", true, "TcpListener(IPAddress.Loopback, 0) + random path token", "127.0.0.1 only"),
        ("mcp-v049-http-bounds", MaxHttpHeaderBytes == 16384 && MaxProtocolRequestBytes == 65536 && MaxHttpTrailerBytes == 4096, $"header={MaxHttpHeaderBytes}; body={MaxProtocolRequestBytes}; trailers={MaxHttpTrailerBytes}", "bounded"),
        ("mcp-v049-chunked", true, "bounded decoder required for official Streamable HTTP client", "admitted within same body ceiling"),
        ("mcp-v049-public-exposure", true, "false", "false"),
        ("mcp-v049-tunnel", true, "not started by Workbench", "separate authority")
    };

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunV051BrowseContractChecks() => new[]
    {
        ("mcp-v051-tool-surface", true, "read_local_app_chunk + list_local_app_entries", "exactly two read-only tools"),
        ("mcp-v051-list-caller-authority", true, "role/directory/index/count only", "no app/lease/bearer/root"),
        ("mcp-v051-list-recursion", true, "not exposed", "no recursion/glob"),
        ("mcp-v051-list-open-world", true, "false", "false"),
        ("mcp-v051-tool-surface-version", ToolSurfaceVersionV051 == "0.51.0", ToolSurfaceVersionV051, "0.51.0")
    };

    private async Task RunAcceptLoopAsync(TcpListener listener, string endpointPath, int port, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
                await HandleClientAsync(client, endpointPath, port, session, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(TcpClient client, string endpointPath, int port, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            HttpRequest request;
            try { request = await ReadHttpRequestAsync(stream, cancellationToken); }
            catch (InvalidDataException ex)
            {
                await WriteHttpTextAsync(stream, 400, "Bad Request", "REFUSED: " + ex.Message, cancellationToken);
                return;
            }

            if (!request.Method.Equals("POST", StringComparison.Ordinal))
            {
                await WriteHttpTextAsync(stream, 405, "Method Not Allowed", "POST required", cancellationToken, "Allow: POST\r\n");
                return;
            }
            if (!request.Path.Equals(endpointPath, StringComparison.Ordinal))
            {
                await WriteHttpTextAsync(stream, 404, "Not Found", "Not Found", cancellationToken);
                return;
            }
            if (!request.Host.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) && !request.Host.Equals($"localhost:{port}", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpTextAsync(stream, 400, "Bad Request", "Host boundary refused", cancellationToken);
                return;
            }
            if (!request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpTextAsync(stream, 415, "Unsupported Media Type", "application/json required", cancellationToken);
                return;
            }

            var result = await ProcessMcpBodyAsync(request.Body, session, cancellationToken);
            if (result.IsNotification)
            {
                await WriteHttpEmptyAsync(stream, 202, "Accepted", cancellationToken);
                return;
            }
            await WriteHttpJsonAsync(stream, result.Envelope!, cancellationToken);
        }
    }

    private async Task<McpProcessResult> ProcessMcpBodyAsync(byte[] body, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 }); }
        catch (JsonException) { return new McpProcessResult(false, ErrorEnvelope(null, -32700, "Parse error")); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0" || !root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
                return new McpProcessResult(false, ErrorEnvelope(TryCloneId(root), -32600, "Invalid Request"));
            var id = TryCloneId(root);
            if (id is null) return new McpProcessResult(true, null);
            var method = methodElement.GetString()!;
            return method switch
            {
                "initialize" => new McpProcessResult(false, InitializeEnvelope(id, root)),
                "ping" => new McpProcessResult(false, ResultEnvelope(id, new Dictionary<string, object?>())),
                "tools/list" => new McpProcessResult(false, ResultEnvelope(id, BuildToolsListResult())),
                "tools/call" => new McpProcessResult(false, await ToolCallEnvelopeAsync(id, root, session, cancellationToken)),
                _ => new McpProcessResult(false, ErrorEnvelope(id, -32601, "Method not found"))
            };
        }
    }

    private static object InitializeEnvelope(JsonElement? id, JsonElement root)
    {
        var requested = LegacyProtocolVersion;
        if (root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out var protocol) && protocol.ValueKind == JsonValueKind.String)
            requested = protocol.GetString() ?? LegacyProtocolVersion;
        if (requested is not ("2025-11-25" or "2025-06-18" or "2024-11-05")) return ErrorEnvelope(id, -32602, "Unsupported initialize protocol version");
        return ResultEnvelope(id, new Dictionary<string, object?>
        {
            ["protocolVersion"] = requested,
            ["capabilities"] = new Dictionary<string, object?> { ["tools"] = new Dictionary<string, object?> { ["listChanged"] = false } },
            ["serverInfo"] = new Dictionary<string, object?> { ["name"] = "Matawaka Workbench Lease-Gated Read/Browse Adapter", ["version"] = ToolSurfaceVersionV051 }
        });
    }

    private async Task<object> ToolCallEnvelopeAsync(JsonElement? id, JsonElement root, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return ErrorEnvelope(id, -32602, "Invalid tool call");
        var name = nameElement.GetString();
        var arguments = parameters.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object ? args : default;
        if (arguments.ValueKind != JsonValueKind.Object) return ToolResultEnvelope(id, true, "REFUSED: arguments object is required");

        return name switch
        {
            "read_local_app_chunk" => await ReadToolEnvelopeAsync(id, arguments, session, cancellationToken),
            "list_local_app_entries" => await ListToolEnvelopeAsync(id, arguments, session, cancellationToken),
            _ => ErrorEnvelope(id, -32602, "Invalid tool call")
        };
    }

    private async Task<object> ReadToolEnvelopeAsync(JsonElement? id, JsonElement arguments, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "role", "relativePath", "offset", "maxBytes", "expectedFileSha256" };
        foreach (var property in arguments.EnumerateObject()) if (!allowed.Contains(property.Name)) return ToolResultEnvelope(id, true, $"REFUSED: unknown tool argument {property.Name}");

        try
        {
            var role = RequireString(arguments, "role");
            var relativePath = RequireString(arguments, "relativePath");
            var offset = RequireInt64(arguments, "offset");
            var maxBytes = RequireInt32(arguments, "maxBytes");
            string? expectedSha = null;
            if (arguments.TryGetProperty("expectedFileSha256", out var expectedElement) && expectedElement.ValueKind != JsonValueKind.Null)
            {
                if (expectedElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("expectedFileSha256 must be string or null.");
                expectedSha = expectedElement.GetString();
            }

            var request = new LocalAppLeaseReadRequestV048(LocalAppReadLeaseV048Service.ReadRequestSchema, "mcp-read-" + Guid.NewGuid().ToString("N"), session.LeaseId, session.Bearer, session.ApplicationId, role, relativePath, offset, maxBytes, expectedSha);
            var result = await _leases.AuthorizeAndReadAsync(session.WorkspaceRoot, request, cancellationToken);
            var response = new LocalAppMcpReadResponseV049(
                ReadResponseSchema, Version, DateTimeOffset.Now, result.Response.ApplicationId, result.Response.Role,
                result.Response.RelativePath, result.Response.FileBytes, result.Response.FileSha256, result.Response.Offset,
                result.Response.ReturnedBytes, result.Response.EndOfFile, result.Response.ContentBase64, result.Response.Utf8Text,
                result.Response.RemainingCalls, result.Response.RemainingBytes, result.Response.ExpiresAt,
                "Result came only through the accepted v0.48 lease gate. No mutation or process execution authority is present in this MCP tool.");
            return ToolResultEnvelope(id, false, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (InvalidDataException ex)
        {
            var safe = ex.Message.Replace(session.WorkspaceRoot, "<workspace>", StringComparison.OrdinalIgnoreCase);
            return ToolResultEnvelope(id, true, "REFUSED_BY_ACTIVE_READ_LEASE: " + safe);
        }
        catch (Exception) { return ToolResultEnvelope(id, true, "MCP_ADAPTER_INTERNAL_ERROR"); }
    }

    private async Task<object> ListToolEnvelopeAsync(JsonElement? id, JsonElement arguments, LocalAppMcpAdapterSessionV049 session, CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "role", "relativeDirectory", "startIndex", "maxEntries" };
        foreach (var property in arguments.EnumerateObject()) if (!allowed.Contains(property.Name)) return ToolResultEnvelope(id, true, $"REFUSED: unknown tool argument {property.Name}");

        try
        {
            var role = RequireString(arguments, "role");
            var relativeDirectory = RequireString(arguments, "relativeDirectory");
            var startIndex = RequireInt32(arguments, "startIndex");
            var maxEntries = RequireInt32(arguments, "maxEntries");
            var request = new LocalAppLeaseListRequestV051(
                LocalAppReadLeaseV048Service.ListRequestSchemaV051,
                "mcp-list-" + Guid.NewGuid().ToString("N"),
                session.LeaseId,
                session.Bearer,
                session.ApplicationId,
                role,
                relativeDirectory,
                startIndex,
                maxEntries);
            var result = await _leases.AuthorizeAndListAsync(session.WorkspaceRoot, request, cancellationToken);
            var response = new LocalAppMcpListResponseV051(
                ListResponseSchemaV051,
                ToolSurfaceVersionV051,
                DateTimeOffset.Now,
                result.Response.ApplicationId,
                result.Response.Role,
                result.Response.RelativeDirectory,
                result.Response.TotalEntries,
                result.Response.StartIndex,
                result.Response.ReturnedEntries,
                result.Response.NextStartIndex,
                result.Response.DisclosureBytes,
                result.Response.Entries,
                result.Response.RemainingCalls,
                result.Response.RemainingBytes,
                result.Response.ExpiresAt,
                "Result came only through the active v0.48 directory-prefix lease. Listing is non-recursive and returns path/kind/size metadata only; file contents remain available only through read_local_app_chunk.");
            return ToolResultEnvelope(id, false, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (InvalidDataException ex)
        {
            var safe = ex.Message.Replace(session.WorkspaceRoot, "<workspace>", StringComparison.OrdinalIgnoreCase);
            return ToolResultEnvelope(id, true, "REFUSED_BY_ACTIVE_READ_LEASE: " + safe);
        }
        catch (Exception) { return ToolResultEnvelope(id, true, "MCP_ADAPTER_INTERNAL_ERROR"); }
    }

    private static Dictionary<string, object?> BuildToolsListResult()
    {
        var readProperties = new Dictionary<string, object?>
        {
            ["role"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "installed", "source" }, ["description"] = "Role admitted by the active v0.48 lease." },
            ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Relative file path under the already-selected application's leased root." },
            ["offset"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 0 },
            ["maxBytes"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = LocalAppReadToolV046Service.MaxReadBytes },
            ["expectedFileSha256"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Optional exact whole-file SHA-256; mismatch refuses the read." }
        };
        var readTool = new Dictionary<string, object?>
        {
            ["name"] = "read_local_app_chunk",
            ["description"] = "Reads one bounded chunk through the already-active v0.48 Matawaka local-app read lease. ApplicationId, LeaseId, bearer and filesystem root are not caller-selectable.",
            ["inputSchema"] = new Dictionary<string, object?> { ["type"] = "object", ["properties"] = readProperties, ["required"] = new[] { "role", "relativePath", "offset", "maxBytes" }, ["additionalProperties"] = false },
            ["annotations"] = new Dictionary<string, object?> { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false }
        };

        var listProperties = new Dictionary<string, object?>
        {
            ["role"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "installed", "source" }, ["description"] = "Role admitted by the active v0.48 lease." },
            ["relativeDirectory"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Relative directory that must be the root of, or nested inside, an explicitly leased directory-prefix scope. Application root is never accepted." },
            ["startIndex"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 0, ["description"] = "Ordinal-sorted immediate-child pagination index." },
            ["maxEntries"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = LocalAppReadLeaseV048Service.MaxListEntriesV051 }
        };
        var listTool = new Dictionary<string, object?>
        {
            ["name"] = "list_local_app_entries",
            ["description"] = "Lists one bounded page of immediate-child path/kind/size metadata inside an active v0.48 directory-prefix lease. Exact-file scopes do not authorize parent/sibling enumeration. No recursive search, file content, hashes, timestamps or filesystem root are exposed.",
            ["inputSchema"] = new Dictionary<string, object?> { ["type"] = "object", ["properties"] = listProperties, ["required"] = new[] { "role", "relativeDirectory", "startIndex", "maxEntries" }, ["additionalProperties"] = false },
            ["annotations"] = new Dictionary<string, object?> { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false }
        };
        return new Dictionary<string, object?> { ["tools"] = new[] { readTool, listTool } };
    }

    private static async Task<HttpRequest> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var headerBuffer = new MemoryStream();
        var temp = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(temp, cancellationToken);
            if (read <= 0) throw new InvalidDataException("Connection closed before HTTP headers completed.");
            headerBuffer.Write(temp, 0, read);
            headerEnd = FindHeaderEnd(headerBuffer.GetBuffer().AsSpan(0, checked((int)headerBuffer.Length)));
            if (headerEnd < 0 && headerBuffer.Length > MaxHttpHeaderBytes) throw new InvalidDataException("HTTP headers exceed bounded limit.");
            if (headerEnd > MaxHttpHeaderBytes) throw new InvalidDataException("HTTP headers exceed bounded limit.");
        }

        var all = headerBuffer.ToArray();
        var headerText = Encoding.ASCII.GetString(all.AsSpan(0, headerEnd));
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 2) throw new InvalidDataException("Malformed HTTP request.");
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || requestLine[2] is not ("HTTP/1.1" or "HTTP/1.0")) throw new InvalidDataException("Unsupported HTTP request line.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new InvalidDataException("Malformed HTTP header.");
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!headers.TryAdd(name, value)) throw new InvalidDataException("Duplicate HTTP header refused.");
        }
        if (!headers.TryGetValue("Host", out var host) || string.IsNullOrWhiteSpace(host)) throw new InvalidDataException("Host header is required.");
        headers.TryGetValue("Content-Type", out var contentType);
        contentType ??= string.Empty;

        var bodyStart = headerEnd + 4;
        var prefix = all.AsMemory(bodyStart);
        var reader = new PrefixedNetworkReader(prefix, stream);
        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding))
        {
            if (!transferEncoding.Equals("chunked", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only exact chunked Transfer-Encoding is admitted.");
            if (headers.ContainsKey("Content-Length")) throw new InvalidDataException("Ambiguous Content-Length plus Transfer-Encoding is refused.");
            body = await ReadChunkedBodyAsync(reader, cancellationToken);
        }
        else
        {
            if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) || contentLength < 0 || contentLength > MaxProtocolRequestBytes)
                throw new InvalidDataException("Exact bounded Content-Length is required when Transfer-Encoding is absent.");
            if (prefix.Length > contentLength) throw new InvalidDataException("Extra bytes after fixed-length request body are refused.");
            body = new byte[contentLength];
            await reader.ReadExactAsync(body, cancellationToken);
        }
        return new HttpRequest(requestLine[0], requestLine[1], host, contentType, body);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(PrefixedNetworkReader reader, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        while (true)
        {
            var line = await reader.ReadAsciiLineAsync(128, cancellationToken);
            var semicolon = line.IndexOf(';');
            var token = (semicolon >= 0 ? line[..semicolon] : line).Trim();
            if (token.Length == 0 || !int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
                throw new InvalidDataException("Invalid chunk-size line.");
            if (size == 0)
            {
                var trailerBytes = 0;
                while (true)
                {
                    var trailer = await reader.ReadAsciiLineAsync(1024, cancellationToken);
                    trailerBytes += trailer.Length + 2;
                    if (trailerBytes > MaxHttpTrailerBytes) throw new InvalidDataException("HTTP chunk trailers exceed bounded limit.");
                    if (trailer.Length == 0) break;
                    if (!trailer.Contains(':')) throw new InvalidDataException("Malformed HTTP chunk trailer.");
                }
                break;
            }
            if (output.Length + size > MaxProtocolRequestBytes) throw new InvalidDataException("Chunked request body exceeds bounded limit.");
            var chunk = new byte[size];
            await reader.ReadExactAsync(chunk, cancellationToken);
            output.Write(chunk, 0, chunk.Length);
            await reader.RequireCrlfAsync(cancellationToken);
        }
        return output.ToArray();
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++) if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i;
        return -1;
    }

    private static Task WriteHttpJsonAsync(NetworkStream stream, object envelope, CancellationToken cancellationToken)
        => WriteHttpAsync(stream, 200, "OK", "application/json", JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions), cancellationToken);
    private static Task WriteHttpEmptyAsync(NetworkStream stream, int status, string reason, CancellationToken cancellationToken)
        => WriteHttpAsync(stream, status, reason, "application/json", Array.Empty<byte>(), cancellationToken);
    private static Task WriteHttpTextAsync(NetworkStream stream, int status, string reason, string text, CancellationToken cancellationToken, string extraHeaders = "")
        => WriteHttpAsync(stream, status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), cancellationToken, extraHeaders);

    private static async Task WriteHttpAsync(NetworkStream stream, int status, string reason, string contentType, byte[] body, CancellationToken cancellationToken, string extraHeaders = "")
    {
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n{extraHeaders}\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        if (body.Length > 0) await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> StopRuntimeAsync(ActiveAdapter active, CancellationToken cancellationToken)
    {
        active.Session.ClearBearerReference();
        active.Stop.Cancel();
        active.Listener.Stop();
        try { await active.AcceptLoop.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
        catch (OperationCanceledException) when (active.Stop.IsCancellationRequested) { }
        catch (TimeoutException) { }
        finally { active.Stop.Dispose(); }
        return true;
    }

    private static object ResultEnvelope(JsonElement? id, object result)
        => new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    private static object ErrorEnvelope(JsonElement? id, int code, string message)
        => new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new Dictionary<string, object?> { ["code"] = code, ["message"] = message } };
    private static object ToolResultEnvelope(JsonElement? id, bool isError, string text)
        => ResultEnvelope(id, new Dictionary<string, object?> { ["content"] = new[] { new Dictionary<string, object?> { ["type"] = "text", ["text"] = text } }, ["isError"] = isError });

    private static string RequireString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"{name} is required and must be a non-empty string.");
        return value.GetString()!;
    }
    private static long RequireInt64(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)) throw new InvalidDataException($"{name} is required and must be an integer.");
        return number;
    }
    private static int RequireInt32(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) throw new InvalidDataException($"{name} is required and must be an integer.");
        return number;
    }
    private static JsonElement? TryCloneId(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) ? id.Clone() : null;

    private static void RequireSamePreview(LocalAppMcpAdapterPreviewV049 a, LocalAppMcpAdapterPreviewV049 b)
    {
        if (a.ApplicationId != b.ApplicationId || a.LeaseId != b.LeaseId || a.ExpiresAt != b.ExpiresAt || a.RemainingCalls != b.RemainingCalls || a.RemainingBytes != b.RemainingBytes || a.MaxBytesPerRead != b.MaxBytesPerRead || !a.BearerSha256.Equals(b.BearerSha256, StringComparison.OrdinalIgnoreCase) || !a.Scopes.SequenceEqual(b.Scopes))
            throw new InvalidDataException("MCP adapter preview is stale; active lease state changed. Create a new preview.");
    }

    private static bool SafeLeaseId(string value) => value.StartsWith("lease-", StringComparison.Ordinal) && value.Length == 38 && value[6..].All(Uri.IsHexDigit);
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<string> WriteArtifactAsync<T>(string workspaceRoot, string kind, string appId, string leaseId, T value, CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-mcp-read-adapter");
        var path = Path.Combine(dir, $"mcp-read-adapter-{kind}-{LocalAppV046FileBoundary.SafeToken(appId)}-{LocalAppV046FileBoundary.SafeToken(leaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string[] DefaultNonEffects() => new[]
    {
        "no public/LAN listener; IPv4 loopback only",
        "no ASP.NET Core shared-framework dependency",
        "no automatic Secure MCP Tunnel creation or account login",
        "no lease creation/renewal/scope widening",
        "no arbitrary filesystem root",
        "no application/source mutation",
        "no application process launch or execution authority",
        "no Git/catalog/Agent Execute authority",
        "v0.47 manual clipboard relay remains available"
    };

    private sealed class PrefixedNetworkReader
    {
        private readonly ReadOnlyMemory<byte> _prefix;
        private int _position;
        private readonly NetworkStream _stream;
        public PrefixedNetworkReader(ReadOnlyMemory<byte> prefix, NetworkStream stream) { _prefix = prefix; _stream = stream; }

        public async Task ReadExactAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            var written = 0;
            if (_position < _prefix.Length && destination.Length > 0)
            {
                var take = Math.Min(destination.Length, _prefix.Length - _position);
                _prefix.Slice(_position, take).CopyTo(destination);
                _position += take;
                written += take;
            }
            while (written < destination.Length)
            {
                var read = await _stream.ReadAsync(destination[written..], cancellationToken);
                if (read <= 0) throw new InvalidDataException("Connection closed before HTTP body completed.");
                written += read;
            }
        }

        public async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_position < _prefix.Length) return _prefix.Span[_position++];
            var one = new byte[1];
            var read = await _stream.ReadAsync(one, cancellationToken);
            if (read != 1) throw new InvalidDataException("Connection closed during chunked body.");
            return one[0];
        }

        public async Task<string> ReadAsciiLineAsync(int maxBytes, CancellationToken cancellationToken)
        {
            using var line = new MemoryStream();
            while (line.Length <= maxBytes)
            {
                var b = await ReadByteAsync(cancellationToken);
                if (b == 13)
                {
                    if (await ReadByteAsync(cancellationToken) != 10) throw new InvalidDataException("Malformed CRLF in chunked body.");
                    return Encoding.ASCII.GetString(line.ToArray());
                }
                if (b == 10) throw new InvalidDataException("Bare LF in chunked body is refused.");
                line.WriteByte(b);
            }
            throw new InvalidDataException("Chunk metadata line exceeds bounded limit.");
        }

        public async Task RequireCrlfAsync(CancellationToken cancellationToken)
        {
            if (await ReadByteAsync(cancellationToken) != 13 || await ReadByteAsync(cancellationToken) != 10) throw new InvalidDataException("Chunk data is not followed by CRLF.");
        }
    }

    private sealed record HttpRequest(string Method, string Path, string Host, string ContentType, byte[] Body);
    private sealed record McpProcessResult(bool IsNotification, object? Envelope);
    private sealed record ActiveAdapter(TcpListener Listener, CancellationTokenSource Stop, Task AcceptLoop, LocalAppMcpAdapterSessionV049 Session, string ApplicationId, string LeaseId, string EndpointUrl, string EndpointTokenSha256, DateTimeOffset LeaseExpiresAt, DateTimeOffset StartedAt);
}
