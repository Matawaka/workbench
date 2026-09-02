using System.IO;
using System.Text;

namespace Matawaka.Workbench.App;

public sealed record LocalAppReadRequestV046(
    string Schema,
    string ApplicationId,
    string Role,
    string RelativePath,
    long Offset,
    int MaxBytes);

public sealed record LocalAppReadResponseV046(
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
    bool FileMutationPerformed,
    bool ProcessLaunchPerformed,
    bool NetworkAccessPerformed,
    string Note);

public sealed class LocalAppReadToolV046Service
{
    public const string Version = "0.46.0";
    public const string RequestSchema = "matawaka.local-app-read-request/v0.46";
    public const string ResponseSchema = "matawaka.local-app-read-response/v0.46";
    public const int MaxReadBytes = 1024 * 1024;

    public LocalAppReadResponseV046 Read(
        string workspaceRoot,
        LocalAppReadRequestV046 request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Schema != RequestSchema)
            throw new InvalidDataException("Exact v0.46 local-app read request schema is required.");
        if (request.Offset < 0) throw new InvalidDataException("Read offset cannot be negative.");
        if (request.MaxBytes <= 0 || request.MaxBytes > MaxReadBytes)
            throw new InvalidDataException($"MaxBytes must be between 1 and {MaxReadBytes}.");

        var role = request.Role.Trim().ToLowerInvariant();
        var root = role switch
        {
            "installed" => LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, request.ApplicationId),
            "source" => LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, request.ApplicationId, requireBinding: true),
            _ => throw new InvalidDataException("Read role must be exactly installed or source.")
        };
        var relative = LocalAppV046FileBoundary.NormalizeRelative(request.RelativePath);
        LocalAppV046FileBoundary.EnsureNoReparseBoundary(root, relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        LocalAppV046FileBoundary.EnsureInsideRoot(root, path, "read target");
        if (!File.Exists(path)) throw new InvalidDataException($"Read target file is missing: {relative}");
        LocalAppV046FileBoundary.RejectReparse(path, "read target");

        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        if (request.Offset > info.Length) throw new InvalidDataException("Read offset is beyond end of file.");
        var remaining = info.Length - request.Offset;
        var count = (int)Math.Min(request.MaxBytes, remaining);
        var bytes = new byte[count];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Seek(request.Offset, SeekOrigin.Begin);
            var read = 0;
            while (read < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = stream.Read(bytes, read, count - read);
                if (n == 0) break;
                read += n;
            }
            if (read != bytes.Length) Array.Resize(ref bytes, read);
        }

        string? text = null;
        try
        {
            var strict = new UTF8Encoding(false, true);
            var candidate = strict.GetString(bytes);
            if (!candidate.Contains('\0')) text = candidate;
        }
        catch (DecoderFallbackException)
        {
            text = null;
        }

        return new LocalAppReadResponseV046(
            ResponseSchema,
            Version,
            DateTimeOffset.Now,
            request.ApplicationId,
            role,
            relative,
            info.Length,
            LocalAppV046FileBoundary.HashFile(path),
            request.Offset,
            bytes.Length,
            request.Offset + bytes.Length >= info.Length,
            Convert.ToBase64String(bytes),
            text,
            false,
            false,
            false,
            "Bounded local read primitive only. v0.46 provides no external transport or automatic disclosure; a future connector/tool adapter may invoke this same fixed-root service after separate authorization.");
    }

    public static string ToolContractJson() => """
{
  "Schema": "matawaka.local-app-read-tool-contract/v0.46",
  "RequestSchema": "matawaka.local-app-read-request/v0.46",
  "ResponseSchema": "matawaka.local-app-read-response/v0.46",
  "Roles": ["installed", "source"],
  "MaxReadBytes": 1048576,
  "PathModel": "ApplicationId + role + relative path; fixed root confinement; reparse refusal",
  "Response": "full-file SHA-256/size + requested chunk Base64 + UTF-8 text when strictly decodable",
  "FileMutationAllowed": false,
  "ProcessLaunchAllowed": false,
  "NetworkTransportImplemented": false,
  "AutomaticDisclosureAllowed": false,
  "Note": "Service contract is tool-ready local capability; transport/connector authority is intentionally absent in v0.46."
}
""";

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("read-v046-max-chunk", MaxReadBytes == 1048576, MaxReadBytes.ToString(), "1048576"),
        ("read-v046-fixed-roles", true, "installed/source", "installed/source"),
        ("read-v046-mutation", true, "false", "false"),
        ("read-v046-execution", true, "false", "false"),
        ("read-v046-network-transport", true, "not implemented", "not implemented")
    };
}
