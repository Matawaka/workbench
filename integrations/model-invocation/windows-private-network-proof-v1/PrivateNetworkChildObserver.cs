using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class PrivateNetworkChildObserver
{
    private const string Authorization = "ISSUE-106-CURRENT-EXPLICIT-DEVELOPMENT";
    private const string NativeBoundaryBlob = "8a8f91a13c114e9c224cf449193800f0fedfdaf2";
    private const string SelectionRule = "OPERATIONAL_RFC1918_IPV4_LOWEST_NUMERIC_THEN_INTERFACE_ID";
    private const string Schema = "matawaka.workbench-private-network-child-observer/v0.1";

    private sealed record TargetEvidence(string Address, string InterfaceId, string InterfaceName, string InterfaceType, string SelectionRule);
    private sealed record SelectedTarget(IPAddress Address, string InterfaceId, string InterfaceName, NetworkInterfaceType InterfaceType);
    private sealed record Receipt(
        string Schema,
        string SourceHead,
        string NativeBoundaryBlob,
        TargetEvidence? Target,
        int TargetPort,
        bool ParentTargetControlConnected,
        bool UnexpectedIsolatedConnectionObserved,
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
        bool DnsUsed,
        bool ExternalNetworkAccessed,
        bool NetworkIsolationConfigMutated,
        bool FirewallRuleMutated,
        bool RouteOrInterfaceMutated,
        bool GlobalWindowsPolicyMutated,
        bool ProductionProviderRegistered,
        bool ProofClaimed,
        string? FailureStage,
        int? FailureNativeCode);

    private static int Main(string[] args)
    {
        if (args.Length != 6 || args[0] != "--observe-private-child" || args[5] != Authorization || !IsHex(args[4], 40))
            return 64;

        string root = NativeBoundary.ValidateRoot(args[1]);
        string manifestPath = Path.GetFullPath(args[2]);
        string receiptPath = Path.GetFullPath(args[3]);
        string sourceHead = args[4];
        NativeBoundary.Need(!File.Exists(receiptPath), "DIAGNOSTIC_CREATE_ONLY_RECEIPT_REQUIRED");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes = manifest.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

        var boundary = new NativeBoundary();
        TcpListener? listener = null;
        TargetEvidence? target = null;
        int targetPort = 0;
        bool control = false, unexpected = false;
        byte[] stdout = [], stderr = [];
        string? failure = null;
        int? native = null;
        try
        {
            boundary.Prepare(root, hashes);
            SelectedTarget selected = SelectPrivateTarget();
            target = new TargetEvidence(selected.Address.ToString(), selected.InterfaceId, selected.InterfaceName, selected.InterfaceType.ToString(), SelectionRule);
            listener = new TcpListener(selected.Address, 0);
            listener.Start(2);
            targetPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var normal = new TcpClient(AddressFamily.InterNetwork))
            {
                normal.Connect(selected.Address, targetPort);
                using var accepted = listener.AcceptTcpClient();
                control = accepted.Connected;
            }
            NativeBoundary.Need(control, "DIAGNOSTIC_PARENT_TARGET_CONTROL_FAILED");

            boundary.Start(root, targetPort);
            var stdoutTask = Task.Run(() => ReadBounded(boundary.Output));
            var stderrTask = Task.Run(() => ReadBounded(boundary.Error));
            NativeBoundary.Need(boundary.Wait(10000), "DIAGNOSTIC_CHILD_TIMEOUT");
            unexpected = listener.Pending();
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
            listener?.Stop();
            boundary.Dispose();
        }

        var receipt = new Receipt(
            Schema, sourceHead, NativeBoundaryBlob, target, targetPort, control, unexpected,
            boundary.Token, boundary.JobLimitsVerified, boundary.ProcessCreated, boundary.ProcessExited, boundary.ExitCode,
            boundary.ProfileCreated, boundary.ProfileRemoved, boundary.CleanupSucceeded,
            stdout.Length, Decode(stdout), stderr.Length, Decode(stderr),
            false, false, false, false, false, false, false, false, failure, native);
        using (var file = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(JsonSerializer.Serialize(receipt));

        return boundary.Token is not null && boundary.JobLimitsVerified && boundary.ProcessCreated && boundary.ProcessExited &&
            boundary.ProfileCreated && boundary.ProfileRemoved && boundary.CleanupSucceeded && control && stdout.Length > 0 ? 0 : 3;
    }

    private static SelectedTarget SelectPrivateTarget()
    {
        var candidates = new List<SelectedTarget>();
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            IPInterfaceProperties properties;
            try { properties = networkInterface.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                IPAddress address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || !IsRfc1918(address))
                    continue;
                candidates.Add(new SelectedTarget(address, networkInterface.Id, networkInterface.Name, networkInterface.NetworkInterfaceType));
            }
        }
        NativeBoundary.Need(candidates.Count > 0, "DIAGNOSTIC_PRIVATE_TARGET_ABSENT");
        return candidates.OrderBy(c => AddressKey(c.Address)).ThenBy(c => c.InterfaceId, StringComparer.Ordinal).First();
    }

    private static uint AddressKey(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        NativeBoundary.Need(bytes.Length == 4, "DIAGNOSTIC_TARGET_ADDRESS_SHAPE");
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static bool IsRfc1918(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
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

    private static string Decode(byte[] bytes) => bytes.Length == 0 ? "" : Encoding.UTF8.GetString(bytes);
    private static bool IsHex(string value, int length) => value.Length == length && value.All(c => Uri.IsHexDigit(c));
}
