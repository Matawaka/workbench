using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Workbench.NetworkIsolationQualification;

internal static class Program
{
    private const string Authorization = "ISSUE-105-CURRENT-EXPLICIT-DEVELOPMENT";
    private const string Predecessor = "c4c0d2287ae944fd65d046e2ce6451d6e9d92d4a";
    private const string ChildSchema = "matawaka.workbench-appcontainer-network-child/v0.1";
    private const string EvidenceSchema = "matawaka.workbench-windows-network-isolation-proof/v0.1";
    private const string ContractJson = "{\"schema\":\"matawaka.workbench-windows-host-profile/v0.1\",\"appContainerCapabilities\":0,\"loopbackExemptionRequired\":false,\"childProcessRestricted\":true,\"jobActiveProcessLimit\":1,\"jobProcessMemoryBytes\":536870912,\"killOnJobClose\":true,\"dieOnUnhandledException\":true,\"networkTarget\":\"127.0.0.1\",\"networkTargetClass\":\"PRIVATE_NETWORK\",\"socketTimeoutMilliseconds\":1000}";
    internal static readonly string ContractDigest = Hash(Encoding.UTF8.GetBytes(ContractJson));

    private sealed record ChildEvidence(
        string Schema,
        string ContractDigestSha256,
        bool DiagnoseHasRequiredCapability,
        uint DiagnoseInfoReturn,
        int DiagnoseInfoType,
        string SocketOutcome,
        int? SocketCode);

    private sealed record TokenObservation(
        bool AppContainer,
        int Capabilities,
        bool PackageMatches,
        bool LowIntegrity,
        bool Elevated,
        bool InOwnedJob);

    private sealed record TrialEvidence(
        string Schema,
        string Status,
        string SourceHead,
        string Predecessor,
        string ContractDigestSha256,
        string ProfileName,
        string PackageSid,
        string ChildExecutableSha256,
        TokenObservation? Token,
        bool JobLimitsVerified,
        bool LoopbackExemptBefore,
        bool LoopbackExemptAfter,
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

    private sealed class BoundaryFailure(string stage, int nativeCode = 0) : Exception(stage)
    {
        internal int NativeCode { get; } = nativeCode;
    }

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--unit" })) return Unit();
        if (args.Length == 3 && args[0] == "--child" && int.TryParse(args[1], out var port)) return Child(port, args[2]);
        if (args.Length == 4 && args[0] == "--parent") return Parent(args[1], args[2], args[3]);
        Console.Error.WriteLine("EXPLICIT_MODE_REQUIRED");
        return 64;
    }

    private static int Unit()
    {
        var pass = 0;
        var validChild = new ChildEvidence(ChildSchema, ContractDigest, false, 0, 1, "TIMEOUT", null);
        ValidateChild(validChild); pass++;
        foreach (var bad in new[]
        {
            validChild with { DiagnoseHasRequiredCapability = true },
            validChild with { DiagnoseInfoReturn = 5 },
            validChild with { DiagnoseInfoType = 0 },
            validChild with { DiagnoseInfoType = 2 },
            validChild with { ContractDigestSha256 = new string('0', 64) },
            validChild with { SocketOutcome = "CONNECTED" }
        })
        {
            MustRefuse(() => ValidateChild(bad)); pass++;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(validChild);
        _ = ParseChild(json); pass++;
        var node = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        node["Extra"] = true;
        MustRefuse(() => ParseChild(JsonSerializer.SerializeToUtf8Bytes(node))); pass++;
        MustRefuse(() => ParseChild(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json)[..^1] + ",\"Schema\":\"x\"}"))); pass++;

