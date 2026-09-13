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

internal static class PrivatePeerProofV1
{
    private const string Authorization = "ISSUE-107-CONTROLLED-EXECUTOR-TRIAL";
    private const string Predecessor = "efff344bc1418e9b1da946a8493f854bf8ada9af";
    private const string ExactNativeBoundaryBlob = "8a8f91a13c114e9c224cf449193800f0fedfdaf2";
    private const string QualifiedPeerSourceSha256 = "b01ab34cd9923d34b72c4ca557ffb292cb9621a896be0fbbb020ddd88d8bc049";
    private const string QualifiedPeerContractSha256 = "793de6f7d0f68317d331b2e7e3eec20e02dcdd9acac5c665ceab633122aea1ca";
    private const string ProofNodeAddressLiteral = "10.77.0.1";
    private const string PeerAddressLiteral = "10.77.0.2";
    private const int ManagementPort = 41060;
    private const int TargetPort = 41061;
    private const int MaxLineBytes = 4096;
    private const string CommandSchema = "matawaka.private-peer-command/v0.1";
    private const string AckSchema = "matawaka.private-peer-ack/v0.1";
    private const string PeerReceiptSchema = "matawaka.private-peer-receipt/v0.1";
    private const string ChildSchema = "matawaka.workbench-appcontainer-private-peer-child/v0.1";
    private const string ProofSchema = "matawaka.workbench-windows-private-peer-isolation-proof/v0.1";
    private const string ProofBasis = "PRIVATE_NETWORK_MISSING_CAPABILITY_AND_PEER_ZERO_ACCEPT";
    private const string ProfilePrefix = "Matawaka.IsolationProbe.";
    private const string ContractJson = "{\"schema\":\"matawaka.workbench-windows-private-peer-profile/v0.1\",\"nativeBoundaryBlob\":\"8a8f91a13c114e9c224cf449193800f0fedfdaf2\",\"peerSourceSha256\":\"b01ab34cd9923d34b72c4ca557ffb292cb9621a896be0fbbb020ddd88d8bc049\",\"peerContractSha256\":\"793de6f7d0f68317d331b2e7e3eec20e02dcdd9acac5c665ceab633122aea1ca\",\"proofNodeAddress\":\"10.77.0.1\",\"peerAddress\":\"10.77.0.2\",\"managementPort\":41060,\"targetPort\":41061,\"appContainerCapabilities\":0,\"requiredMissingCapability\":\"PRIVATE_NETWORK\",\"requiredDiagnoseInfoType\":1,\"peerChildWindowAcceptsForPass\":0,\"socketBehaviorUsedAsProof\":false,\"dnsUsed\":false,\"internetTargetUsed\":false}";
    private static readonly string ContractDigest = Sha256Hex(Encoding.UTF8.GetBytes(ContractJson));

    private sealed record PeerCommand(string Schema, string Op, string SessionId, string NonceHex);
    private sealed record PeerAck(string Schema, string Op, string SessionId, bool Accepted, bool ProofClaimed);
    private sealed record PeerReceipt(
        string Schema,
        string SessionId,
        string NonceSha256,
        string PeerAddress,
        string ProofNodeAddress,
        int ManagementPort,
        int TargetPort,
        bool ParentControlAccepted,
        string? ParentControlRemoteAddress,
        int TargetAcceptsTotal,
        int ChildWindowAcceptedConnections,
        DateTimeOffset? ChildWindowOpenedUtc,
        DateTimeOffset? ChildWindowClosedUtc,
        bool ProtocolSatisfied,
        bool DnsUsed,
        bool InternetTargetUsed,
        bool ExternalServiceUsed,
        bool NetworkConfigurationMutated,
        bool ProofClaimed,
        string? FailureStage);

    private sealed record TopologyObservation(
        bool ProofNodeAddressPresent,
        bool PeerAddressAbsentLocally,
        string LocalIpv4SetSha256);

    private sealed record ChildEvidence(
        string Schema,
        string ContractDigestSha256,
        string PackageSid,
        int CapabilityCount,
        string TargetAddress,
        int TargetPort,
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
        string PeerProtocolSourceSha256,
        string PeerContractSha256,
        string PrivateNetworkProofBasis,
        bool DiagnosticClassificationUsedAsProof,
        bool PeerZeroAcceptUsedAsProof,
        bool SocketBehaviorUsedAsProof,
        TopologyObservation? Topology,
        string SessionId,
        string NonceSha256,
        PeerReceipt? Peer,
        string ProfileName,
        string PackageSid,
        string ChildExecutableSha256,
        TokenObservation? Token,
        bool JobLimitsVerified,
        ChildEvidence? Child,
        bool ProcessCreated,
        bool ProcessExited,
        uint? ProcessExitCode,
        bool ProfileCreated,
        bool ProfileRemoved,
        bool CleanupSucceeded,
        bool OsPrivateNetworkIsolationProven,
        bool SocketTimeoutPromotedToProof,
        bool DnsUsed,
        bool InternetTargetUsed,
        bool ExternalServiceUsed,
        bool NetworkIsolationConfigMutated,
        bool FirewallRuleMutated,
        bool RouteOrInterfaceMutated,
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
        if (args.Length == 2 && args[0] == "--child" && int.TryParse(args[1], out int port) && port == TargetPort)
            return Child(port);
        if (args.Length == 6 && args[0] == "--peer-private-trial")
            return Parent(args[1], args[2], args[3], args[4], args[5]);
        Console.Error.WriteLine("EXPLICIT_TEST_MODE_REQUIRED");
        return 64;
    }

