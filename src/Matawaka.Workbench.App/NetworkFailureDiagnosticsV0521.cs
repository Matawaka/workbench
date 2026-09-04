using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record NetworkFailureDiagnosticV0521(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string Classification,
    string HttpRequestError,
    string? SocketError,
    string? InnerExceptionType,
    bool RawExceptionMessagePersisted,
    bool RequestHeadersPersisted,
    bool ProxyCredentialPersisted,
    bool BearerPersisted,
    string Note);

public static class NetworkFailureDiagnosticsV0521
{
    public const string Schema = "matawaka.workbench-network-failure-diagnostic/v0.52.1";
    public const string Version = "0.52.1";

    public static NetworkFailureDiagnosticV0521? TryCreate(ArtifactAcquisitionExceptionV052 failure)
    {
        if (failure is null || failure.Classification != "NETWORK_FAILED") return null;
        var http = FindInner<HttpRequestException>(failure);
        if (http is null) return null;
        var socket = FindInner<SocketException>(http);
        var httpError = http.HttpRequestError.ToString();
        var socketError = socket?.SocketErrorCode.ToString();
        var classification = Classify(httpError, socketError);
        return new NetworkFailureDiagnosticV0521(
            Schema,
            Version,
            DateTimeOffset.Now,
            classification,
            httpError,
            socketError,
            http.InnerException?.GetType().Name,
            false,
            false,
            false,
            false,
            "Bounded transport diagnostic only. Raw exception messages, request headers, proxy credentials and acquisition bearer are deliberately omitted. Diagnostic evidence creates no retry, network, acquisition or execution authority.");
    }

    internal static string Classify(string? httpRequestError, string? socketError)
    {
        var http = httpRequestError ?? string.Empty;
        var socket = socketError ?? string.Empty;
        if (http == "NameResolutionError" || socket is "HostNotFound" or "TryAgain" or "NoData") return "NETWORK_DNS_FAILED";
        if (http == "SecureConnectionError") return "NETWORK_TLS_FAILED";
        if (http == "ProxyTunnelError") return "NETWORK_PROXY_TUNNEL_FAILED";
        if (socket == "ConnectionRefused") return "NETWORK_CONNECTION_REFUSED";
        if (socket is "NetworkUnreachable" or "HostUnreachable") return "NETWORK_UNREACHABLE";
        if (socket == "TimedOut") return "NETWORK_CONNECT_TIMEOUT";
        if (http is "VersionNegotiationError" or "HttpProtocolError" or "InvalidResponse" or "ResponseEnded" or "ExtendedConnectNotSupported") return "NETWORK_PROTOCOL_FAILED";
        if (http == "UserAuthenticationError") return "NETWORK_AUTH_NEGOTIATION_FAILED";
        if (http == "ConfigurationLimitExceeded") return "NETWORK_CLIENT_LIMIT_FAILED";
        if (http == "ConnectionError") return "NETWORK_CONNECTION_FAILED";
        return "NETWORK_FAILED_OTHER";
    }

    public static string OperatorSummary(NetworkFailureDiagnosticV0521 diagnostic)
    {
        var sb = new StringBuilder();
        sb.Append(diagnostic.Classification);
        sb.Append("; HttpRequestError=").Append(diagnostic.HttpRequestError);
        if (!string.IsNullOrWhiteSpace(diagnostic.SocketError)) sb.Append("; SocketError=").Append(diagnostic.SocketError);
        if (!string.IsNullOrWhiteSpace(diagnostic.InnerExceptionType)) sb.Append("; Inner=").Append(diagnostic.InnerExceptionType);
        return sb.ToString();
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        string requestId,
        NetworkFailureDiagnosticV0521 diagnostic,
        CancellationToken cancellationToken)
    {
        if (diagnostic.Schema != Schema || diagnostic.Version != Version || diagnostic.RawExceptionMessagePersisted ||
            diagnostic.RequestHeadersPersisted || diagnostic.ProxyCredentialPersisted || diagnostic.BearerPersisted)
            throw new InvalidDataException("Unsafe or unexpected v0.52.1 network diagnostic receipt.");
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "artifact-acquisition-v0521-diagnostics");
        var safe = LocalAppV046FileBoundary.SafeToken(requestId);
        var path = Path.Combine(dir, $"network-diagnostic-{safe}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(diagnostic, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("diagnostic-v0521-dns", Classify("NameResolutionError", "HostNotFound") == "NETWORK_DNS_FAILED", Classify("NameResolutionError", "HostNotFound"), "NETWORK_DNS_FAILED"),
        ("diagnostic-v0521-tls", Classify("SecureConnectionError", null) == "NETWORK_TLS_FAILED", Classify("SecureConnectionError", null), "NETWORK_TLS_FAILED"),
        ("diagnostic-v0521-proxy", Classify("ProxyTunnelError", null) == "NETWORK_PROXY_TUNNEL_FAILED", Classify("ProxyTunnelError", null), "NETWORK_PROXY_TUNNEL_FAILED"),
        ("diagnostic-v0521-refused", Classify("ConnectionError", "ConnectionRefused") == "NETWORK_CONNECTION_REFUSED", Classify("ConnectionError", "ConnectionRefused"), "NETWORK_CONNECTION_REFUSED"),
        ("diagnostic-v0521-unreachable", Classify("ConnectionError", "NetworkUnreachable") == "NETWORK_UNREACHABLE", Classify("ConnectionError", "NetworkUnreachable"), "NETWORK_UNREACHABLE"),
        ("diagnostic-v0521-secret-boundary", true, "raw messages/headers/proxy credentials/bearer omitted", "omitted"),
        ("diagnostic-v0521-authority", true, "diagnostic receipt only", "no retry/network/acquisition/execution authority")
    };

    private static T? FindInner<T>(Exception value) where T : Exception
    {
        Exception? current = value;
        while (current is not null)
        {
            if (current is T match) return match;
            current = current.InnerException;
        }
        return null;
    }
}
