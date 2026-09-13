using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class PrivateNetworkProofV1
{
    private const string Authorization = "ISSUE-106-CURRENT-EXPLICIT-DEVELOPMENT";
    private const string Predecessor = "27595e46d44858250f59bf908852f30aa79c3495";
    private const string ExactNativeBoundaryBlob = "8a8f91a13c114e9c224cf449193800f0fedfdaf2";
    private const string ProfilePrefix = "Matawaka.IsolationProbe.";
    private const string SelectionRule = "OPERATIONAL_RFC1918_IPV4_LOWEST_NUMERIC_THEN_INTERFACE_ID";
    private const string ProofBasis = "PRIVATE_NETWORK_MISSING_CAPABILITY_DIAGNOSTIC";
    private const string ChildSchema = "matawaka.workbench-appcontainer-private-network-child/v0.1";
    private const string ProofSchema = "matawaka.workbench-windows-private-network-isolation-proof/v0.1";
    private const string ContractJson = "{\"schema\":\"matawaka.workbench-windows-private-network-profile/v0.1\",\"nativeBoundaryBlob\":\"8a8f91a13c114e9c224cf449193800f0fedfdaf2\",\"appContainerCapabilities\":0,\"childProcessRestricted\":true,\"jobActiveProcessLimit\":1,\"jobProcessMemoryBytes\":536870912,\"killOnJobClose\":true,\"dieOnUnhandledException\":true,\"targetClass\":\"runner-owned-rfc1918-unicast-ipv4\",\"targetSelectionRule\":\"OPERATIONAL_RFC1918_IPV4_LOWEST_NUMERIC_THEN_INTERFACE_ID\",\"requiredMissingCapability\":\"PRIVATE_NETWORK\",\"requiredDiagnoseInfoType\":1,\"socketTimeoutMilliseconds\":1000,\"socketBehaviorUsedAsProof\":false,\"timeoutAloneIsProof\":false,\"dnsUsed\":false,\"externalNetworkAccessed\":false}";
    private static readonly string ContractDigest = Hash(Encoding.UTF8.GetBytes(ContractJson));

    private sealed record TargetEvidence(
        string Address,
        string InterfaceId,
        string InterfaceName,
        string InterfaceType,
        string SelectionRule);

    private sealed record ChildEvidence(
        string Schema,
        string ContractDigestSha256,
        string PackageSid,
        int CapabilityCount,
        TargetEvidence Target,
        bool DiagnoseHasRequiredCapability,
        uint DiagnoseInfoReturn,
        int DiagnoseInfoType,
        bool DiagnosticClassificationUsedAsProof,
        string SocketOutcome,
        int? SocketCode,
        bool SocketBehaviorUsedAsProof,
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
        string PrivateNetworkProofBasis,
        bool DiagnosticClassificationUsedAsProof,
        bool SocketBehaviorUsedAsProof,
        TargetEvidence? Target,
        int TargetPort,
        string ProfileName,
        string PackageSid,
        string ChildExecutableSha256,
        TokenObservation? Token,
        bool JobLimitsVerified,
        bool ParentTargetControlConnected,
        bool UnexpectedIsolatedConnectionObserved,
        ChildEvidence? Child,
        bool ProcessCreated,
        bool ProcessExited,
        uint? ProcessExitCode,
        bool ProfileCreated,
        bool ProfileRemoved,
        bool CleanupSucceeded,
        bool OsPrivateNetworkIsolationProven,
        bool SocketTimeoutPromotedToProof,
        bool TargetSelectionUsedDns,
        bool ExternalNetworkAccessed,
        bool NetworkIsolationConfigMutated,
        bool FirewallRuleMutated,
        bool RouteOrInterfaceMutated,
        bool GlobalWindowsPolicyMutated,
        bool ModelStarted,
        bool GameAccessed,
        bool ProductionProviderRegistered,
        string? FailureStage,
        int? FailureNativeCode);

    private sealed record SelectedTarget(IPAddress Address, string InterfaceId, string InterfaceName, NetworkInterfaceType InterfaceType);

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--unit" })) return Unit();
        if (args.SequenceEqual(new[] { "--escape-canary" })) return 17;
        if (args.Length == 2 && args[0] == "--child" && int.TryParse(args[1], out var port) && port is > 0 and <= 65535)
            return Child(port);
        if (args.Length == 6 && args[0] == "--private-network-trial")
            return Parent(args[1], args[2], args[3], args[4], args[5]);
        Console.Error.WriteLine("EXPLICIT_TEST_MODE_REQUIRED");
        return 64;
    }

    private static int Unit()
    {
        int pass = 0;
        var target = new TargetEvidence("10.0.0.4", "if-1", "Ethernet", "Ethernet", SelectionRule);
        ValidateTarget(target); pass++;
        foreach (var badTarget in new[]
        {
            target with { Address = "127.0.0.1" },
            target with { Address = "169.254.1.2" },
            target with { Address = "8.8.8.8" },
            target with { Address = "0.0.0.0" },
            target with { InterfaceId = "" },
            target with { SelectionRule = "OTHER" }
        }) { MustRefuse(() => ValidateTarget(badTarget)); pass++; }

        var validChild = new ChildEvidence(ChildSchema, ContractDigest, "S-1-15-2-1", 0, target, false, 0, 1, true,
            "TIMEOUT", null, false, true, true, true, true, 367);
        ValidateChild(validChild); pass++;
        foreach (var bad in new[]
        {
            validChild with { CapabilityCount = 1 },
            validChild with { DiagnoseHasRequiredCapability = true },
            validChild with { DiagnoseInfoReturn = 5 },
            validChild with { DiagnoseInfoType = 0 },
            validChild with { DiagnoseInfoType = 2 },
            validChild with { DiagnoseInfoType = 3 },
            validChild with { DiagnosticClassificationUsedAsProof = false },
            validChild with { SocketOutcome = "CONNECTED" },
            validChild with { SocketBehaviorUsedAsProof = true },
            validChild with { ReadAllowed = false },
            validChild with { ReadDenied = false },
            validChild with { WriteDenied = false },
            validChild with { ChildDenied = false },
            validChild with { ChildErrorCode = 2 },
            validChild with { PackageSid = "S-1-5-18" },
            validChild with { ContractDigestSha256 = new string('0', 64) },
            validChild with { Target = target with { Address = "127.0.0.1" } }
        }) { MustRefuse(() => ValidateChild(bad)); pass++; }

        byte[] raw = JsonSerializer.SerializeToUtf8Bytes(validChild);
        _ = ParseChild(raw); pass++;
        var extra = JsonSerializer.Deserialize<Dictionary<string, object>>(raw)!;
        extra["Authority"] = true;
        MustRefuse(() => ParseChild(JsonSerializer.SerializeToUtf8Bytes(extra))); pass++;
        MustRefuse(() => ParseChild(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw)[..^1] + ",\"Schema\":\"x\"}"))); pass++;
        MustRefuse(() => ParseChild([])); pass++;
        MustRefuse(() => ParseChild(new byte[8193])); pass++;

        var token = new TokenObservation(true, 0, true, true, false, true);
        var validProof = new ProofEvidence(ProofSchema, "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", new string('a', 40),
            Predecessor, ExactNativeBoundaryBlob, ContractDigest, ProofBasis, true, false, target, 43210,
            "Matawaka.IsolationProbe.test", validChild.PackageSid, new string('b', 64), token, true, true, false, validChild,
            true, true, 0, true, true, true, true, false, false, false, false, false, false, false, false, false, false, null, null);
        ValidateProof(validProof); pass++;
        foreach (var bad in new[]
        {
            validProof with { PrivateNetworkProofBasis = "OTHER", OsPrivateNetworkIsolationProven = false },
            validProof with { DiagnosticClassificationUsedAsProof = false, OsPrivateNetworkIsolationProven = false },
            validProof with { SocketBehaviorUsedAsProof = true, OsPrivateNetworkIsolationProven = false },
            validProof with { Target = target with { Address = "127.0.0.1" }, OsPrivateNetworkIsolationProven = false },
            validProof with { ParentTargetControlConnected = false, OsPrivateNetworkIsolationProven = false },
            validProof with { UnexpectedIsolatedConnectionObserved = true, OsPrivateNetworkIsolationProven = false },
            validProof with { Child = validChild with { Target = target with { Address = "10.0.0.5" } }, OsPrivateNetworkIsolationProven = false },
            validProof with { SocketTimeoutPromotedToProof = true, OsPrivateNetworkIsolationProven = false },
            validProof with { TargetSelectionUsedDns = true, OsPrivateNetworkIsolationProven = false },
            validProof with { ExternalNetworkAccessed = true, OsPrivateNetworkIsolationProven = false },
            validProof with { RouteOrInterfaceMutated = true, OsPrivateNetworkIsolationProven = false },
            validProof with { ProfileRemoved = false, CleanupSucceeded = false, OsPrivateNetworkIsolationProven = false },
            validProof with { ProductionProviderRegistered = true, OsPrivateNetworkIsolationProven = false }
        }) { MustRefuse(() => ValidateProof(bad)); pass++; }

        NativeBoundary.Need(IsRfc1918(IPAddress.Parse("10.1.2.3")), "RFC1918_10"); pass++;
        NativeBoundary.Need(IsRfc1918(IPAddress.Parse("172.16.0.1")) && IsRfc1918(IPAddress.Parse("172.31.255.254")), "RFC1918_172"); pass++;
        NativeBoundary.Need(IsRfc1918(IPAddress.Parse("192.168.1.1")) && !IsRfc1918(IPAddress.Parse("172.32.0.1")), "RFC1918_192"); pass++;
        Console.WriteLine(JsonSerializer.Serialize(new { status = "WINDOWS_PRIVATE_NETWORK_PROOF_V1_PURE_CONTROLS_PASS", passed = pass, nativeCalls = false }));
        return 0;
    }

    private static void MustRefuse(Action action)
    {
        bool refused = false;
        try { action(); }
        catch (Exception e) when (e is BoundaryFailure or InvalidDataException or JsonException or InvalidOperationException) { refused = true; }
        NativeBoundary.Need(refused, "HOSTILE_CASE_ACCEPTED");
    }

    private static void ValidateTarget(TargetEvidence target)
    {
        if (!IPAddress.TryParse(target.Address, out var address) || address is null || address.AddressFamily != AddressFamily.InterNetwork)
            throw new BoundaryFailure("TARGET_IPV4_LITERAL_REQUIRED");
        NativeBoundary.Need(!IPAddress.IsLoopback(address) && IsRfc1918(address), "TARGET_RFC1918_NON_LOOPBACK_REQUIRED");
        NativeBoundary.Need(!string.IsNullOrWhiteSpace(target.InterfaceId) && !string.IsNullOrWhiteSpace(target.InterfaceName) && !string.IsNullOrWhiteSpace(target.InterfaceType), "TARGET_INTERFACE_IDENTITY_REQUIRED");
        NativeBoundary.Need(target.SelectionRule == SelectionRule, "TARGET_SELECTION_RULE");
    }

    private static void ValidateChild(ChildEvidence child)
    {
        NativeBoundary.Need(child.Schema == ChildSchema, "CHILD_SCHEMA");
        NativeBoundary.Need(child.ContractDigestSha256 == ContractDigest, "CHILD_CONTRACT_BINDING");
        NativeBoundary.Need(child.PackageSid.StartsWith("S-1-15-2-", StringComparison.Ordinal), "CHILD_PACKAGE_SID");
        NativeBoundary.Need(child.CapabilityCount == 0, "CHILD_CAPABILITY_COUNT");
        ValidateTarget(child.Target);
        NativeBoundary.Need(!child.DiagnoseHasRequiredCapability, "WINDOWS_DIAG_CAPABILITY_PRESENT");
        NativeBoundary.Need(child.DiagnoseInfoReturn == 0, "WINDOWS_DIAG_INFO_FAILED");
        NativeBoundary.Need(child.DiagnoseInfoType == 1, "WINDOWS_DIAG_NOT_PRIVATE_NETWORK_DENIAL");
        NativeBoundary.Need(child.DiagnosticClassificationUsedAsProof, "PRIVATE_NETWORK_DIAGNOSTIC_NOT_BOUND_AS_PROOF");
        NativeBoundary.Need(child.SocketOutcome is "TIMEOUT" or "SOCKET_ERROR" or "REFUSED", "SOCKET_OUTCOME_INVALID");
        NativeBoundary.Need(child.SocketOutcome != "CONNECTED", "ISOLATED_SOCKET_CONNECTED");
        NativeBoundary.Need(!child.SocketBehaviorUsedAsProof, "SOCKET_BEHAVIOR_PROMOTED_TO_PROOF");
        NativeBoundary.Need(child.ReadAllowed && child.ReadDenied && child.WriteDenied && child.ChildDenied, "CHILD_NONNETWORK_BOUNDARY_REFUSED");
        NativeBoundary.Need(child.ChildErrorCode is 5 or 367, "CHILD_PROCESS_DENIAL_REQUIRED");
    }

    private static ChildEvidence ParseChild(byte[] bytes)
    {
        NativeBoundary.Need(bytes.Length is > 0 and <= 8192, "CHILD_RECEIPT_SIZE");
        using var doc = JsonDocument.Parse(bytes);
        NativeBoundary.Need(doc.RootElement.ValueKind == JsonValueKind.Object, "CHILD_RECEIPT_OBJECT");
        string[] expected = ["Schema", "ContractDigestSha256", "PackageSid", "CapabilityCount", "Target",
            "DiagnoseHasRequiredCapability", "DiagnoseInfoReturn", "DiagnoseInfoType", "DiagnosticClassificationUsedAsProof",
            "SocketOutcome", "SocketCode", "SocketBehaviorUsedAsProof", "ReadAllowed", "ReadDenied", "WriteDenied", "ChildDenied", "ChildErrorCode"];
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        NativeBoundary.Need(names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "CHILD_RECEIPT_KEYS");
        var value = JsonSerializer.Deserialize<ChildEvidence>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("CHILD_RECEIPT_DESERIALIZE");
        ValidateChild(value);
        return value;
    }

    private static void ValidateProof(ProofEvidence proof)
    {
        NativeBoundary.Need(proof.Schema == ProofSchema && proof.Status == "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", "PROOF_STATUS");
        NativeBoundary.Need(IsHex(proof.SourceHead, 40) && proof.Predecessor == Predecessor && proof.NativeBoundaryBlob == ExactNativeBoundaryBlob, "PROOF_SOURCE_BINDING");
        NativeBoundary.Need(proof.ContractDigestSha256 == ContractDigest, "PROOF_CONTRACT_BINDING");
        NativeBoundary.Need(proof.PrivateNetworkProofBasis == ProofBasis && proof.DiagnosticClassificationUsedAsProof && !proof.SocketBehaviorUsedAsProof, "PROOF_EVIDENCE_ROLES");
        var target = proof.Target ?? throw new BoundaryFailure("PROOF_TARGET_ABSENT");
        ValidateTarget(target);
        NativeBoundary.Need(proof.TargetPort is > 0 and <= 65535, "PROOF_TARGET_PORT");
        NativeBoundary.Need(proof.ProfileName.StartsWith(ProfilePrefix, StringComparison.Ordinal), "PROOF_PROFILE_NAME");
        NativeBoundary.Need(proof.PackageSid.StartsWith("S-1-15-2-", StringComparison.Ordinal), "PROOF_PACKAGE_SID");
        NativeBoundary.Need(IsHex(proof.ChildExecutableSha256, 64), "PROOF_CHILD_SHA");
        NativeBoundary.ValidateToken(proof.Token ?? throw new BoundaryFailure("PROOF_TOKEN_ABSENT"));
        NativeBoundary.Need(proof.JobLimitsVerified, "PROOF_JOB_LIMITS");
        NativeBoundary.Need(proof.ParentTargetControlConnected && !proof.UnexpectedIsolatedConnectionObserved, "PROOF_TARGET_OBSERVATION");
        var child = proof.Child ?? throw new BoundaryFailure("PROOF_CHILD_ABSENT");
        ValidateChild(child);
        NativeBoundary.Need(child.PackageSid == proof.PackageSid && child.Target == target, "PROOF_CHILD_TARGET_PROFILE_BINDING");
        NativeBoundary.Need(proof.ProcessCreated && proof.ProcessExited && proof.ProcessExitCode == 0, "PROOF_PROCESS_TERMINAL");
        NativeBoundary.Need(proof.ProfileCreated && proof.ProfileRemoved && proof.CleanupSucceeded, "PROOF_CLEANUP");
        NativeBoundary.Need(proof.OsPrivateNetworkIsolationProven && !proof.SocketTimeoutPromotedToProof, "PROOF_NETWORK_BOOLEAN_CONTRACT");
        NativeBoundary.Need(!proof.TargetSelectionUsedDns && !proof.ExternalNetworkAccessed, "PROOF_EXTERNAL_TARGET_BOUNDARY");
        NativeBoundary.Need(!proof.NetworkIsolationConfigMutated && !proof.FirewallRuleMutated && !proof.RouteOrInterfaceMutated && !proof.GlobalWindowsPolicyMutated, "PROOF_GLOBAL_MUTATION");
        NativeBoundary.Need(!proof.ModelStarted && !proof.GameAccessed && !proof.ProductionProviderRegistered, "PROOF_AUTHORITY_WIDENING");
    }

    private static int Child(int port)
    {
        bool readAllowed = false, readDenied = false, writeDenied = false, childDenied = false;
        int? childErrorCode = null, socketCode = null;
        string socketOutcome = "OTHER";
        try { readAllowed = File.ReadAllText("allow-read.txt") == "SYNTHETIC_ALLOWED"; } catch { }
        try { _ = File.ReadAllText("deny-read.txt"); } catch (UnauthorizedAccessException) { readDenied = true; }
        try { File.WriteAllText("deny-write.txt", "UNEXPECTED_WRITE"); } catch (UnauthorizedAccessException) { writeDenied = true; }

        SelectedTarget selected = SelectPrivateTarget();
        var target = ToEvidence(selected);
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ConnectAsync(new IPEndPoint(selected.Address, port)).WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
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
        string targetLiteral = selected.Address.ToString();
        uint hasRequired = NetworkIsolationDiagnoseConnectFailure(targetLiteral);
        uint infoReturn = NetworkIsolationDiagnoseConnectFailureAndGetInfo(targetLiteral, out int infoType);
        var result = new ChildEvidence(ChildSchema, ContractDigest, packageSid, capabilityCount, target, hasRequired != 0,
            infoReturn, infoType, true, socketOutcome, socketCode, false, readAllowed, readDenied, writeDenied, childDenied, childErrorCode);
        Console.WriteLine(JsonSerializer.Serialize(result));
        try { ValidateChild(result); return 0; } catch { return 2; }
    }

    private static int Parent(string directory, string manifestPath, string evidencePath, string sourceHead, string authorization)
    {
        if (authorization != Authorization || !IsHex(sourceHead, 40)) return 65;
        string root = NativeBoundary.ValidateRoot(directory);
        string evidenceFull = Path.GetFullPath(evidencePath);
        NativeBoundary.Need(!File.Exists(evidenceFull), "CREATE_ONLY_EVIDENCE_REQUIRED");
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(evidenceFull)!, "PRIVATE-NETWORK-PROOF-ATTEMPT.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(marker, new { schema = "matawaka.workbench-private-network-proof-attempt/v0.1", sourceHead, authorization = "ISSUE_106", modelAuthorized = false, dnsUsed = false, internetTargetUsed = false });

        using var manifestDoc = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes = manifestDoc.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var boundary = new NativeBoundary();
        TcpListener? listener = null;
        string status = "FAIL_CLOSED", stage = "START", profileName = "", packageSid = "", childSha = "";
        int? nativeCode = null;
        ChildEvidence? child = null;
        TargetEvidence? target = null;
        int targetPort = 0;
        bool control = false, unexpected = false, osProof = false;
        try
        {
            stage = "PREPARE";
            boundary.Prepare(root, hashes);
            var identity = BoundaryIdentity(boundary);
            profileName = identity.ProfileName;
            packageSid = identity.PackageSid;
            childSha = Hash(File.ReadAllBytes(Path.Combine(root, "Workbench.AppContainerProbe.exe")));

            stage = "TARGET_SELECT";
            SelectedTarget selected = SelectPrivateTarget();
            target = ToEvidence(selected);
            ValidateTarget(target);
            listener = new TcpListener(selected.Address, 0);
            listener.Start(2);
            targetPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            stage = "PARENT_TARGET_CONTROL";
            using (var normal = new TcpClient(AddressFamily.InterNetwork))
            {
                normal.Connect(selected.Address, targetPort);
                using var accepted = listener.AcceptTcpClient();
                control = accepted.Connected;
            }
            NativeBoundary.Need(control, "PARENT_PRIVATE_TARGET_CONTROL_FAILED");

            stage = "NATIVE_START";
            boundary.Start(root, targetPort);
            var outputTask = Task.Run(() => ReadBounded(boundary.Output));
            var errorTask = Task.Run(() => ReadBounded(boundary.Error));
            stage = "WAIT_CHILD";
            NativeBoundary.Need(boundary.Wait(10000), "CHILD_TIMEOUT_PARENT_WAIT");
            unexpected = listener.Pending();
            byte[] outBytes = outputTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _ = errorTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            child = ParseChild(outBytes);
            NativeBoundary.Need(boundary.ExitCode == 0, "CHILD_EXIT_NONZERO");
            NativeBoundary.Need(!unexpected, "ISOLATED_PRIVATE_CONNECTION_ACCEPTABLE_PATH_OBSERVED");
            NativeBoundary.Need(boundary.Token is not null, "TOKEN_EVIDENCE_ABSENT");
            NativeBoundary.ValidateToken(boundary.Token!);
            NativeBoundary.Need(boundary.JobLimitsVerified, "JOB_EVIDENCE_ABSENT");
            NativeBoundary.Need(child.PackageSid == packageSid, "CHILD_PROFILE_SID_MISMATCH");
            NativeBoundary.Need(child.Target == target, "CHILD_PRIVATE_TARGET_MISMATCH");

            osProof = child.CapabilityCount == 0 && !child.DiagnoseHasRequiredCapability && child.DiagnoseInfoReturn == 0 &&
                child.DiagnoseInfoType == 1 && child.DiagnosticClassificationUsedAsProof && !child.SocketBehaviorUsedAsProof &&
                child.SocketOutcome != "CONNECTED" && !unexpected;
            NativeBoundary.Need(osProof, "OS_PRIVATE_NETWORK_ISOLATION_EVIDENCE_INCOMPLETE");
            status = "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN";
            stage = "COMPLETE";
        }
        catch (BoundaryFailure e) { status = "FAIL_CLOSED"; stage = e.Message; nativeCode = e.NativeCode; }
        catch (Exception e) { status = "FAIL_CLOSED"; stage = "MANAGED_" + e.GetType().Name; }
        finally { listener?.Stop(); boundary.Dispose(); }

        if (!boundary.CleanupSucceeded) { status = "FAIL_CLOSED"; stage = "CLEANUP_INCOMPLETE"; osProof = false; }
        var result = new ProofEvidence(ProofSchema, status, sourceHead, Predecessor, ExactNativeBoundaryBlob, ContractDigest,
            ProofBasis, true, false, target, targetPort, profileName, packageSid, childSha, boundary.Token, boundary.JobLimitsVerified,
            control, unexpected, child, boundary.ProcessCreated, boundary.ProcessExited, boundary.ExitCode, boundary.ProfileCreated,
            boundary.ProfileRemoved, boundary.CleanupSucceeded, osProof && status == "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN",
            false, false, false, false, false, false, false, false, false, false, stage == "COMPLETE" ? null : stage, nativeCode);
        if (result.Status == "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN") ValidateProof(result);
        using (var file = new FileStream(evidenceFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status == "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN" ? 0 : 3;
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
        NativeBoundary.Need(candidates.Count > 0, "PRIVATE_TARGET_ABSENT");
        return candidates.OrderBy(c => AddressKey(c.Address)).ThenBy(c => c.InterfaceId, StringComparer.Ordinal).First();
    }

    private static TargetEvidence ToEvidence(SelectedTarget selected) => new(selected.Address.ToString(), selected.InterfaceId, selected.InterfaceName, selected.InterfaceType.ToString(), SelectionRule);

    private static uint AddressKey(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        NativeBoundary.Need(bytes.Length == 4, "TARGET_ADDRESS_SHAPE");
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static bool IsRfc1918(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static (string ProfileName, string PackageSid) BoundaryIdentity(NativeBoundary boundary)
    {
        var type = typeof(NativeBoundary);
        var profileField = type.GetField("profile", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new BoundaryFailure("PROFILE_REFLECTION_BINDING");
        var sidField = type.GetField("packageSid", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new BoundaryFailure("PACKAGE_SID_REFLECTION_BINDING");
        string profile = profileField.GetValue(boundary) as string ?? throw new BoundaryFailure("PROFILE_NAME_ABSENT");
        object raw = sidField.GetValue(boundary) ?? throw new BoundaryFailure("PACKAGE_SID_ABSENT");
        nint ptr = raw is IntPtr p ? p : throw new BoundaryFailure("PACKAGE_SID_SHAPE");
        NativeBoundary.Need(ptr != 0, "PACKAGE_SID_ABSENT");
        return (profile, new SecurityIdentifier(ptr).Value);
    }

    private static string CurrentPackageSid(out int capabilityCount)
    {
        Win(OpenProcessToken(GetCurrentProcess(), 8, out nint token), "CHILD_TOKEN_QUERY");
        try
        {
            using var sid = QueryToken(token, 31);
            using var caps = QueryToken(token, 30);
            nint sidPtr = Marshal.ReadIntPtr(sid.Pointer);
            NativeBoundary.Need(sidPtr != 0, "CHILD_PACKAGE_SID_ABSENT");
            capabilityCount = Marshal.ReadInt32(caps.Pointer);
            NativeBoundary.Need(capabilityCount >= 0, "CHILD_CAPABILITY_SHAPE");
            return new SecurityIdentifier(sidPtr).Value;
        }
        finally { CloseHandle(token); }
    }

    private static byte[] ReadBounded(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            int n = stream.Read(chunk);
            if (n == 0) break;
            NativeBoundary.Need(buffer.Length + n <= 8192, "CHILD_OUTPUT_LIMIT");
            buffer.Write(chunk, 0, n);
        }
        return buffer.ToArray();
    }

    private static TokenBuffer QueryToken(nint token, int kind)
    {
        bool initial = GetTokenInformation(token, kind, 0, 0, out uint needed);
        int error = Marshal.GetLastWin32Error();
        if (initial || error != 122 || needed is 0 or > 16384) throw new BoundaryFailure("CHILD_TOKEN_SIZE_QUERY_" + kind, error);
        var buffer = new TokenBuffer((int)needed);
        try
        {
            Win(GetTokenInformation(token, kind, buffer.Pointer, needed, out uint written), "CHILD_TOKEN_QUERY");
            NativeBoundary.Need(written <= needed, "CHILD_TOKEN_SIZE_CHANGED");
            return buffer;
        }
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

    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailure(string serverName);
    [DllImport("Firewallapi.dll", CharSet = CharSet.Unicode)] private static extern uint NetworkIsolationDiagnoseConnectFailureAndGetInfo(string serverName, out int errorType);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int kind, nint data, uint size, out uint returned);
}
