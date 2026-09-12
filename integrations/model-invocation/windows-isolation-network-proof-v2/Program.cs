using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class NetworkProofV2
{
    private const string Authorization = "ISSUE-105-CURRENT-EXPLICIT-DEVELOPMENT";
    private const string Predecessor = "c4c0d2287ae944fd65d046e2ce6451d6e9d92d4a";
    private const string ExactNativeBoundaryBlob = "8a8f91a13c114e9c224cf449193800f0fedfdaf2";
    private const string ProfilePrefix = "Matawaka.IsolationProbe.";
    private const string ChildSchema = "matawaka.workbench-appcontainer-network-child/v0.2";
    private const string ProofSchema = "matawaka.workbench-windows-network-isolation-proof/v0.2";
    private const string ContractJson = "{\"schema\":\"matawaka.workbench-windows-host-network-profile/v0.2\",\"nativeBoundaryBlob\":\"8a8f91a13c114e9c224cf449193800f0fedfdaf2\",\"appContainerCapabilities\":0,\"loopbackExemptionRequired\":false,\"childProcessRestricted\":true,\"jobActiveProcessLimit\":1,\"jobProcessMemoryBytes\":536870912,\"killOnJobClose\":true,\"dieOnUnhandledException\":true,\"networkTarget\":\"127.0.0.1\",\"requiredMissingCapability\":\"PRIVATE_NETWORK\",\"socketTimeoutMilliseconds\":1000,\"timeoutAloneIsProof\":false}";
    private static readonly string ContractDigest = Hash(Encoding.UTF8.GetBytes(ContractJson));

    private sealed record ChildEvidence(
        string Schema,
        string ContractDigestSha256,
        string PackageSid,
        int CapabilityCount,
        bool LoopbackExemptDuring,
        bool DiagnoseHasRequiredCapability,
        uint DiagnoseInfoReturn,
        int DiagnoseInfoType,
        string SocketOutcome,
        int? SocketCode,
        bool ReadAllowed,
        bool ReadDenied,
        bool WriteDenied,
        bool ChildDenied,
        int? ChildErrorCode);

    private sealed record ProofEvidence(
        string Schema,
        string Status,
        string SourceHead,
        string Predecessor,
        string NativeBoundaryBlob,
        string ContractDigestSha256,
        string ProfileName,
        string PackageSid,
        string ChildExecutableSha256,
        TokenObservation? Token,
        bool JobLimitsVerified,
        bool? LoopbackExemptBefore,
        bool? LoopbackExemptAfter,
        bool NormalLoopbackControlConnected,
        bool UnexpectedIsolatedConnectionObserved,
        ChildEvidence? Child,
        bool ProcessCreated,
        bool ProcessExited,
        uint? ProcessExitCode,
        bool ProfileCreated,
        bool ProfileRemoved,
        bool CleanupSucceeded,
        bool OsNetworkIsolationProven,
        bool SocketTimeoutPromotedToProof,
        bool NetworkIsolationConfigMutated,
        bool FirewallRuleMutated,
        bool GlobalWindowsPolicyMutated,
        bool ModelStarted,
        bool GameAccessed,
        bool ProductionProviderRegistered,
        string? FailureStage,
        int? FailureNativeCode);

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--unit" })) return Unit();
        if (args.SequenceEqual(new[] { "--escape-canary" })) return 17;
        if (args.Length == 2 && args[0] == "--child" && int.TryParse(args[1], out var port) && port is > 0 and <= 65535)
            return Child(port);
        if (args.Length == 6 && args[0] == "--network-trial")
            return Parent(args[1], args[2], args[3], args[4], args[5]);
        Console.Error.WriteLine("EXPLICIT_TEST_MODE_REQUIRED");
        return 64;
    }

    private static int Unit()
    {
        var pass = 0;
        var validChild = new ChildEvidence(ChildSchema, ContractDigest, "S-1-15-2-1", 0, false, false, 0, 1,
            "TIMEOUT", null, true, true, true, true, 5);
        ValidateChild(validChild); pass++;
        foreach (var bad in new[]
        {
            validChild with { CapabilityCount = 1 },
            validChild with { LoopbackExemptDuring = true },
            validChild with { DiagnoseHasRequiredCapability = true },
            validChild with { DiagnoseInfoReturn = 5 },
            validChild with { DiagnoseInfoType = 0 },
            validChild with { DiagnoseInfoType = 2 },
            validChild with { SocketOutcome = "CONNECTED" },
            validChild with { ReadAllowed = false },
            validChild with { ReadDenied = false },
            validChild with { WriteDenied = false },
            validChild with { ChildDenied = false },
            validChild with { ChildErrorCode = 2 },
            validChild with { PackageSid = "S-1-5-18" },
            validChild with { ContractDigestSha256 = new string('0', 64) }
        })
        { MustRefuse(() => ValidateChild(bad)); pass++; }

        var raw = JsonSerializer.SerializeToUtf8Bytes(validChild);
        _ = ParseChild(raw); pass++;
        var extra = JsonSerializer.Deserialize<Dictionary<string, object>>(raw)!; extra["Authority"] = true;
        MustRefuse(() => ParseChild(JsonSerializer.SerializeToUtf8Bytes(extra))); pass++;
        MustRefuse(() => ParseChild(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw)[..^1] + ",\"Schema\":\"x\"}"))); pass++;
        MustRefuse(() => ParseChild([])); pass++;
        MustRefuse(() => ParseChild(new byte[8193])); pass++;

        var token = new TokenObservation(true, 0, true, true, false, true);
        var validProof = new ProofEvidence(ProofSchema, "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", new string('a', 40), Predecessor,
            ExactNativeBoundaryBlob, ContractDigest, "Matawaka.Workbench.AppContainerProbe.test", validChild.PackageSid,
            new string('b', 64), token, true, false, false, true, false, validChild, true, true, 0, true, true, true,
            true, false, false, false, false, false, false, false, null, null);
        ValidateProof(validProof); pass++;
        foreach (var bad in new[]
        {
            validProof with { LoopbackExemptBefore = true, OsNetworkIsolationProven = false },
            validProof with { LoopbackExemptAfter = null, OsNetworkIsolationProven = false },
            validProof with { Token = token with { Capabilities = 1 }, OsNetworkIsolationProven = false },
            validProof with { PackageSid = "S-1-15-2-9", OsNetworkIsolationProven = false },
            validProof with { Child = validChild with { PackageSid = "S-1-15-2-9" }, OsNetworkIsolationProven = false },
            validProof with { UnexpectedIsolatedConnectionObserved = true, OsNetworkIsolationProven = false },
            validProof with { SocketTimeoutPromotedToProof = true, OsNetworkIsolationProven = false },
            validProof with { ProfileRemoved = false, CleanupSucceeded = false, OsNetworkIsolationProven = false },
            validProof with { ProductionProviderRegistered = true, OsNetworkIsolationProven = false }
        })
        { MustRefuse(() => ValidateProof(bad)); pass++; }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "WINDOWS_NETWORK_PROOF_V2_PURE_CONTROLS_PASS", passed = pass, nativeCalls = false }));
        return 0;
    }

    private static void MustRefuse(Action action)
    {
        bool refused = false;
        try { action(); }
        catch (Exception e) when (e is BoundaryFailure or InvalidDataException or JsonException or InvalidOperationException) { refused = true; }
        NativeBoundary.Need(refused, "HOSTILE_CASE_ACCEPTED");
    }

    private static void ValidateChild(ChildEvidence c)
    {
        NativeBoundary.Need(c.Schema == ChildSchema, "CHILD_SCHEMA");
        NativeBoundary.Need(c.ContractDigestSha256 == ContractDigest, "CHILD_CONTRACT_BINDING");
        NativeBoundary.Need(c.PackageSid.StartsWith("S-1-15-2-", StringComparison.Ordinal), "CHILD_PACKAGE_SID");
        NativeBoundary.Need(c.CapabilityCount == 0, "CHILD_CAPABILITY_COUNT");
        NativeBoundary.Need(!c.LoopbackExemptDuring, "CHILD_LOOPBACK_EXEMPT");
        NativeBoundary.Need(!c.DiagnoseHasRequiredCapability, "WINDOWS_DIAG_CAPABILITY_PRESENT");
        NativeBoundary.Need(c.DiagnoseInfoReturn == 0, "WINDOWS_DIAG_INFO_FAILED");
        // Strict initial classification for 127.0.0.1. Do not broaden merely to obtain GREEN.
        NativeBoundary.Need(c.DiagnoseInfoType == 1, "WINDOWS_DIAG_NOT_PRIVATE_NETWORK_DENIAL");
        NativeBoundary.Need(c.SocketOutcome is "TIMEOUT" or "SOCKET_ERROR" or "REFUSED", "SOCKET_OUTCOME_INVALID");
        NativeBoundary.Need(c.SocketOutcome != "CONNECTED", "ISOLATED_SOCKET_CONNECTED");
        NativeBoundary.Need(c.ReadAllowed && c.ReadDenied && c.WriteDenied && c.ChildDenied, "CHILD_NONNETWORK_BOUNDARY_REFUSED");
        NativeBoundary.Need(c.ChildErrorCode is 5 or 367, "CHILD_PROCESS_DENIAL_REQUIRED");
    }

    private static ChildEvidence ParseChild(byte[] bytes)
    {
        NativeBoundary.Need(bytes.Length is > 0 and <= 8192, "CHILD_RECEIPT_SIZE");
        using var doc = JsonDocument.Parse(bytes);
        NativeBoundary.Need(doc.RootElement.ValueKind == JsonValueKind.Object, "CHILD_RECEIPT_OBJECT");
        string[] expected = ["Schema", "ContractDigestSha256", "PackageSid", "CapabilityCount", "LoopbackExemptDuring",
            "DiagnoseHasRequiredCapability", "DiagnoseInfoReturn", "DiagnoseInfoType", "SocketOutcome", "SocketCode",
            "ReadAllowed", "ReadDenied", "WriteDenied", "ChildDenied", "ChildErrorCode"];
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        NativeBoundary.Need(names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "CHILD_RECEIPT_KEYS");
        var value = JsonSerializer.Deserialize<ChildEvidence>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("CHILD_RECEIPT_DESERIALIZE");
        ValidateChild(value);
        return value;
    }

    private static void ValidateProof(ProofEvidence e)
    {
        NativeBoundary.Need(e.Schema == ProofSchema && e.Status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", "PROOF_STATUS");
        NativeBoundary.Need(IsHex(e.SourceHead, 40) && e.Predecessor == Predecessor && e.NativeBoundaryBlob == ExactNativeBoundaryBlob, "PROOF_SOURCE_BINDING");
        NativeBoundary.Need(e.ContractDigestSha256 == ContractDigest, "PROOF_CONTRACT_BINDING");
        NativeBoundary.Need(e.ProfileName.StartsWith(ProfilePrefix, StringComparison.Ordinal), "PROOF_PROFILE_NAME");
        NativeBoundary.Need(e.PackageSid.StartsWith("S-1-15-2-", StringComparison.Ordinal), "PROOF_PACKAGE_SID");
        NativeBoundary.Need(IsHex(e.ChildExecutableSha256, 64), "PROOF_CHILD_SHA");
        NativeBoundary.ValidateToken(e.Token ?? throw new BoundaryFailure("PROOF_TOKEN_ABSENT"));
        NativeBoundary.Need(e.JobLimitsVerified, "PROOF_JOB_LIMITS");
        NativeBoundary.Need(e.LoopbackExemptBefore == false && e.LoopbackExemptAfter == false, "PROOF_LOOPBACK_EXEMPTION");
        NativeBoundary.Need(e.NormalLoopbackControlConnected && !e.UnexpectedIsolatedConnectionObserved, "PROOF_LOOPBACK_OBSERVATION");
        var child = e.Child ?? throw new BoundaryFailure("PROOF_CHILD_ABSENT"); ValidateChild(child);
        NativeBoundary.Need(child.PackageSid == e.PackageSid, "PROOF_CHILD_PACKAGE_BINDING");
        NativeBoundary.Need(e.ProcessCreated && e.ProcessExited && e.ProcessExitCode == 0, "PROOF_PROCESS_TERMINAL");
        NativeBoundary.Need(e.ProfileCreated && e.ProfileRemoved && e.CleanupSucceeded, "PROOF_CLEANUP");
        NativeBoundary.Need(e.OsNetworkIsolationProven && !e.SocketTimeoutPromotedToProof, "PROOF_NETWORK_BOOLEAN_CONTRACT");
        NativeBoundary.Need(!e.NetworkIsolationConfigMutated && !e.FirewallRuleMutated && !e.GlobalWindowsPolicyMutated, "PROOF_GLOBAL_MUTATION");
        NativeBoundary.Need(!e.ModelStarted && !e.GameAccessed && !e.ProductionProviderRegistered, "PROOF_AUTHORITY_WIDENING");
    }

    private static int Child(int port)
    {
        bool readAllowed = false, readDenied = false, writeDenied = false, childDenied = false;
        int? childErrorCode = null, socketCode = null;
        string socketOutcome = "OTHER";
        try { readAllowed = File.ReadAllText("allow-read.txt") == "SYNTHETIC_ALLOWED"; } catch { }
        try { _ = File.ReadAllText("deny-read.txt"); } catch (UnauthorizedAccessException) { readDenied = true; }
        try { File.WriteAllText("deny-write.txt", "UNEXPECTED_WRITE"); } catch (UnauthorizedAccessException) { writeDenied = true; }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port)).WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
            socketOutcome = socket.Connected ? "CONNECTED" : "OTHER";
        }
        catch (SocketException e) { socketCode = e.ErrorCode; socketOutcome = e.SocketErrorCode == SocketError.ConnectionRefused ? "REFUSED" : "SOCKET_ERROR"; }
        catch (TimeoutException) { socketOutcome = "TIMEOUT"; }
        catch { socketOutcome = "OTHER"; }

        try
        {
            var start = new ProcessStartInfo { FileName = Path.Combine(Environment.CurrentDirectory, "Workbench.AppContainerProbe.exe"), UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--escape-canary");
            using var process = Process.Start(start);
            if (process is not null && !process.WaitForExit(1000)) { process.Kill(); process.WaitForExit(1000); }
        }
        catch (System.ComponentModel.Win32Exception e) { childErrorCode = e.NativeErrorCode; childDenied = e.NativeErrorCode is 5 or 367; }

        string packageSid = CurrentPackageSid(out int capabilityCount);
        bool exempt = IsLoopbackExempt(packageSid);
        uint hasRequired = NetworkIsolationDiagnoseConnectFailure("127.0.0.1");
        uint infoReturn = NetworkIsolationDiagnoseConnectFailureAndGetInfo("127.0.0.1", out int infoType);
        var result = new ChildEvidence(ChildSchema, ContractDigest, packageSid, capabilityCount, exempt, hasRequired != 0,
            infoReturn, infoType, socketOutcome, socketCode, readAllowed, readDenied, writeDenied, childDenied, childErrorCode);
        Console.WriteLine(JsonSerializer.Serialize(result));
        try { ValidateChild(result); return 0; } catch { return 2; }
    }

    private static int Parent(string directory, string manifestPath, string evidencePath, string sourceHead, string authorization)
    {
        if (authorization != Authorization || !IsHex(sourceHead, 40)) return 65;
        string root = NativeBoundary.ValidateRoot(directory);
        string evidenceFull = Path.GetFullPath(evidencePath);
        NativeBoundary.Need(!File.Exists(evidenceFull), "CREATE_ONLY_EVIDENCE_REQUIRED");
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(evidenceFull)!, "NETWORK-PROOF-ATTEMPT.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(marker, new { schema = "matawaka.workbench-network-proof-attempt/v0.1", sourceHead, authorization = "ISSUE_105", modelAuthorized = false, target = "127.0.0.1" });

        using var manifestDoc = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes = manifestDoc.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var boundary = new NativeBoundary();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        string status = "FAIL_CLOSED", stage = "START", profileName = "", packageSid = "", childSha = "";
        int? nativeCode = null; ChildEvidence? child = null;
        bool control = false, unexpected = false; bool? exemptBefore = null, exemptAfter = null;
        bool osProof = false;
        try
        {
            stage = "PREPARE"; boundary.Prepare(root, hashes);
            var identity = BoundaryIdentity(boundary);
            profileName = identity.ProfileName;
            packageSid = identity.PackageSid;
            var packageSidPtr = identity.PackageSidPtr;
            childSha = Hash(File.ReadAllBytes(Path.Combine(root, "Workbench.AppContainerProbe.exe")));
            stage = "LOOPBACK_CONFIG_BEFORE"; exemptBefore = IsLoopbackExempt(packageSidPtr); NativeBoundary.Need(exemptBefore == false, "LOOPBACK_EXEMPT_BEFORE");
            stage = "LOOPBACK_CONTROL"; listener.Start(1); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var normal = new TcpClient()) { normal.Connect(IPAddress.Loopback, port); using var accepted = listener.AcceptTcpClient(); control = accepted.Connected; }
            NativeBoundary.Need(control, "NORMAL_LOOPBACK_CONTROL_FAILED");
            stage = "NATIVE_START"; boundary.Start(root, port);
            var outputTask = Task.Run(() => ReadBounded(boundary.Output)); var errorTask = Task.Run(() => ReadBounded(boundary.Error));
            stage = "WAIT_CHILD"; NativeBoundary.Need(boundary.Wait(10000), "CHILD_TIMEOUT_PARENT_WAIT");
            unexpected = listener.Pending();
            var outBytes = outputTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _ = errorTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            child = ParseChild(outBytes);
            NativeBoundary.Need(boundary.ExitCode == 0, "CHILD_EXIT_NONZERO");
            NativeBoundary.Need(!unexpected, "ISOLATED_CONNECTION_ACCEPTABLE_PATH_OBSERVED");
            NativeBoundary.Need(boundary.Token is not null, "TOKEN_EVIDENCE_ABSENT"); NativeBoundary.ValidateToken(boundary.Token!);
            NativeBoundary.Need(boundary.JobLimitsVerified, "JOB_EVIDENCE_ABSENT");
            NativeBoundary.Need(child.PackageSid == packageSid, "CHILD_PROFILE_SID_MISMATCH");
            stage = "LOOPBACK_CONFIG_AFTER"; exemptAfter = IsLoopbackExempt(packageSidPtr); NativeBoundary.Need(exemptAfter == false, "LOOPBACK_EXEMPT_AFTER");
            osProof = child.CapabilityCount == 0 && !child.LoopbackExemptDuring && !child.DiagnoseHasRequiredCapability && child.DiagnoseInfoReturn == 0 && child.DiagnoseInfoType == 1 && child.SocketOutcome != "CONNECTED" && !unexpected;
            NativeBoundary.Need(osProof, "OS_NETWORK_ISOLATION_EVIDENCE_INCOMPLETE");
            status = "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN"; stage = "COMPLETE";
        }
        catch (BoundaryFailure e) { status = "FAIL_CLOSED"; stage = e.Message; nativeCode = e.NativeCode; }
        catch (Exception e) { status = "FAIL_CLOSED"; stage = "MANAGED_" + e.GetType().Name; }
        finally { listener.Stop(); boundary.Dispose(); }

        if (!boundary.CleanupSucceeded) { status = "FAIL_CLOSED"; stage = "CLEANUP_INCOMPLETE"; osProof = false; }
        var result = new ProofEvidence(ProofSchema, status, sourceHead, Predecessor, ExactNativeBoundaryBlob, ContractDigest,
            profileName, packageSid, childSha, boundary.Token, boundary.JobLimitsVerified, exemptBefore, exemptAfter, control,
            unexpected, child, boundary.ProcessCreated, boundary.ProcessExited, boundary.ExitCode, boundary.ProfileCreated,
            boundary.ProfileRemoved, boundary.CleanupSucceeded, osProof && status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN",
            false, false, false, false, false, false, false, stage == "COMPLETE" ? null : stage, nativeCode);
        if (result.Status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN") ValidateProof(result);
        using (var file = new FileStream(evidenceFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN" ? 0 : 3;
    }

    private static (string ProfileName, string PackageSid, nint PackageSidPtr) BoundaryIdentity(NativeBoundary boundary)
    {
        var type = typeof(NativeBoundary);
        var profileField = type.GetField("profile", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new BoundaryFailure("PROFILE_REFLECTION_BINDING");
        var sidField = type.GetField("packageSid", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new BoundaryFailure("PACKAGE_SID_REFLECTION_BINDING");
        var profile = profileField.GetValue(boundary) as string ?? throw new BoundaryFailure("PROFILE_NAME_ABSENT");
        var raw = sidField.GetValue(boundary) ?? throw new BoundaryFailure("PACKAGE_SID_ABSENT");
        nint ptr = raw is IntPtr p ? p : throw new BoundaryFailure("PACKAGE_SID_SHAPE");
        NativeBoundary.Need(ptr != 0, "PACKAGE_SID_ABSENT");
        return (profile, new SecurityIdentifier(ptr).Value, ptr);
    }

    private static string CurrentPackageSid(out int capabilityCount)
    {
        Win(OpenProcessToken(GetCurrentProcess(), 8, out nint token), "CHILD_TOKEN_QUERY");
        try
        {
            using var sid = QueryToken(token, 31); using var caps = QueryToken(token, 30);
            nint sidPtr = Marshal.ReadIntPtr(sid.Pointer); NativeBoundary.Need(sidPtr != 0, "CHILD_PACKAGE_SID_ABSENT");
            capabilityCount = Marshal.ReadInt32(caps.Pointer); NativeBoundary.Need(capabilityCount >= 0, "CHILD_CAPABILITY_SHAPE");
            return new SecurityIdentifier(sidPtr).Value;
        }
        finally { CloseHandle(token); }
    }

    private static bool IsLoopbackExempt(string sid)
    {
        Win(ConvertStringSidToSid(sid, out nint ptr), "LOOPBACK_SID_PARSE");
        try { return IsLoopbackExempt(ptr); } finally { LocalFree(ptr); }
    }

    private static bool IsLoopbackExempt(nint sid)
    {
        uint rc = NetworkIsolationGetAppContainerConfig(out uint count, out nint entries);
        if (rc != 0) throw new BoundaryFailure("GET_LOOPBACK_CONFIG_FAILED", unchecked((int)rc));
        try
        {
            int size = Marshal.SizeOf<SidAndAttributes>();
            for (uint i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<SidAndAttributes>(entries + checked((int)i * size));
                if (item.Sid != 0 && EqualSid(item.Sid, sid)) return true;
            }
            return false;
        }
        finally
        {
            if (entries != 0)
            {
                var heap = GetProcessHeap(); int size = Marshal.SizeOf<SidAndAttributes>();
                for (uint i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<SidAndAttributes>(entries + checked((int)i * size));
                    if (item.Sid != 0) _ = HeapFree(heap, 0, item.Sid);
                }
                _ = HeapFree(heap, 0, entries);
            }
        }
    }

    private static byte[] ReadBounded(Stream stream)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[1024];
        while (true) { int n = stream.Read(chunk); if (n == 0) break; NativeBoundary.Need(buffer.Length + n <= 8192, "CHILD_OUTPUT_LIMIT"); buffer.Write(chunk, 0, n); }
        return buffer.ToArray();
    }

    private static TokenBuffer QueryToken(nint token, int kind)
    {
        bool initial = GetTokenInformation(token, kind, 0, 0, out uint needed); int error = Marshal.GetLastWin32Error();
        if (initial || error != 122 || needed is 0 or > 16384) throw new BoundaryFailure("CHILD_TOKEN_SIZE_QUERY_" + kind, error);
        var buffer = new TokenBuffer((int)needed);
        try { Win(GetTokenInformation(token, kind, buffer.Pointer, needed, out uint written), "CHILD_TOKEN_QUERY"); NativeBoundary.Need(written <= needed, "CHILD_TOKEN_SIZE_CHANGED"); return buffer; }
        catch { buffer.Dispose(); throw; }
    }

    private sealed class TokenBuffer(int size) : IDisposable
    {
        internal nint Pointer { get; } = Marshal.AllocHGlobal(size);
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }

    private static void Win(bool ok, string stage)
    {
        if (!ok) throw new BoundaryFailure(stage, Marshal.GetLastWin32Error());
    }

    private static bool IsHex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailure(string serverName);
    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailureAndGetInfo(string serverName, out int errorType);
    [DllImport("Firewallapi.dll")] private static extern uint NetworkIsolationGetAppContainerConfig(out uint count, out nint appContainerSids);
    [DllImport("kernel32.dll")] private static extern nint GetProcessHeap();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HeapFree(nint heap, uint flags, nint memory);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int kind, nint data, uint size, out uint returned);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSidToSid(string text, out nint sid);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualSid(nint a, nint b);
}
