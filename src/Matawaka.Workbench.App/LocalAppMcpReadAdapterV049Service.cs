using System.ComponentModel;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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

[McpServerToolType]
public sealed class LocalAppMcpReadToolsV049
{
    [McpServerTool, Description("Reads one bounded chunk from the explicitly leased Matawaka local application. The adapter is already fixed to one ApplicationId and one active v0.48 read lease; callers cannot supply or widen those authorities.")]
    public async Task<string> read_local_app_chunk(
        LocalAppMcpAdapterSessionV049 session,
        LocalAppReadLeaseV048Service leaseService,
        [Description("Exactly 'installed' or 'source'. Must be admitted by the active lease.")] string role,
        [Description("Relative file path under the leased installed/source root.")] string relativePath,
        [Description("Zero-based byte offset.")] long offset,
        [Description("Maximum bytes to read. Lease and hard ceilings still apply.")] int maxBytes,
        [Description("Optional exact whole-file SHA-256. A mismatch is refused instead of guessed.")] string? expectedFileSha256 = null,
        CancellationToken cancellationToken = default)
    {
        var request = new LocalAppLeaseReadRequestV048(
            LocalAppReadLeaseV048Service.ReadRequestSchema,
            "mcp-read-" + Guid.NewGuid().ToString("N"),
            session.LeaseId,
            session.Bearer,
            session.ApplicationId,
            role,
            relativePath,
            offset,
            maxBytes,
            expectedFileSha256);
        var result = await leaseService.AuthorizeAndReadAsync(session.WorkspaceRoot, request, cancellationToken);
        var response = new LocalAppMcpReadResponseV049(
            LocalAppMcpReadAdapterV049Service.ReadResponseSchema,
            LocalAppMcpReadAdapterV049Service.Version,
            DateTimeOffset.Now,
            result.Response.ApplicationId,
            result.Response.Role,
            result.Response.RelativePath,
            result.Response.FileBytes,
            result.Response.FileSha256,
            result.Response.Offset,
            result.Response.ReturnedBytes,
            result.Response.EndOfFile,
            result.Response.ContentBase64,
            result.Response.Utf8Text,
            result.Response.RemainingCalls,
            result.Response.RemainingBytes,
            result.Response.ExpiresAt,
            "Result came only through the already-accepted v0.48 lease gate. No mutation or process execution authority is present in this tool.");
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed class LocalAppMcpReadAdapterV049Service
{
    public const string Version = "0.49.0";
    public const string PreviewSchema = "matawaka.local-app-mcp-read-adapter-preview/v0.49";
    public const string GrantSchema = "matawaka.local-app-mcp-read-adapter-grant/v0.49";
    public const string StartReceiptSchema = "matawaka.local-app-mcp-read-adapter-start-receipt/v0.49";
    public const string StopReceiptSchema = "matawaka.local-app-mcp-read-adapter-stop-receipt/v0.49";
    public const string ReadResponseSchema = "matawaka.local-app-mcp-read-response/v0.49";
    public const string McpSdkPackage = "ModelContextProtocol.AspNetCore";
    public const string McpSdkVersion = "2.2.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LocalAppReadLeaseV048Service _leases = new();
    private ActiveAdapter? _active;

    public LocalAppMcpAdapterPreviewV049 PreviewFromGrantJson(
        string workspaceRoot,
        string selectedApplicationId,
        string grantJson,
        CancellationToken cancellationToken)
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

    public LocalAppMcpAdapterPreviewV049 Preview(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppReadLeaseGrantV048 grant,
        CancellationToken cancellationToken)
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
            PreviewSchema,
            Version,
            DateTimeOffset.Now,
            selectedApplicationId,
            state.LeaseId,
            state.Scopes,
            state.ExpiresAt,
            state.RemainingCalls,
            state.RemainingBytes,
            state.MaxBytesPerRead,
            state.BearerSha256,
            true,
            false,
            true,
            DefaultNonEffects(),
            "Preview validates the selected app, active v0.48 lease and bearer hash only. It creates no listener, tunnel or file read. Explicit loopback-listener confirmation is still required.");
    }

