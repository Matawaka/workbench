using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class StderrDiagnostic
{
    private const string Authorization = "ISSUE-105-CURRENT-EXPLICIT-DEVELOPMENT";
    private const string Schema = "matawaka.workbench-appcontainer-child-stderr-diagnostic/v0.1";

    private sealed record Receipt(
        string Schema,
        string SourceHead,
        string NativeBoundaryBlob,
        TokenObservation? Token,
        bool JobLimitsVerified,
        bool ProcessCreated,
        bool ProcessExited,
        uint? ProcessExitCode,
        bool ProfileCreated,
        bool ProfileRemoved,
        bool CleanupSucceeded,
        int StdoutBytes,
        string StdoutUtf8,
        int StderrBytes,
        string StderrUtf8,
        bool NetworkIsolationConfigMutated,
        bool FirewallRuleMutated,
        bool GlobalWindowsPolicyMutated,
        bool ProductionProviderRegistered,
        bool ProofClaimed,
        string? FailureStage,
        int? FailureNativeCode);

    private static int Main(string[] args)
    {
        if (args.Length != 5 || args[0] != "--child-stderr-diagnostic" || args[4] != Authorization || !IsHex(args[3], 40))
            return 64;

        string root = NativeBoundary.ValidateRoot(args[1]);
        string manifestPath = Path.GetFullPath(args[2]);
        string sourceHead = args[3];
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes = manifest.RootElement.GetProperty("files").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);

        var boundary = new NativeBoundary();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        byte[] stdout = [];
        byte[] stderr = [];
        string? failure = null;
        int? native = null;
        try
        {
            boundary.Prepare(root, hashes);
            listener.Start(1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            boundary.Start(root, port);

            var stdoutTask = Task.Run(() => ReadBounded(boundary.Output));
            var stderrTask = Task.Run(() => ReadBounded(boundary.Error));
            NativeBoundary.Need(boundary.Wait(10000), "DIAGNOSTIC_CHILD_TIMEOUT");
            stdout = stdoutTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            stderr = stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch (BoundaryFailure e)
        {
            failure = e.Message;
            native = e.NativeCode;
        }
        catch (Exception e)
        {
            failure = "MANAGED_" + e.GetType().Name + ":" + e.Message;
        }
        finally
        {
            listener.Stop();
            boundary.Dispose();
        }

        var receipt = new Receipt(
            Schema,
            sourceHead,
            "8a8f91a13c114e9c224cf449193800f0fedfdaf2",
            boundary.Token,
            boundary.JobLimitsVerified,
            boundary.ProcessCreated,
            boundary.ProcessExited,
            boundary.ExitCode,
            boundary.ProfileCreated,
            boundary.ProfileRemoved,
            boundary.CleanupSucceeded,
            stdout.Length,
            Decode(stdout),
            stderr.Length,
            Decode(stderr),
            false,
            false,
            false,
            false,
            false,
            failure,
            native);

        Console.WriteLine(JsonSerializer.Serialize(receipt));
        return boundary.Token is not null && boundary.JobLimitsVerified && boundary.ProcessCreated && boundary.ProcessExited &&
               boundary.ProfileCreated && boundary.ProfileRemoved && boundary.CleanupSucceeded ? 0 : 3;
    }

    private static byte[] ReadBounded(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            int n = stream.Read(chunk);
            if (n == 0) break;
            NativeBoundary.Need(buffer.Length + n <= 8192, "DIAGNOSTIC_STREAM_LIMIT");
            buffer.Write(chunk, 0, n);
        }
        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
}