        var validTrial = new TrialEvidence(EvidenceSchema, "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", new string('a', 40), Predecessor,
            ContractDigest, "Matawaka.NetworkProof.test", "S-1-15-2-1", new string('b', 64),
            new TokenObservation(true, 0, true, true, false, true), true, false, false, true, false, validChild,
            true, true, 0, true, true, true, true, false, false, false, false, false, false, false, null, null);
        ValidateProof(validTrial); pass++;
        foreach (var bad in new[]
        {
            validTrial with { LoopbackExemptBefore = true, OsNetworkIsolationProven = false },
            validTrial with { LoopbackExemptAfter = true, OsNetworkIsolationProven = false },
            validTrial with { Token = validTrial.Token! with { Capabilities = 1 }, OsNetworkIsolationProven = false },
            validTrial with { UnexpectedIsolatedConnectionObserved = true, OsNetworkIsolationProven = false },
            validTrial with { Child = validChild with { DiagnoseInfoType = 0 }, OsNetworkIsolationProven = false },
            validTrial with { ProfileRemoved = false, CleanupSucceeded = false, OsNetworkIsolationProven = false },
            validTrial with { SocketTimeoutPromotedToProof = true, OsNetworkIsolationProven = false },
            validTrial with { PackageSid = "S-1-15-2-2", Token = validTrial.Token! with { PackageMatches = false }, OsNetworkIsolationProven = false }
        })
        {
            MustRefuse(() => ValidateProof(bad)); pass++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "WINDOWS_NETWORK_ISOLATION_PURE_CONTROLS_PASS", passed = pass, nativeCalls = false }));
        return 0;
    }

    private static void MustRefuse(Action action)
    {
        var refused = false;
        try { action(); }
        catch (Exception e) when (e is BoundaryFailure or InvalidDataException or JsonException) { refused = true; }
        Need(refused, "HOSTILE_CASE_ACCEPTED");
    }

    private static void ValidateChild(ChildEvidence child)
    {
        Need(child.Schema == ChildSchema, "CHILD_SCHEMA");
        Need(child.ContractDigestSha256 == ContractDigest, "CHILD_CONTRACT_BINDING");
        Need(!child.DiagnoseHasRequiredCapability, "WINDOWS_DIAG_CAPABILITY_PRESENT");
        Need(child.DiagnoseInfoReturn == 0, "WINDOWS_DIAG_INFO_FAILED");
        // Strict first contract for 127.0.0.1: do not broaden this merely to obtain GREEN.
        Need(child.DiagnoseInfoType == 1, "WINDOWS_DIAG_NOT_PRIVATE_NETWORK_DENIAL");
        Need(child.SocketOutcome is "TIMEOUT" or "SOCKET_ERROR" or "REFUSED", "SOCKET_OUTCOME_INVALID");
        Need(child.SocketOutcome != "CONNECTED", "ISOLATED_SOCKET_CONNECTED");
    }

    private static ChildEvidence ParseChild(byte[] bytes)
    {
        Need(bytes.Length is > 0 and <= 8192, "CHILD_RECEIPT_SIZE");
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        Need(root.ValueKind == JsonValueKind.Object, "CHILD_RECEIPT_OBJECT");
        string[] expected = ["Schema", "ContractDigestSha256", "DiagnoseHasRequiredCapability", "DiagnoseInfoReturn", "DiagnoseInfoType", "SocketOutcome", "SocketCode"];
        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
        Need(names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "CHILD_RECEIPT_KEYS");
        var child = JsonSerializer.Deserialize<ChildEvidence>(root.GetRawText()) ?? throw new InvalidDataException("CHILD_RECEIPT_DESERIALIZE");
        ValidateChild(child);
        return child;
    }

    private static void ValidateProof(TrialEvidence e)
    {
        Need(e.Schema == EvidenceSchema && e.Status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", "PROOF_STATUS");
        Need(e.ContractDigestSha256 == ContractDigest, "PROOF_CONTRACT_BINDING");
        Need(IsHex(e.SourceHead, 40) && e.Predecessor == Predecessor, "PROOF_SOURCE_BINDING");
        Need(IsHex(e.ChildExecutableSha256, 64), "PROOF_PROCESS_IMAGE_SHA");
        Need(e.ProfileName.StartsWith("Matawaka.NetworkProof.", StringComparison.Ordinal) && e.PackageSid.StartsWith("S-1-15-2-", StringComparison.Ordinal), "PROOF_PROFILE_BINDING");
        Need(e.Token is { AppContainer: true, Capabilities: 0, PackageMatches: true, LowIntegrity: true, Elevated: false, InOwnedJob: true }, "PROOF_TOKEN_BINDING");
        Need(e.JobLimitsVerified, "PROOF_JOB_BINDING");
        Need(!e.LoopbackExemptBefore && !e.LoopbackExemptAfter, "PROOF_LOOPBACK_EXEMPTION_PRESENT");
        Need(e.NormalLoopbackControlConnected && !e.UnexpectedIsolatedConnectionObserved, "PROOF_LOOPBACK_OBSERVATION");
        Need(e.Child is not null, "PROOF_CHILD_ABSENT");
        ValidateChild(e.Child!);
        Need(e.ProcessCreated && e.ProcessExited && e.ProcessExitCode == 0, "PROOF_PROCESS_TERMINAL");
        Need(e.ProfileCreated && e.ProfileRemoved && e.CleanupSucceeded, "PROOF_CLEANUP");
        Need(e.OsNetworkIsolationProven && !e.SocketTimeoutPromotedToProof, "PROOF_BOOLEAN_CONTRACT");
        Need(!e.NetworkIsolationConfigMutated && !e.FirewallRuleMutated && !e.GlobalWindowsPolicyMutated, "PROOF_GLOBAL_MUTATION");
        Need(!e.ModelStarted && !e.GameAccessed && !e.ProductionProviderRegistered, "PROOF_AUTHORITY_WIDENING");
    }

    private static int Child(int port, string expectedContractDigest)
    {
        if (port is <= 0 or > 65535 || expectedContractDigest != ContractDigest) return 65;
        string outcome = "OTHER";
        int? socketCode = null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port)).WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
            outcome = socket.Connected ? "CONNECTED" : "OTHER";
        }
        catch (SocketException e)
        {
            socketCode = e.ErrorCode;
            outcome = e.SocketErrorCode == SocketError.ConnectionRefused ? "REFUSED" : "SOCKET_ERROR";
        }
        catch (TimeoutException) { outcome = "TIMEOUT"; }
        catch { outcome = "OTHER"; }

        uint hasCapability = NetworkIsolationDiagnoseConnectFailure("127.0.0.1");
        uint infoReturn = NetworkIsolationDiagnoseConnectFailureAndGetInfo("127.0.0.1", out int infoType);
        var evidence = new ChildEvidence(ChildSchema, ContractDigest, hasCapability != 0, infoReturn, infoType, outcome, socketCode);
        Console.WriteLine(JsonSerializer.Serialize(evidence));
        try { ValidateChild(evidence); return 0; }
        catch { return 2; }
    }

    private static int Parent(string sourceHead, string evidencePath, string authorization)
    {
        if (authorization != Authorization || !IsHex(sourceHead, 40)) return 65;
        var evidenceFull = Path.GetFullPath(evidencePath);
        if (File.Exists(evidenceFull)) throw new InvalidDataException("CREATE_ONLY_EVIDENCE_REQUIRED");
        Directory.CreateDirectory(Path.GetDirectoryName(evidenceFull)!);

        string status = "FAIL_CLOSED";
        string? failure = null;
        int? failureNative = null;
        string profileName = "";
        string packageSid = "";
        string childSha = "";
        TokenObservation? token = null;
        ChildEvidence? child = null;
        bool job = false, exemptBefore = true, exemptAfter = true, control = false, unexpected = false;
        bool processCreated = false, processExited = false, profileCreated = false, profileRemoved = false, cleanup = false;
        uint? exitCode = null;
        bool osProof = false;

        var trialParent = Path.Combine(Path.GetTempPath(), "MatawakaNetworkProof-" + Guid.NewGuid().ToString("N"));
        var trialRoot = Path.Combine(trialParent, "trial");
        Directory.CreateDirectory(trialRoot);
        var sourceExe = Environment.ProcessPath ?? throw new InvalidDataException("PROCESS_PATH_UNAVAILABLE");
        var childExe = Path.Combine(trialRoot, "Matawaka.NetworkProof.exe");
        File.Copy(sourceExe, childExe, overwrite: false);
        childSha = Hash(File.ReadAllBytes(childExe));

        var boundary = new NativeBoundary();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            boundary.Prepare(trialRoot, childExe);
            profileName = boundary.ProfileName;
            packageSid = boundary.PackageSid;
            profileCreated = boundary.ProfileCreated;
            exemptBefore = boundary.IsLoopbackExempt();
            Need(!exemptBefore, "LOOPBACK_EXEMPT_BEFORE");

            listener.Start(1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var normal = new TcpClient())
            {
                normal.Connect(IPAddress.Loopback, port);
                using var accepted = listener.AcceptTcpClient();
                control = accepted.Connected;
            }
            Need(control, "NORMAL_LOOPBACK_CONTROL_FAILED");

            boundary.Start(trialRoot, childExe, port, ContractDigest);
            processCreated = boundary.ProcessCreated;
            token = boundary.Token;
            job = boundary.JobLimitsVerified;
            var outputTask = Task.Run(() => ReadBounded(boundary.Output));
            var errorTask = Task.Run(() => ReadBounded(boundary.Error));
            Need(boundary.Wait(10000), "CHILD_TIMEOUT_PARENT_WAIT");
            processExited = boundary.ProcessExited;
            exitCode = boundary.ExitCode;
            unexpected = listener.Pending();
            var outBytes = outputTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _ = errorTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (outBytes.Length > 0) child = ParseChild(outBytes);
            Need(exitCode == 0, "CHILD_EXIT_NONZERO");
            Need(!unexpected, "ISOLATED_CONNECTION_ACCEPTABLE_PATH_OBSERVED");
            exemptAfter = boundary.IsLoopbackExempt();
            Need(!exemptAfter, "LOOPBACK_EXEMPT_AFTER");
            Need(token is { AppContainer: true, Capabilities: 0, PackageMatches: true, LowIntegrity: true, Elevated: false, InOwnedJob: true }, "TOKEN_BOUNDARY_REFUSED");
            Need(job, "JOB_BOUNDARY_REFUSED");
            osProof = child is not null && !child.DiagnoseHasRequiredCapability && child.DiagnoseInfoReturn == 0 && child.DiagnoseInfoType == 1 && !exemptBefore && !exemptAfter && !unexpected;
            Need(osProof, "OS_NETWORK_ISOLATION_EVIDENCE_INCOMPLETE");
            status = "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN";
        }
        catch (BoundaryFailure e) { failure = e.Message; failureNative = e.NativeCode == 0 ? null : e.NativeCode; }
        catch (Exception e) { failure = "MANAGED_" + e.GetType().Name; }
        finally
        {
            listener.Stop();
            boundary.Dispose();
            processCreated = boundary.ProcessCreated;
            processExited = boundary.ProcessExited;
            exitCode = boundary.ExitCode;
            profileCreated = boundary.ProfileCreated;
            profileRemoved = boundary.ProfileRemoved;
            cleanup = boundary.CleanupSucceeded;
            try { Directory.Delete(trialParent, recursive: true); } catch { }
        }

        if (!cleanup) { status = "FAIL_CLOSED"; failure ??= "CLEANUP_INCOMPLETE"; osProof = false; }
        var result = new TrialEvidence(EvidenceSchema, status, sourceHead, Predecessor, ContractDigest, profileName, packageSid, childSha,
            token, job, exemptBefore, exemptAfter, control, unexpected, child, processCreated, processExited, exitCode,
            profileCreated, profileRemoved, cleanup, osProof, false, false, false, false, false, false, false, failure, failureNative);
        if (status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN") ValidateProof(result);
        using (var file = new FileStream(evidenceFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(JsonSerializer.Serialize(result));
        return status == "OS_NETWORK_PATH_NOT_AUTHORIZED_PROVEN" ? 0 : 3;
    }

    private static byte[] ReadBounded(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            int n = stream.Read(chunk, 0, chunk.Length);
            if (n == 0) break;
            Need(buffer.Length + n <= 8192, "CHILD_OUTPUT_LIMIT");
            buffer.Write(chunk, 0, n);
        }
        return buffer.ToArray();
    }

    private sealed class NativeBoundary : IDisposable
    {
        private nint packageSid, process, thread, job;
        private nint parentOut, childOut, parentErr, childErr, childIn, parentIn;
        private string? profileName, root;
        private bool profileOwned, disposed;
        internal bool ProfileRemoved { get; private set; }
        internal bool ProcessExited { get; private set; }
        internal bool ProcessCreated { get; private set; }
        internal uint? ExitCode { get; private set; }
        internal TokenObservation? Token { get; private set; }
        internal bool JobLimitsVerified { get; private set; }
        internal string ProfileName => profileName ?? throw new BoundaryFailure("PROFILE_NAME_ABSENT");
        internal string PackageSid => packageSid == 0 ? throw new BoundaryFailure("PACKAGE_SID_ABSENT") : SidToString(packageSid);
        internal Stream Output { get; private set; } = Stream.Null;
        internal Stream Error { get; private set; } = Stream.Null;
        internal bool ProfileCreated => profileOwned;
        internal bool CleanupSucceeded => (!ProfileOwnedOrCreated() || ProfileRemoved) && (!ProcessCreated || ProcessExited);
        private bool ProfileOwnedOrCreated() => profileOwned;

        internal void Prepare(string trialRoot, string childExe)
        {
            Need(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "WINDOWS_X64_REQUIRED");
            root = Path.GetFullPath(trialRoot);
            Need(new DirectoryInfo(root).Name == "trial" && File.Exists(childExe), "TRIAL_SHAPE");
            Need((File.GetAttributes(childExe) & FileAttributes.ReparsePoint) == 0, "CHILD_REPARSE_REFUSED");
            profileName = "Matawaka.NetworkProof." + Guid.NewGuid().ToString("N");
            int hr = CreateAppContainerProfile(profileName, "Matawaka network proof", "Disposable zero-capability proof profile", 0, 0, out packageSid);
            if (hr != 0) throw new BoundaryFailure("CREATE_PROFILE_REFUSED", hr);
            profileOwned = true;
            Need(packageSid != 0, "PACKAGE_SID_ABSENT");
            ApplyTrialAcl(root, childExe, packageSid);
        }

        internal bool IsLoopbackExempt()
        {
            Need(packageSid != 0, "PACKAGE_SID_ABSENT");
            uint rc = NetworkIsolationGetAppContainerConfig(out uint count, out nint entries);
            if (rc != 0) throw new BoundaryFailure("GET_LOOPBACK_CONFIG_FAILED", unchecked((int)rc));
            try
            {
                int size = Marshal.SizeOf<SidAndAttributes>();
                for (uint i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<SidAndAttributes>(entries + checked((int)i * size));
                    if (item.Sid != 0 && EqualSid(item.Sid, packageSid)) return true;
                }
                return false;
            }
            finally
            {
                if (entries != 0)
                {
                    var heap = GetProcessHeap();
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

        internal void Start(string trialRoot, string childExe, int port, string contractDigest)
        {
            Need(profileOwned && process == 0 && root == Path.GetFullPath(trialRoot), "START_STATE");
            var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
            Win(CreatePipe(out childIn, out parentIn, ref sa, 0), "STDIN_PIPE");
            Win(CreatePipe(out parentOut, out childOut, ref sa, 0), "STDOUT_PIPE");
            Win(CreatePipe(out parentErr, out childErr, ref sa, 0), "STDERR_PIPE");
            foreach (var h in new[] { parentIn, parentOut, parentErr }) Win(SetHandleInformation(h, 1, 0), "PIPE_INHERITANCE");
            using var attrs = new AttributeList(3);
            attrs.Add(0x20009, new SecurityCapabilities { AppContainerSid = packageSid, Capabilities = 0, Count = 0, Reserved = 0 });
            attrs.AddHandles(0x20002, [childIn, childOut, childErr]);
            attrs.Add(0x2000e, 1u);
            var si = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    StdInput = childIn, StdOutput = childOut, StdError = childErr
                },
                Attributes = attrs.Pointer
            };
            var cmd = new StringBuilder('"' + childExe + '"' + " --child " + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + contractDigest);
            string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int folderHr = GetAppContainerFolderPath(PackageSid, out nint folderPtr);
            if (folderHr != 0) throw new BoundaryFailure("CONTAINER_FOLDER_QUERY", folderHr);
            string containerFolder;
            try { containerFolder = Marshal.PtrToStringUni(folderPtr) ?? throw new BoundaryFailure("CONTAINER_FOLDER_ABSENT"); }
            finally { Marshal.FreeCoTaskMem(folderPtr); }
            var envVars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["COMPlus_EnableDiagnostics"] = "0", ["DOTNET_EnableDiagnostics"] = "0", ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
                ["SystemRoot"] = systemRoot, ["WINDIR"] = systemRoot, ["SystemDrive"] = Path.GetPathRoot(systemRoot)!.TrimEnd('\\'),
                ["USERPROFILE"] = containerFolder, ["APPDATA"] = containerFolder, ["LOCALAPPDATA"] = containerFolder,
                ["TEMP"] = containerFolder, ["TMP"] = containerFolder
            };
            var environment = string.Join('\0', envVars.Select(p => p.Key + "=" + p.Value)) + "\0\0";
            nint env = Marshal.StringToHGlobalUni(environment);
            ProcessInformation pi;
            try
            {
                Win(CreateProcess(childExe, cmd, 0, 0, true, 0x00080000 | 0x00000400 | 0x00000004 | 0x00000008,
                    env, root, ref si, out pi), "CREATE_APPCONTAINER_PROCESS_REFUSED");
            }
            finally { Marshal.FreeHGlobal(env); }
            process = pi.Process; thread = pi.Thread; ProcessCreated = true;
            job = CreateJobObject(0, null); Win(job != 0, "CREATE_JOB");
            var limits = new ExtendedLimits
            {
                Basic = new BasicLimits { Flags = 0x8 | 0x100 | 0x400 | 0x2000, ActiveProcesses = 1 },
                ProcessMemory = (nuint)(512 * 1024 * 1024)
            };
            nint limitsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimits>());
            try
            {
                Marshal.StructureToPtr(limits, limitsPtr, false);
                Win(SetInformationJobObject(job, 9, limitsPtr, (uint)Marshal.SizeOf<ExtendedLimits>()), "SET_JOB_LIMITS");
                Win(QueryInformationJobObject(job, 9, limitsPtr, (uint)Marshal.SizeOf<ExtendedLimits>(), out uint returned), "QUERY_JOB_LIMITS");
                Need(returned == (uint)Marshal.SizeOf<ExtendedLimits>(), "JOB_LIMITS_SHAPE");
                var observed = Marshal.PtrToStructure<ExtendedLimits>(limitsPtr);
                Need(observed.Basic.Flags == limits.Basic.Flags && observed.Basic.ActiveProcesses == 1 && observed.ProcessMemory == limits.ProcessMemory, "JOB_LIMITS_REFUSED");
                JobLimitsVerified = true;
            }
            finally { Marshal.FreeHGlobal(limitsPtr); }
            Win(AssignProcessToJobObject(job, process), "ASSIGN_JOB_BEFORE_RESUME");
            Win(IsProcessInJob(process, job, out bool inJob), "QUERY_OWN_JOB");
            Win(OpenProcessToken(process, 8, out nint token), "QUERY_CHILD_TOKEN");
            try { Token = ObserveToken(token, inJob); }
            finally { CloseHandle(token); }
            Need(Token is { AppContainer: true, Capabilities: 0, PackageMatches: true, LowIntegrity: true, Elevated: false, InOwnedJob: true }, "TOKEN_BOUNDARY_REFUSED");
            var image = new StringBuilder(32768); uint length = (uint)image.Capacity;
            Win(QueryFullProcessImageName(process, 0, image, ref length), "PROCESS_IMAGE_QUERY");
            Need(string.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(childExe), StringComparison.OrdinalIgnoreCase), "PROCESS_IMAGE_MISMATCH");
            Close(ref parentIn); Close(ref childIn); Close(ref childOut); Close(ref childErr);
            Output = new FileStream(new SafeFileHandle(parentOut, true), FileAccess.Read, 4096, false); parentOut = 0;
            Error = new FileStream(new SafeFileHandle(parentErr, true), FileAccess.Read, 4096, false); parentErr = 0;
            Win(ResumeThread(thread) != uint.MaxValue, "RESUME_THREAD");
        }

        private TokenObservation ObserveToken(nint token, bool inJob)
        {
            using var app = QueryToken(token, 29);
            using var caps = QueryToken(token, 30);
            using var sid = QueryToken(token, 31);
            using var integrity = QueryToken(token, 25);
            using var elevated = QueryToken(token, 20);
            Win(ConvertStringSidToSid("S-1-16-4096", out nint low), "LOW_SID");
            try
            {
                return new TokenObservation(Marshal.ReadInt32(app.Pointer) == 1, Marshal.ReadInt32(caps.Pointer),
                    Marshal.ReadIntPtr(sid.Pointer) != 0 && EqualSid(Marshal.ReadIntPtr(sid.Pointer), packageSid),
                    Marshal.ReadIntPtr(integrity.Pointer) != 0 && EqualSid(Marshal.ReadIntPtr(integrity.Pointer), low),
                    Marshal.ReadInt32(elevated.Pointer) != 0, inJob);
            }
            finally { LocalFree(low); }
        }

        internal bool Wait(uint milliseconds)
        {
            Need(process != 0 && milliseconds <= 15000, "WAIT_BOUND");
            uint result = WaitForSingleObject(process, milliseconds);
            if (result == 0)
            {
                Win(GetExitCodeProcess(process, out uint code), "EXIT_CODE");
                ExitCode = code; ProcessExited = true; return true;
            }
            if (result == 258) return false;
            throw new BoundaryFailure("PROCESS_WAIT_FAILED", Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (process != 0 && !ProcessExited)
            {
                if (job != 0) _ = TerminateJobObject(job, 90);
                _ = TerminateProcess(process, 90);
                if (WaitForSingleObject(process, 5000) == 0) { ProcessExited = true; if (GetExitCodeProcess(process, out uint code)) ExitCode = code; }
            }
            Close(ref thread); Close(ref job);
            Output.Dispose(); Error.Dispose();
            Close(ref parentIn); Close(ref childIn); Close(ref parentOut); Close(ref childOut); Close(ref parentErr); Close(ref childErr);
            if (profileOwned && (!ProcessCreated || ProcessExited)) ProfileRemoved = DeleteAppContainerProfile(profileName!) == 0;
            Close(ref process);
            if (packageSid != 0) { FreeSid(packageSid); packageSid = 0; }
        }

        private static void ApplyTrialAcl(string root, string childExe, nint sid)
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new BoundaryFailure("OWNER_UNAVAILABLE");
            var package = new SecurityIdentifier(sid);
            var acl = new DirectoryInfo(root).GetAccessControl();
            acl.SetAccessRuleProtection(true, false);
            foreach (FileSystemAccessRule existing in acl.GetAccessRules(true, true, typeof(SecurityIdentifier))) acl.RemoveAccessRuleSpecific(existing);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(package, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
            var fileAcl = new FileInfo(childExe).GetAccessControl();
            fileAcl.SetAccessRuleProtection(true, false);
            foreach (FileSystemAccessRule existing in fileAcl.GetAccessRules(true, true, typeof(SecurityIdentifier))) fileAcl.RemoveAccessRuleSpecific(existing);
            fileAcl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            fileAcl.AddAccessRule(new FileSystemAccessRule(package, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            new FileInfo(childExe).SetAccessControl(fileAcl);
        }

        private static NativeMemory QueryToken(nint token, int kind)
        {
            if (kind == 20)
            {
                var b = new NativeMemory(sizeof(uint));
                try { Win(GetTokenInformation(token, kind, b.Pointer, sizeof(uint), out uint len), "TOKEN_ELEVATION_QUERY"); Need(len == sizeof(uint), "TOKEN_ELEVATION_SHAPE"); return b; }
                catch { b.Dispose(); throw; }
            }
            bool initial = GetTokenInformation(token, kind, 0, 0, out uint needed);
            int error = Marshal.GetLastWin32Error();
            if (initial || error != 122 || needed is 0 or > 16384) throw new BoundaryFailure("TOKEN_SIZE_QUERY_" + kind, error);
            var buffer = new NativeMemory((int)needed);
            try { Win(GetTokenInformation(token, kind, buffer.Pointer, needed, out uint written), "TOKEN_QUERY"); Need(written <= needed, "TOKEN_SIZE_CHANGED"); return buffer; }
            catch { buffer.Dispose(); throw; }
        }

        private sealed class NativeMemory(int size) : IDisposable
        {
            internal nint Pointer { get; } = Marshal.AllocHGlobal(size);
            public void Dispose() => Marshal.FreeHGlobal(Pointer);
        }

        private sealed class AttributeList : IDisposable
        {
            internal nint Pointer { get; }
            private readonly List<nint> values = [];
            internal AttributeList(int count)
            {
                nuint size = 0; _ = InitializeProcThreadAttributeList(0, count, 0, ref size);
                Need(Marshal.GetLastWin32Error() == 122 && size is > 0 and < 65536, "ATTRIBUTE_SIZE");
                Pointer = Marshal.AllocHGlobal((nint)size);
                try { Win(InitializeProcThreadAttributeList(Pointer, count, 0, ref size), "ATTRIBUTE_INIT"); }
                catch { Marshal.FreeHGlobal(Pointer); throw; }
            }
            internal void Add<T>(nuint key, T value) where T : struct
            {
                int n = Marshal.SizeOf<T>(); nint p = Marshal.AllocHGlobal(n); values.Add(p); Marshal.StructureToPtr(value, p, false);
                Win(UpdateProcThreadAttribute(Pointer, 0, key, p, (nuint)n, 0, 0), "ATTRIBUTE_SET");
            }
            internal void AddHandles(nuint key, nint[] handles)
            {
                nint p = Marshal.AllocHGlobal(handles.Length * IntPtr.Size); values.Add(p); Marshal.Copy(handles, 0, p, handles.Length);
                Win(UpdateProcThreadAttribute(Pointer, 0, key, p, (nuint)(handles.Length * IntPtr.Size), 0, 0), "HANDLE_ALLOWLIST");
            }
            public void Dispose() { DeleteProcThreadAttributeList(Pointer); foreach (var p in values) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(Pointer); }
        }

        private static string SidToString(nint sid)
        {
            Win(ConvertSidToStringSid(sid, out nint text), "SID_TO_STRING");
            try { return Marshal.PtrToStringUni(text) ?? throw new BoundaryFailure("SID_STRING_ABSENT"); }
            finally { LocalFree(text); }
        }

        private static void Close(ref nint handle) { if (handle != 0) { CloseHandle(handle); handle = 0; } }
    }

    private static bool IsHex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Need(bool condition, string stage) { if (!condition) throw new BoundaryFailure(stage); }
    private static void Win(bool condition, string stage) { if (!condition) throw new BoundaryFailure(stage, Marshal.GetLastWin32Error()); }

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal nint Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityCapabilities { internal nint AppContainerSid, Capabilities; internal uint Count, Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { internal int Size; internal nint Reserved, Desktop, Title; internal uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags; internal ushort Show, Reserved2; internal nint ReservedPointer, StdInput, StdOutput, StdError; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo StartupInfo; internal nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinWorking, MaxWorking; internal uint ActiveProcesses; internal nuint Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { internal BasicLimits Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcess, PeakJob; }

    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailure(string wszServerName);
    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailureAndGetInfo(string wszServerName, out int netIsoError);
    [DllImport("Firewallapi.dll")] private static extern uint NetworkIsolationGetAppContainerConfig(out uint count, out nint appContainerSids);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int CreateAppContainerProfile(string name, string display, string description, nint caps, uint count, out nint sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int DeleteAppContainerProfile(string name);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int GetAppContainerFolderPath(string sid, out nint path);
    [DllImport("advapi32.dll")] private static extern nint FreeSid(nint sid);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint p);
    [DllImport("kernel32.dll")] private static extern nint GetProcessHeap();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HeapFree(nint heap, uint flags, nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out nint r, out nint w, ref SecurityAttributes sa, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(nint h, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint key, nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string app, StringBuilder cmd, nint pa, nint ta, bool inherit, uint flags, nint env, string cwd, ref StartupInfoEx si, out ProcessInformation pi);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int kind, nint data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(nint job, int kind, nint data, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(nint process, nint job, out bool result);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int kind, nint data, uint size, out uint returned);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSidToSid(string text, out nint sid);
    [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertSidToStringSid(nint sid, out nint text);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualSid(nint a, nint b);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(nint h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(nint job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint h);
}