    public async Task<LocalAppMcpAdapterGrantV049> StartAsync(
        string workspaceRoot,
        string selectedApplicationId,
        LocalAppMcpAdapterPreviewV049 confirmedPreview,
        string grantJson,
        CancellationToken cancellationToken)
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

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LocalAppMcpReadAdapterV049Service).Assembly.FullName,
                Args = Array.Empty<string>()
            });
            builder.Logging.ClearProviders();
            builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddSingleton(session);
            builder.Services.AddSingleton<LocalAppReadLeaseV048Service>();
            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools<LocalAppMcpReadToolsV049>();

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                var host = context.Request.Host.Host;
                if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                await next();
            });
            app.MapMcp(endpointPath);

            try
            {
                await app.StartAsync(cancellationToken);
                var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
                    ?? throw new InvalidDataException("Kestrel did not expose its bound address.");
                var address = addresses.SingleOrDefault(x => x.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("MCP adapter did not bind exactly one IPv4 loopback address.");
                if (addresses.Count != 1)
                    throw new InvalidDataException("MCP adapter unexpectedly bound more than one listener address.");
                var endpoint = address.TrimEnd('/') + endpointPath;
                _active = new ActiveAdapter(app, session, selectedApplicationId, fresh.LeaseId, endpoint, endpointTokenSha, fresh.ExpiresAt, DateTimeOffset.Now);
                return new LocalAppMcpAdapterGrantV049(
                    GrantSchema,
                    Version,
                    DateTimeOffset.Now,
                    selectedApplicationId,
                    fresh.LeaseId,
                    endpoint,
                    endpointTokenSha,
                    fresh.ExpiresAt,
                    new[] { "read_local_app_chunk" },
                    true,
                    false,
                    false,
                    "This is a local loopback MCP endpoint only. It is not reachable by ChatGPT directly and no Secure MCP Tunnel was started. Configure any supported tunnel separately and deliberately.");
            }
            catch
            {
                session.ClearBearerReference();
                try { await app.StopAsync(CancellationToken.None); } catch { }
                await app.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(LocalAppMcpAdapterStartReceiptV049 Receipt, string ReceiptPath)> WriteStartReceiptAsync(
        string workspaceRoot,
        LocalAppMcpAdapterGrantV049 grant,
        bool endpointClipboardWritePerformed,
        CancellationToken cancellationToken)
    {
        var active = _active ?? throw new InvalidDataException("No active v0.49 MCP adapter exists for a start receipt.");
        if (active.ApplicationId != grant.ApplicationId || active.LeaseId != grant.LeaseId || active.EndpointTokenSha256 != grant.EndpointTokenSha256)
            throw new InvalidDataException("MCP adapter grant does not match active runtime.");
        var uri = new Uri(grant.EndpointUrl);
        var receipt = new LocalAppMcpAdapterStartReceiptV049(
            StartReceiptSchema,
            Version,
            DateTimeOffset.Now,
            grant.ApplicationId,
            grant.LeaseId,
            grant.EndpointTokenSha256,
            $"{uri.Scheme}://{uri.Host}:{uri.Port}",
            grant.LeaseExpiresAt,
            grant.Tools,
            endpointClipboardWritePerformed,
            false,
            true,
            false,
            false,
            false,
            DefaultNonEffects(),
            "MCP_READ_ADAPTER_LOOPBACK_READY_NO_TUNNEL",
            "The adapter is listening only on IPv4 loopback and delegates every content read to the active v0.48 lease. Endpoint token plaintext and bearer plaintext are not persisted in this receipt.");
        var path = await WriteArtifactAsync(workspaceRoot, "start", grant.ApplicationId, grant.LeaseId, receipt, cancellationToken);
        return (receipt, path);
    }

    public async Task<(LocalAppMcpAdapterStopReceiptV049 Receipt, string ReceiptPath)> StopAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = _active ?? throw new InvalidDataException("No active v0.49 MCP adapter exists in this Workbench process.");
            _active = null;
            active.Session.ClearBearerReference();
            var stopped = false;
            try
            {
                await active.App.StopAsync(cancellationToken);
                stopped = true;
            }
            finally
            {
                await active.App.DisposeAsync();
            }
            var receipt = new LocalAppMcpAdapterStopReceiptV049(
                StopReceiptSchema,
                Version,
                DateTimeOffset.Now,
                active.ApplicationId,
                active.LeaseId,
                active.EndpointTokenSha256,
                stopped,
                true,
                false,
                false,
                false,
                DefaultNonEffects(),
                "MCP_READ_ADAPTER_STOPPED_LOCAL_ONLY",
                "The local listener stopped and the Workbench-held plaintext bearer reference was cleared. This is reference clearing, not a claim of managed-memory zeroization.");
            var path = await WriteArtifactAsync(workspaceRoot, "stop", active.ApplicationId, active.LeaseId, receipt, cancellationToken);
            return (receipt, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopBestEffortAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var active = _active;
            _active = null;
            if (active is null) return;
            active.Session.ClearBearerReference();
            try { await active.App.StopAsync(CancellationToken.None); } catch { }
            try { await active.App.DisposeAsync(); } catch { }
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsActiveFor(string applicationId)
        => _active is { } active && active.ApplicationId.Equals(applicationId, StringComparison.Ordinal);

    public static async Task<IReadOnlyList<string>> ProbeToolNamesAsync(string endpointUrl, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpointUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "Matawaka-v0.49-qualification-client"
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-v049-sdk-package", McpSdkPackage == "ModelContextProtocol.AspNetCore", McpSdkPackage, "ModelContextProtocol.AspNetCore"),
        ("mcp-v049-sdk-version", McpSdkVersion == "2.2.0", McpSdkVersion, "2.2.0"),
        ("mcp-v049-tool-count", true, "read_local_app_chunk only", "1 read-only content tool"),
        ("mcp-v049-bound-authority", true, "ApplicationId/LeaseId/bearer fixed in DI session", "not MCP arguments"),
        ("mcp-v049-loopback", true, "IPAddress.Loopback + random port + random path token", "127.0.0.1 only"),
        ("mcp-v049-public-exposure", true, "false", "false"),
        ("mcp-v049-tunnel", true, "not started by Workbench", "separate authority")
    };

    private static void RequireSamePreview(LocalAppMcpAdapterPreviewV049 a, LocalAppMcpAdapterPreviewV049 b)
    {
        if (a.ApplicationId != b.ApplicationId || a.LeaseId != b.LeaseId || a.ExpiresAt != b.ExpiresAt ||
            a.RemainingCalls != b.RemainingCalls || a.RemainingBytes != b.RemainingBytes || a.MaxBytesPerRead != b.MaxBytesPerRead ||
            !a.BearerSha256.Equals(b.BearerSha256, StringComparison.OrdinalIgnoreCase) ||
            !a.Scopes.SequenceEqual(b.Scopes))
            throw new InvalidDataException("MCP adapter preview is stale; active lease state changed. Create a new preview.");
    }

    private static bool SafeLeaseId(string value)
        => value.StartsWith("lease-", StringComparison.Ordinal) && value.Length == 38 && value[6..].All(Uri.IsHexDigit);

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
        "no automatic Secure MCP Tunnel creation or account login",
        "no lease creation/renewal/scope widening",
        "no arbitrary filesystem root",
        "no application/source mutation",
        "no application process launch or execution authority",
        "no Git/catalog/Agent Execute authority",
        "v0.47 manual clipboard relay remains available"
    };

    private sealed record ActiveAdapter(
        WebApplication App,
        LocalAppMcpAdapterSessionV049 Session,
        string ApplicationId,
        string LeaseId,
        string EndpointUrl,
        string EndpointTokenSha256,
        DateTimeOffset LeaseExpiresAt,
        DateTimeOffset StartedAt);
}