    private static int Unit()
    {
        int pass = 0;
        string session = "0123456789abcdef0123456789abcdef";
        string nonce = new string('a', 64);
        string nonceDigest = Sha256Hex(Convert.FromHexString(nonce));

        var begin = new PeerCommand(CommandSchema, "BEGIN", session, nonce);
        byte[] commandBytes = JsonSerializer.SerializeToUtf8Bytes(begin);
        Need(ParseCommand(commandBytes) == begin, "UNIT_COMMAND_ROUNDTRIP"); pass++;
        foreach (byte[] hostile in new[]
        {
            [], new byte[MaxLineBytes + 1], Encoding.UTF8.GetBytes("[]"),
            Encoding.UTF8.GetBytes("{\"Schema\":\"matawaka.private-peer-command/v0.1\",\"Op\":\"BEGIN\",\"SessionId\":\"0123456789abcdef0123456789abcdef\",\"NonceHex\":\"" + nonce + "\",\"Authority\":true}"),
            Encoding.UTF8.GetBytes("{\"Schema\":\"matawaka.private-peer-command/v0.1\",\"Op\":\"BEGIN\",\"SessionId\":\"0123456789abcdef0123456789abcdef\",\"NonceHex\":\"" + nonce + "\",\"Op\":\"ARM\"}")
        }) { MustRefuse(() => ParseCommand(hostile)); pass++; }

        var ack = new PeerAck(AckSchema, "BEGIN", session, true, false);
        Need(ParseAck(JsonSerializer.SerializeToUtf8Bytes(ack), "BEGIN", session) == ack, "UNIT_ACK_ROUNDTRIP"); pass++;
        foreach (var badAck in new[]
        {
            ack with { Schema = "wrong" }, ack with { Op = "ARM" }, ack with { SessionId = new string('1',32) },
            ack with { Accepted = false }, ack with { ProofClaimed = true }
        }) { MustRefuse(() => ParseAck(JsonSerializer.SerializeToUtf8Bytes(badAck), "BEGIN", session)); pass++; }

        var peer = new PeerReceipt(PeerReceiptSchema, session, nonceDigest, PeerAddressLiteral, ProofNodeAddressLiteral,
            ManagementPort, TargetPort, true, ProofNodeAddressLiteral, 1, 0,
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, true,
            false, false, false, false, false, null);
        ValidatePeerReceipt(peer, session, nonceDigest); pass++;
        foreach (var bad in new[]
        {
            peer with { ChildWindowAcceptedConnections = 1 },
            peer with { TargetAcceptsTotal = 2 },
            peer with { ParentControlAccepted = false },
            peer with { ParentControlRemoteAddress = "10.77.0.3" },
            peer with { ProtocolSatisfied = false },
            peer with { PeerAddress = ProofNodeAddressLiteral },
            peer with { DnsUsed = true },
            peer with { InternetTargetUsed = true },
            peer with { ExternalServiceUsed = true },
            peer with { NetworkConfigurationMutated = true },
            peer with { ProofClaimed = true },
            peer with { FailureStage = "x" }
        }) { MustRefuse(() => ValidatePeerReceipt(bad, session, nonceDigest)); pass++; }

        TopologyObservation goodTopology = ObserveSyntheticTopology([ProofNodeAddressLiteral, "192.168.9.4"]);
        Need(goodTopology.ProofNodeAddressPresent && goodTopology.PeerAddressAbsentLocally && IsHex(goodTopology.LocalIpv4SetSha256, 64), "UNIT_TOPOLOGY_PASS"); pass++;
        MustRefuse(() => ValidateTopology(ObserveSyntheticTopology([PeerAddressLiteral]))); pass++;
        MustRefuse(() => ValidateTopology(ObserveSyntheticTopology(["192.168.9.4"]))); pass++;

        var child = new ChildEvidence(ChildSchema, ContractDigest, "S-1-15-2-1", 0, PeerAddressLiteral, TargetPort,
            false, 0, 1, true, "TIMEOUT", null, false, true, true, true, true, 367);
        ValidateChild(child); pass++;
        foreach (var bad in new[]
        {
            child with { CapabilityCount = 1 }, child with { TargetAddress = ProofNodeAddressLiteral }, child with { TargetPort = 1 },
            child with { DiagnoseHasRequiredCapability = true }, child with { DiagnoseInfoReturn = 5 }, child with { DiagnoseInfoType = 0 },
            child with { DiagnoseInfoType = 2 }, child with { DiagnosticClassificationUsedAsProof = false },
            child with { SocketOutcome = "CONNECTED" }, child with { SocketBehaviorUsedAsProof = true },
            child with { ReadAllowed = false }, child with { ReadDenied = false }, child with { WriteDenied = false },
            child with { ChildDenied = false }, child with { ChildErrorCode = 2 }
        }) { MustRefuse(() => ValidateChild(bad)); pass++; }

        var token = new TokenObservation(true, 0, true, true, false, true);
        var proof = new ProofEvidence(ProofSchema, "OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN", new string('a',40), Predecessor,
            ExactNativeBoundaryBlob, ContractDigest, QualifiedPeerSourceSha256, QualifiedPeerContractSha256, ProofBasis,
            true, true, false, goodTopology, session, nonceDigest, peer, "Matawaka.IsolationProbe.test", child.PackageSid,
            new string('b',64), token, true, child, true, true, 0, true, true, true, true, false,
            false, false, false, false, false, false, false, false, false, false, null, null);
        ValidateProof(proof); pass++;
        foreach (var bad in new[]
        {
            proof with { DiagnosticClassificationUsedAsProof = false, OsPrivateNetworkIsolationProven = false },
            proof with { PeerZeroAcceptUsedAsProof = false, OsPrivateNetworkIsolationProven = false },
            proof with { SocketBehaviorUsedAsProof = true, OsPrivateNetworkIsolationProven = false },
            proof with { Peer = peer with { ChildWindowAcceptedConnections = 1 }, OsPrivateNetworkIsolationProven = false },
            proof with { SocketTimeoutPromotedToProof = true, OsPrivateNetworkIsolationProven = false },
            proof with { DnsUsed = true, OsPrivateNetworkIsolationProven = false },
            proof with { ProductionProviderRegistered = true, OsPrivateNetworkIsolationProven = false }
        }) { MustRefuse(() => ValidateProof(bad)); pass++; }

        Console.WriteLine(JsonSerializer.Serialize(new { status = "PRIVATE_PEER_PROOF_COORDINATOR_V1_PURE_CONTROLS_PASS", passed = pass, networkCalls = false, proofClaimed = false }));
        return 0;
    }

