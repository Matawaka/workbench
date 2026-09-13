using System.Runtime.InteropServices;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class LoopbackApiChildDiagnostic
{
    private const string Schema = "matawaka.workbench-appcontainer-loopback-api-diagnostic/v0.1";

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [DllImport("Firewallapi.dll", ExactSpelling = true)]
    private static extern uint NetworkIsolationGetAppContainerConfig(out uint count, out nint entries);

    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint NetworkIsolationDiagnoseConnectFailure(string serverName);

    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint NetworkIsolationDiagnoseConnectFailureAndGetInfo(string serverName, out int errorType);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetProcessHeap();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool HeapFree(nint heap, uint flags, nint memory);

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--child" || !int.TryParse(args[1], out int port) || port is <= 0 or > 65535)
            return 64;

        uint count = 0;
        nint entries = 0;
        uint configReturn = NetworkIsolationGetAppContainerConfig(out count, out entries);
        try
        {
            uint hasRequiredCapability = NetworkIsolationDiagnoseConnectFailure("127.0.0.1");
            uint infoReturn = NetworkIsolationDiagnoseConnectFailureAndGetInfo("127.0.0.1", out int infoType);
            var result = new
            {
                schema = Schema,
                target = "127.0.0.1",
                observedPort = port,
                getConfigReturn = configReturn,
                getConfigCount = count,
                diagnoseHasRequiredCapability = hasRequiredCapability != 0,
                diagnoseInfoReturn = infoReturn,
                diagnoseInfoType = infoType,
                networkIsolationConfigMutated = false,
                firewallRuleMutated = false,
                globalWindowsPolicyMutated = false,
                proofClaimed = false
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        finally
        {
            if (configReturn == 0 && entries != 0)
            {
                nint heap = GetProcessHeap();
                int size = Marshal.SizeOf<SidAndAttributes>();
                for (uint i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<SidAndAttributes>(entries + checked((int)i * size));
                    if (item.Sid != 0) _ = HeapFree(heap, 0, item.Sid);
                }
                _ = HeapFree(heap, 0, entries);
            }
        }
    }
}