    private static int Child(int port)
    {
        bool readAllowed=false, readDenied=false, writeDenied=false, childDenied=false;
        int? childErrorCode=null, socketCode=null;
        string socketOutcome="OTHER";
        try { readAllowed = File.ReadAllText("allow-read.txt") == "SYNTHETIC_ALLOWED"; } catch { }
        try { _ = File.ReadAllText("deny-read.txt"); } catch (UnauthorizedAccessException) { readDenied=true; }
        try { File.WriteAllText("deny-write.txt","UNEXPECTED_WRITE"); } catch (UnauthorizedAccessException) { writeDenied=true; }

        IPAddress peer = IPAddress.Parse(PeerAddressLiteral);
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ConnectAsync(new IPEndPoint(peer, port)).WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
            socketOutcome = socket.Connected ? "CONNECTED" : "OTHER";
        }
        catch (SocketException e) { socketCode=e.ErrorCode; socketOutcome=e.SocketErrorCode==SocketError.ConnectionRefused?"REFUSED":"SOCKET_ERROR"; }
        catch (TimeoutException) { socketOutcome="TIMEOUT"; }
        catch { socketOutcome="OTHER"; }

        try
        {
            var start = new ProcessStartInfo { FileName=Path.Combine(Environment.CurrentDirectory,"Workbench.AppContainerProbe.exe"), UseShellExecute=false, CreateNoWindow=true };
            start.ArgumentList.Add("--escape-canary");
            using var process=Process.Start(start);
            if(process is not null && !process.WaitForExit(1000)){process.Kill();process.WaitForExit(1000);}
        }
        catch(System.ComponentModel.Win32Exception e){childErrorCode=e.NativeErrorCode;childDenied=e.NativeErrorCode is 5 or 367;}

        string packageSid=CurrentPackageSid(out int capabilityCount);
        uint hasRequired=NetworkIsolationDiagnoseConnectFailure(PeerAddressLiteral);
        uint infoReturn=NetworkIsolationDiagnoseConnectFailureAndGetInfo(PeerAddressLiteral,out int infoType);
        var result=new ChildEvidence(ChildSchema,ContractDigest,packageSid,capabilityCount,PeerAddressLiteral,port,
            hasRequired!=0,infoReturn,infoType,true,socketOutcome,socketCode,false,readAllowed,readDenied,writeDenied,childDenied,childErrorCode);
        Console.WriteLine(JsonSerializer.Serialize(result));
        try{ValidateChild(result);return 0;}catch{return 2;}
    }

    private static int Parent(string directory,string manifestPath,string evidencePath,string sourceHead,string authorization)
    {
        if(authorization!=Authorization || !IsHex(sourceHead,40)) return 65;
        string root=NativeBoundary.ValidateRoot(directory);
        string evidenceFull=Path.GetFullPath(evidencePath);
        Need(!File.Exists(evidenceFull),"CREATE_ONLY_EVIDENCE_REQUIRED");
        using(var marker=new FileStream(Path.Combine(Path.GetDirectoryName(evidenceFull)!,"PRIVATE-PEER-PROOF-ATTEMPT.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            JsonSerializer.Serialize(marker,new{schema="matawaka.workbench-private-peer-proof-attempt/v0.1",sourceHead,authorization="ISSUE_107",peer=PeerAddressLiteral,targetPort=TargetPort,dnsUsed=false,internetTargetUsed=false});

        using var manifestDoc=JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes=manifestDoc.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
        var boundary=new NativeBoundary();
        string status="FAIL_CLOSED",stage="START",profileName="",packageSid="",childSha="",session="",nonceDigest="";
        int? nativeCode=null;
        ChildEvidence? child=null;
        PeerReceipt? peerReceipt=null;
        TopologyObservation? topology=null;
        bool osProof=false;
        try
        {
            stage="TOPOLOGY";topology=ObserveLocalTopology();ValidateTopology(topology);
            stage="PREPARE";boundary.Prepare(root,hashes);
            var identity=BoundaryIdentity(boundary);profileName=identity.ProfileName;packageSid=identity.PackageSid;
            childSha=Sha256Hex(File.ReadAllBytes(Path.Combine(root,"Workbench.AppContainerProbe.exe")));

            session=Guid.NewGuid().ToString("N");byte[] nonceBytes=RandomNumberGenerator.GetBytes(32);string nonce=Convert.ToHexString(nonceBytes).ToLowerInvariant();nonceDigest=Sha256Hex(nonceBytes);
            stage="PEER_BEGIN";ValidateAck(SendManagement(new PeerCommand(CommandSchema,"BEGIN",session,nonce)),"BEGIN",session);
            stage="PARENT_CONTROL";SendParentControl(session,nonce);
            stage="PEER_ARM";ValidateAck(SendManagement(new PeerCommand(CommandSchema,"ARM",session,nonce)),"ARM",session);

            stage="NATIVE_START";boundary.Start(root,TargetPort);
            var outputTask=Task.Run(()=>ReadBounded(boundary.Output));var errorTask=Task.Run(()=>ReadBounded(boundary.Error));
            stage="WAIT_CHILD";Need(boundary.Wait(10000),"CHILD_TIMEOUT_PARENT_WAIT");
            byte[] outBytes=outputTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _=errorTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            child=ParseChild(outBytes);Need(boundary.ExitCode==0,"CHILD_EXIT_NONZERO");
            Need(boundary.Token is not null,"TOKEN_EVIDENCE_ABSENT");NativeBoundary.ValidateToken(boundary.Token!);Need(boundary.JobLimitsVerified,"JOB_EVIDENCE_ABSENT");
            Need(child.PackageSid==packageSid,"CHILD_PROFILE_SID_MISMATCH");

            stage="PEER_FINALIZE";peerReceipt=ParsePeerReceipt(SendManagementRaw(new PeerCommand(CommandSchema,"FINALIZE",session,nonce)));ValidatePeerReceipt(peerReceipt,session,nonceDigest);
            osProof=child.CapabilityCount==0 && !child.DiagnoseHasRequiredCapability && child.DiagnoseInfoReturn==0 && child.DiagnoseInfoType==1 &&
                child.DiagnosticClassificationUsedAsProof && !child.SocketBehaviorUsedAsProof && child.SocketOutcome!="CONNECTED" &&
                peerReceipt.ProtocolSatisfied && peerReceipt.ChildWindowAcceptedConnections==0 && peerReceipt.TargetAcceptsTotal==1;
            Need(osProof,"OS_PRIVATE_NETWORK_ISOLATION_EVIDENCE_INCOMPLETE");
            status="OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN";stage="COMPLETE";
        }
        catch(BoundaryFailure e){status="FAIL_CLOSED";stage=e.Message;nativeCode=e.NativeCode;}
        catch(Exception e){status="FAIL_CLOSED";stage="MANAGED_"+e.GetType().Name;}
        finally{boundary.Dispose();}

        if(!boundary.CleanupSucceeded){status="FAIL_CLOSED";stage="CLEANUP_INCOMPLETE";osProof=false;}
        var result=new ProofEvidence(ProofSchema,status,sourceHead,Predecessor,ExactNativeBoundaryBlob,ContractDigest,QualifiedPeerSourceSha256,
            QualifiedPeerContractSha256,ProofBasis,true,true,false,topology,session,nonceDigest,peerReceipt,profileName,packageSid,childSha,
            boundary.Token,boundary.JobLimitsVerified,child,boundary.ProcessCreated,boundary.ProcessExited,boundary.ExitCode,boundary.ProfileCreated,
            boundary.ProfileRemoved,boundary.CleanupSucceeded,osProof&&status=="OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN",false,false,false,false,
            false,false,false,false,false,false,false,stage=="COMPLETE"?null:stage,nativeCode);
        if(result.Status=="OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN")ValidateProof(result);
        using(var file=new FileStream(evidenceFull,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,result,new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status=="OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN"?0:3;
    }

    private static PeerAck SendManagement(PeerCommand command)=>ParseAck(SendManagementRaw(command),command.Op,command.SessionId);

    private static byte[] SendManagementRaw(PeerCommand command)
    {
        byte[] request=JsonSerializer.SerializeToUtf8Bytes(command);
        Need(request.Length<=MaxLineBytes,"MANAGEMENT_REQUEST_SIZE");
        using var client=new TcpClient(AddressFamily.InterNetwork);
        client.ConnectAsync(IPAddress.Parse(PeerAddressLiteral),ManagementPort).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        using NetworkStream stream=client.GetStream();stream.ReadTimeout=3000;stream.WriteTimeout=3000;
        stream.Write(request);stream.WriteByte((byte)'\n');stream.Flush();
        return ReadLine(stream,MaxLineBytes);
    }

    private static void SendParentControl(string session,string nonce)
    {
        using var client=new TcpClient(AddressFamily.InterNetwork);
        client.ConnectAsync(IPAddress.Parse(PeerAddressLiteral),TargetPort).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        using NetworkStream stream=client.GetStream();stream.ReadTimeout=3000;stream.WriteTimeout=3000;
        byte[] line=Encoding.UTF8.GetBytes($"CONTROL|{session}|{nonce}\n");Need(line.Length<=512,"CONTROL_LINE_SIZE");stream.Write(line);stream.Flush();
        var one=new byte[1];
        while(true){int n=stream.Read(one,0,1);if(n==0)break;throw new BoundaryFailure("PARENT_CONTROL_UNEXPECTED_RESPONSE");}
    }

    private static PeerCommand ParseCommand(byte[] bytes)
    {
        Need(bytes.Length is >0 and <=MaxLineBytes,"COMMAND_SIZE");using JsonDocument doc=JsonDocument.Parse(bytes);Need(doc.RootElement.ValueKind==JsonValueKind.Object,"COMMAND_OBJECT");
        string[] expected=["Schema","Op","SessionId","NonceHex"];ValidateKeys(doc.RootElement,expected,"COMMAND_KEYS");
        PeerCommand value=JsonSerializer.Deserialize<PeerCommand>(doc.RootElement.GetRawText())??throw new BoundaryFailure("COMMAND_DESERIALIZE");
        Need(value.Schema==CommandSchema && value.Op is "BEGIN" or "ARM" or "FINALIZE","COMMAND_SEMANTICS");
        Need(Guid.TryParseExact(value.SessionId,"N",out _)&&value.SessionId==value.SessionId.ToLowerInvariant(),"COMMAND_SESSION");Need(IsLowerHex(value.NonceHex,64),"COMMAND_NONCE");return value;
    }

    private static PeerAck ParseAck(byte[] bytes,string expectedOp,string expectedSession)
    {
        Need(bytes.Length is >0 and <=MaxLineBytes,"ACK_SIZE");using JsonDocument doc=JsonDocument.Parse(bytes);Need(doc.RootElement.ValueKind==JsonValueKind.Object,"ACK_OBJECT");
        ValidateKeys(doc.RootElement,["Schema","Op","SessionId","Accepted","ProofClaimed"],"ACK_KEYS");
        PeerAck ack=JsonSerializer.Deserialize<PeerAck>(doc.RootElement.GetRawText())??throw new BoundaryFailure("ACK_DESERIALIZE");ValidateAck(ack,expectedOp,expectedSession);return ack;
    }

    private static void ValidateAck(PeerAck ack,string expectedOp,string expectedSession)
    {
        Need(ack.Schema==AckSchema&&ack.Op==expectedOp&&ack.SessionId==expectedSession&&ack.Accepted&&!ack.ProofClaimed,"PEER_ACK_REFUSED");
    }

    private static PeerReceipt ParsePeerReceipt(byte[] bytes)
    {
        Need(bytes.Length is >0 and <=MaxLineBytes,"PEER_RECEIPT_SIZE");using JsonDocument doc=JsonDocument.Parse(bytes);Need(doc.RootElement.ValueKind==JsonValueKind.Object,"PEER_RECEIPT_OBJECT");
        ValidateKeys(doc.RootElement,["Schema","SessionId","NonceSha256","PeerAddress","ProofNodeAddress","ManagementPort","TargetPort","ParentControlAccepted","ParentControlRemoteAddress","TargetAcceptsTotal","ChildWindowAcceptedConnections","ChildWindowOpenedUtc","ChildWindowClosedUtc","ProtocolSatisfied","DnsUsed","InternetTargetUsed","ExternalServiceUsed","NetworkConfigurationMutated","ProofClaimed","FailureStage"],"PEER_RECEIPT_KEYS");
        return JsonSerializer.Deserialize<PeerReceipt>(doc.RootElement.GetRawText())??throw new BoundaryFailure("PEER_RECEIPT_DESERIALIZE");
    }

    private static void ValidatePeerReceipt(PeerReceipt receipt,string session,string nonceDigest)
    {
        Need(receipt.Schema==PeerReceiptSchema&&receipt.SessionId==session&&receipt.NonceSha256==nonceDigest,"PEER_RECEIPT_BINDING");
        Need(receipt.PeerAddress==PeerAddressLiteral&&receipt.ProofNodeAddress==ProofNodeAddressLiteral&&receipt.ManagementPort==ManagementPort&&receipt.TargetPort==TargetPort,"PEER_RECEIPT_TOPOLOGY");
        Need(receipt.ParentControlAccepted&&receipt.ParentControlRemoteAddress==ProofNodeAddressLiteral,"PEER_PARENT_CONTROL");
        Need(receipt.TargetAcceptsTotal==1&&receipt.ChildWindowAcceptedConnections==0,"PEER_CHILD_WINDOW_ACCEPTS");
        Need(receipt.ChildWindowOpenedUtc is not null&&receipt.ChildWindowClosedUtc is not null&&receipt.ChildWindowClosedUtc>=receipt.ChildWindowOpenedUtc&&receipt.ChildWindowClosedUtc-receipt.ChildWindowOpenedUtc<=TimeSpan.FromSeconds(20),"PEER_CHILD_WINDOW_TIME");
        Need(receipt.ProtocolSatisfied&&!receipt.DnsUsed&&!receipt.InternetTargetUsed&&!receipt.ExternalServiceUsed&&!receipt.NetworkConfigurationMutated&&!receipt.ProofClaimed&&receipt.FailureStage is null,"PEER_RECEIPT_NON_EFFECTS");
    }

    private static ChildEvidence ParseChild(byte[] bytes)
    {
        Need(bytes.Length is >0 and <=8192,"CHILD_RECEIPT_SIZE");using JsonDocument doc=JsonDocument.Parse(bytes);Need(doc.RootElement.ValueKind==JsonValueKind.Object,"CHILD_RECEIPT_OBJECT");
        ValidateKeys(doc.RootElement,["Schema","ContractDigestSha256","PackageSid","CapabilityCount","TargetAddress","TargetPort","DiagnoseHasRequiredCapability","DiagnoseInfoReturn","DiagnoseInfoType","DiagnosticClassificationUsedAsProof","SocketOutcome","SocketCode","SocketBehaviorUsedAsProof","ReadAllowed","ReadDenied","WriteDenied","ChildDenied","ChildErrorCode"],"CHILD_RECEIPT_KEYS");
        ChildEvidence child=JsonSerializer.Deserialize<ChildEvidence>(doc.RootElement.GetRawText())??throw new BoundaryFailure("CHILD_RECEIPT_DESERIALIZE");ValidateChild(child);return child;
    }

    private static void ValidateChild(ChildEvidence child)
    {
        Need(child.Schema==ChildSchema&&child.ContractDigestSha256==ContractDigest&&child.PackageSid.StartsWith("S-1-15-2-",StringComparison.Ordinal)&&child.CapabilityCount==0,"CHILD_IDENTITY");
        Need(child.TargetAddress==PeerAddressLiteral&&child.TargetPort==TargetPort,"CHILD_TARGET");
        Need(!child.DiagnoseHasRequiredCapability&&child.DiagnoseInfoReturn==0&&child.DiagnoseInfoType==1&&child.DiagnosticClassificationUsedAsProof,"CHILD_PRIVATE_NETWORK_DIAGNOSTIC");
        Need(child.SocketOutcome is "TIMEOUT" or "SOCKET_ERROR" or "REFUSED","CHILD_SOCKET_OUTCOME");Need(child.SocketOutcome!="CONNECTED"&&!child.SocketBehaviorUsedAsProof,"CHILD_SOCKET_ROLE");
        Need(child.ReadAllowed&&child.ReadDenied&&child.WriteDenied&&child.ChildDenied&&child.ChildErrorCode is 5 or 367,"CHILD_LOCAL_BOUNDARY");
    }

    private static void ValidateProof(ProofEvidence proof)
    {
        Need(proof.Schema==ProofSchema&&proof.Status=="OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN"&&proof.OsPrivateNetworkIsolationProven,"PROOF_STATUS");
        Need(IsHex(proof.SourceHead,40)&&proof.Predecessor==Predecessor&&proof.NativeBoundaryBlob==ExactNativeBoundaryBlob&&proof.ContractDigestSha256==ContractDigest,"PROOF_SOURCE");
        Need(proof.PeerProtocolSourceSha256==QualifiedPeerSourceSha256&&proof.PeerContractSha256==QualifiedPeerContractSha256,"PROOF_PEER_IDENTITY");
        Need(proof.PrivateNetworkProofBasis==ProofBasis&&proof.DiagnosticClassificationUsedAsProof&&proof.PeerZeroAcceptUsedAsProof&&!proof.SocketBehaviorUsedAsProof&&!proof.SocketTimeoutPromotedToProof,"PROOF_EVIDENCE_ROLES");
        ValidateTopology(proof.Topology??throw new BoundaryFailure("PROOF_TOPOLOGY_ABSENT"));Need(Guid.TryParseExact(proof.SessionId,"N",out _)&&IsHex(proof.NonceSha256,64),"PROOF_SESSION_BINDING");
        ValidatePeerReceipt(proof.Peer??throw new BoundaryFailure("PROOF_PEER_RECEIPT_ABSENT"),proof.SessionId,proof.NonceSha256);
        Need(proof.ProfileName.StartsWith(ProfilePrefix,StringComparison.Ordinal)&&proof.PackageSid.StartsWith("S-1-15-2-",StringComparison.Ordinal)&&IsHex(proof.ChildExecutableSha256,64),"PROOF_PROFILE_CHILD");
        NativeBoundary.ValidateToken(proof.Token??throw new BoundaryFailure("PROOF_TOKEN_ABSENT"));Need(proof.JobLimitsVerified,"PROOF_JOB_LIMITS");
        var child=proof.Child??throw new BoundaryFailure("PROOF_CHILD_ABSENT");ValidateChild(child);Need(child.PackageSid==proof.PackageSid,"PROOF_CHILD_SID");
        Need(proof.ProcessCreated&&proof.ProcessExited&&proof.ProcessExitCode==0&&proof.ProfileCreated&&proof.ProfileRemoved&&proof.CleanupSucceeded,"PROOF_TERMINAL");
        Need(!proof.DnsUsed&&!proof.InternetTargetUsed&&!proof.ExternalServiceUsed&&!proof.NetworkIsolationConfigMutated&&!proof.FirewallRuleMutated&&!proof.RouteOrInterfaceMutated&&!proof.GlobalWindowsPolicyMutated,"PROOF_NON_EFFECTS");
        Need(!proof.ModelStarted&&!proof.GameAccessed&&!proof.ProductionProviderRegistered,"PROOF_AUTHORITY_WIDENING");
    }

    private static TopologyObservation ObserveLocalTopology()
    {
        var addresses=new List<string>();foreach(NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties props;try{props=ni.GetIPProperties();}catch(NetworkInformationException){continue;}
            foreach(UnicastIPAddressInformation u in props.UnicastAddresses)if(u.Address.AddressFamily==AddressFamily.InterNetwork)addresses.Add(u.Address.ToString());
        }
        return ObserveSyntheticTopology(addresses);
    }

    private static TopologyObservation ObserveSyntheticTopology(IEnumerable<string> addresses)
    {
        string[] sorted=addresses.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        string digest=Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\n",sorted)));
        return new TopologyObservation(sorted.Contains(ProofNodeAddressLiteral,StringComparer.Ordinal),!sorted.Contains(PeerAddressLiteral,StringComparer.Ordinal),digest);
    }

    private static void ValidateTopology(TopologyObservation topology)=>Need(topology.ProofNodeAddressPresent&&topology.PeerAddressAbsentLocally&&IsHex(topology.LocalIpv4SetSha256,64),"PROOF_NODE_TOPOLOGY_REFUSED");

    private static (string ProfileName,string PackageSid) BoundaryIdentity(NativeBoundary boundary)
    {
        var type=typeof(NativeBoundary);var pf=type.GetField("profile",BindingFlags.Instance|BindingFlags.NonPublic)??throw new BoundaryFailure("PROFILE_REFLECTION_BINDING");var sf=type.GetField("packageSid",BindingFlags.Instance|BindingFlags.NonPublic)??throw new BoundaryFailure("PACKAGE_SID_REFLECTION_BINDING");
        string profile=pf.GetValue(boundary) as string??throw new BoundaryFailure("PROFILE_NAME_ABSENT");object raw=sf.GetValue(boundary)??throw new BoundaryFailure("PACKAGE_SID_ABSENT");nint ptr=raw is IntPtr p?p:throw new BoundaryFailure("PACKAGE_SID_SHAPE");Need(ptr!=0,"PACKAGE_SID_ABSENT");return(profile,new SecurityIdentifier(ptr).Value);
    }

    private static string CurrentPackageSid(out int capabilityCount)
    {
        Win(OpenProcessToken(GetCurrentProcess(),8,out nint token),"CHILD_TOKEN_QUERY");try{using var sid=QueryToken(token,31);using var caps=QueryToken(token,30);nint sidPtr=Marshal.ReadIntPtr(sid.Pointer);Need(sidPtr!=0,"CHILD_PACKAGE_SID_ABSENT");capabilityCount=Marshal.ReadInt32(caps.Pointer);Need(capabilityCount>=0,"CHILD_CAPABILITY_SHAPE");return new SecurityIdentifier(sidPtr).Value;}finally{CloseHandle(token);}
    }

    private static TokenBuffer QueryToken(nint token,int kind)
    {
        bool initial=GetTokenInformation(token,kind,0,0,out uint needed);int error=Marshal.GetLastWin32Error();if(initial||error!=122||needed is 0 or >16384)throw new BoundaryFailure("CHILD_TOKEN_SIZE_QUERY_"+kind,error);var buffer=new TokenBuffer((int)needed);try{Win(GetTokenInformation(token,kind,buffer.Pointer,needed,out uint written),"CHILD_TOKEN_QUERY");Need(written<=needed,"CHILD_TOKEN_SIZE_CHANGED");return buffer;}catch{buffer.Dispose();throw;}
    }

    private sealed class TokenBuffer(int size):IDisposable{internal nint Pointer{get;}=Marshal.AllocHGlobal(size);public void Dispose()=>Marshal.FreeHGlobal(Pointer);}

    private static byte[] ReadBounded(Stream stream){using var buffer=new MemoryStream();var chunk=new byte[1024];while(true){int n=stream.Read(chunk);if(n==0)break;Need(buffer.Length+n<=8192,"CHILD_OUTPUT_LIMIT");buffer.Write(chunk,0,n);}return buffer.ToArray();}
    private static byte[] ReadLine(Stream stream,int limit){using var buffer=new MemoryStream();while(true){int b=stream.ReadByte();if(b<0)throw new BoundaryFailure("LINE_EOF");if(b=='\n')break;if(b==0)throw new BoundaryFailure("LINE_NUL");Need(buffer.Length<limit,"LINE_TOO_LARGE");buffer.WriteByte((byte)b);}byte[] bytes=buffer.ToArray();if(bytes.Length>0&&bytes[^1]=='\r')bytes=bytes[..^1];_ = new UTF8Encoding(false,true).GetString(bytes);return bytes;}
    private static void ValidateKeys(JsonElement root,string[] expected,string stage){string[] names=root.EnumerateObject().Select(p=>p.Name).ToArray();Need(names.Length==expected.Length&&names.Distinct(StringComparer.Ordinal).Count()==names.Length&&names.ToHashSet(StringComparer.Ordinal).SetEquals(expected),stage);}
    private static void MustRefuse(Action action){bool refused=false;try{action();}catch(Exception e)when(e is BoundaryFailure or JsonException or InvalidDataException or FormatException){refused=true;}Need(refused,"HOSTILE_CASE_ACCEPTED");}
    private static void Need(bool ok,string stage){if(!ok)throw new BoundaryFailure(stage);}
    private static void Win(bool ok,string stage){if(!ok)throw new BoundaryFailure(stage,Marshal.GetLastWin32Error());}
    private static bool IsHex(string value,int length)=>value.Length==length&&value.All(c=>c is>='0' and<='9' or>='a' and<='f');
    private static bool IsLowerHex(string value,int length)=>IsHex(value,length);
    private static string Sha256Hex(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [DllImport("Firewallapi.dll",CharSet=CharSet.Unicode)]private static extern uint NetworkIsolationDiagnoseConnectFailure(string serverName);
    [DllImport("Firewallapi.dll",CharSet=CharSet.Unicode)]private static extern uint NetworkIsolationDiagnoseConnectFailureAndGetInfo(string serverName,out int errorType);
    [DllImport("kernel32.dll")]private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool OpenProcessToken(nint process,uint access,out nint token);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetTokenInformation(nint token,int kind,nint data,uint size,out uint returned);
}
