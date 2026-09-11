using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workbench.IsolationQualification;

internal static class Probe
{
    private static readonly DateTimeOffset WindowStart=DateTimeOffset.Parse("2026-09-11T20:03:09Z");
    private static readonly DateTimeOffset WindowEnd=DateTimeOffset.Parse("2026-09-11T22:03:09Z");
    private static void CheckWindow()=>NativeBoundary.Need(DateTimeOffset.UtcNow>=WindowStart&&DateTimeOffset.UtcNow<WindowEnd,"AUTHORIZATION_WINDOW_CLOSED");
    internal static string ExitClassification(uint? code)=>code switch
    {
        0=>"CHILD_EXIT_ZERO_REQUIRES_RECEIPT",
        0xc0000142=>"CHILD_DLL_INITIALIZATION_FAILED",
        null=>"CHILD_EXIT_UNOBSERVED",
        _=>"CHILD_EXIT_NONZERO"
    };
    internal static JsonElement ReadReceipt(byte[] bytes)
    {
        NativeBoundary.Need(bytes.Length is >0 and <=8192,"CHILD_RECEIPT_SIZE");
        using var parsed=JsonDocument.Parse(bytes);var value=parsed.RootElement;
        NativeBoundary.Need(value.ValueKind==JsonValueKind.Object,"CHILD_RECEIPT_OBJECT");
        var names=value.EnumerateObject().Select(p=>p.Name).ToArray();
        string[] expected=["schema","readAllowed","readDenied","writeDenied","childDenied","networkDenied","socketCode","childErrorCode","socketFailure"];
        NativeBoundary.Need(names.Length==expected.Length&&names.Distinct().Count()==expected.Length&&names.ToHashSet().SetEquals(expected),"CHILD_RECEIPT_KEYS");
        NativeBoundary.Need(value.GetProperty("schema").GetString()=="workbench.appcontainer-probe-child/v0.3","CHILD_RECEIPT_SCHEMA");
        foreach(var key in expected.Skip(1).Take(5))NativeBoundary.Need(value.GetProperty(key).ValueKind is JsonValueKind.True or JsonValueKind.False,"CHILD_RECEIPT_BOOLEAN");
        foreach(var key in new[]{"socketCode","childErrorCode"})NativeBoundary.Need(value.GetProperty(key).ValueKind==JsonValueKind.Null||value.GetProperty(key).TryGetInt32(out _),"CHILD_RECEIPT_CODE");
        NativeBoundary.Need(value.GetProperty("socketFailure").GetString() is "NONE" or "SOCKET_ERROR" or "TIMEOUT" or "TYPE_INITIALIZATION" or "PLATFORM_UNSUPPORTED" or "OTHER","SOCKET_FAILURE_CLASS");
        return value.Clone();
    }
    internal static JsonElement ValidateReceipt(byte[] bytes)
    {
        var value=ReadReceipt(bytes);
        foreach(var key in new[]{"readAllowed","readDenied","writeDenied","childDenied","networkDenied"})NativeBoundary.Need(value.GetProperty(key).ValueKind==JsonValueKind.True,"CHILD_BOUNDARY_REFUSED");
        NativeBoundary.Need(value.GetProperty("socketCode").GetInt32()==10013,"NETWORK_ACCESS_DENIAL_REQUIRED");
        NativeBoundary.Need(value.GetProperty("socketFailure").GetString()=="SOCKET_ERROR","NETWORK_ERROR_CLASS_REQUIRED");
        NativeBoundary.Need(value.GetProperty("childErrorCode").GetInt32() is 5 or 367,"CHILD_ACCESS_DENIAL_REQUIRED");
        return value;
    }
    private static int Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--unit"}))return Unit();
        if(args.SequenceEqual(new[]{"--escape-canary"}))return 17;
        if(args.Length==2&&args[0]=="--child"&&int.TryParse(args[1],out int port)&&port is >0 and <=65535)return Child(port);
        if(args.Length==3&&args[0]=="--native-trial")return Parent(args[1],args[2]);
        Console.WriteLine("EXPLICIT_TEST_MODE_REQUIRED");return 64;
    }

    private static int Unit()
    {
        int pass=0;
        var valid=new TokenObservation(true,0,true,true,false,true);
        NativeBoundary.ValidateToken(valid);pass++;
        const uint jobFlags=0x8|0x100|0x400|0x2000;
        NativeBoundary.ValidateJob(jobFlags,1,512*1024*1024);pass++;
        foreach(var bad in new (uint Flags,uint Active,nuint Memory)[]{(0,1,512*1024*1024),(jobFlags&~0x2000u,1,512*1024*1024),(jobFlags,2,512*1024*1024),(jobFlags,0,512*1024*1024),(jobFlags,1,0),(jobFlags,1,1024*1024*1024)})
        {
            bool refused=false;try{NativeBoundary.ValidateJob(bad.Flags,bad.Active,bad.Memory);}catch(BoundaryFailure){refused=true;}
            NativeBoundary.Need(refused,"HOSTILE_JOB_ACCEPTED");pass++;
        }
        foreach(var pair in new (uint? Code,string Expected)[]{(0,"CHILD_EXIT_ZERO_REQUIRES_RECEIPT"),(0xc0000142,"CHILD_DLL_INITIALIZATION_FAILED"),(null,"CHILD_EXIT_UNOBSERVED"),(2,"CHILD_EXIT_NONZERO"),(90,"CHILD_EXIT_NONZERO")})
        {NativeBoundary.Need(ExitClassification(pair.Code)==pair.Expected,"EXIT_CLASSIFICATION");pass++;}
        var receipt=new Dictionary<string,object> { ["schema"]="workbench.appcontainer-probe-child/v0.3",
            ["readAllowed"]=true,["readDenied"]=true,["writeDenied"]=true,["childDenied"]=true,["networkDenied"]=true,["socketCode"]=10013,["childErrorCode"]=5,["socketFailure"]="SOCKET_ERROR" };
        _=ValidateReceipt(JsonSerializer.SerializeToUtf8Bytes(receipt));pass++;
        _=ValidateReceipt(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,object>(receipt){["childErrorCode"]=367}));pass++;
        _=ValidateReceipt(JsonSerializer.SerializeToUtf8Bytes(receipt.Reverse().ToDictionary(p=>p.Key,p=>p.Value)));pass++;
        void Reject(byte[] bytes)
        {
            bool rejected=false;
            try{_=ValidateReceipt(bytes);}catch(Exception e)when(e is BoundaryFailure or JsonException or InvalidOperationException or FormatException){rejected=true;}
            NativeBoundary.Need(rejected,"HOSTILE_RECEIPT_ACCEPTED");pass++;
        }
        foreach(var key in receipt.Keys)
        {
            var missing=new Dictionary<string,object>(receipt);missing.Remove(key);Reject(JsonSerializer.SerializeToUtf8Bytes(missing));
        }
        foreach(var key in new[]{"readAllowed","readDenied","writeDenied","childDenied","networkDenied"})
            foreach(var bad in new object[]{false,"true",1})
            {var changed=new Dictionary<string,object>(receipt){[key]=bad};Reject(JsonSerializer.SerializeToUtf8Bytes(changed));}
        foreach(var bad in new object[]{10061,10060,0,"10013"})
        {var changed=new Dictionary<string,object>(receipt){["socketCode"]=bad};Reject(JsonSerializer.SerializeToUtf8Bytes(changed));}
        foreach(var extra in new[]{"actionPermit","capabilities","authority"})
        {var changed=new Dictionary<string,object>(receipt){[extra]=true};Reject(JsonSerializer.SerializeToUtf8Bytes(changed));}
        var validJson=JsonSerializer.Serialize(receipt);
        Reject(Encoding.UTF8.GetBytes(validJson[..^1]+",\"networkDenied\":true}"));
        Reject(Encoding.UTF8.GetBytes(validJson.Replace("v0.3","v0.2",StringComparison.Ordinal)));
        foreach(var bad in new[]{"NONE","TIMEOUT","TYPE_INITIALIZATION","PLATFORM_UNSUPPORTED","OTHER","AUTHORITY"})
        {var changed=new Dictionary<string,object>(receipt){["socketFailure"]=bad};Reject(JsonSerializer.SerializeToUtf8Bytes(changed));}
        foreach(var bad in new object[]{0,2,24,203,1282,"5"})
        {var changed=new Dictionary<string,object>(receipt){["childErrorCode"]=bad};Reject(JsonSerializer.SerializeToUtf8Bytes(changed));}
        foreach(var bad in new[]{"null","[]","{}","{","true"})Reject(Encoding.UTF8.GetBytes(bad));
        Reject([]);Reject(new byte[8193]);
        var readRule=new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-1-0"),
            System.Security.AccessControl.FileSystemRights.ReadAndExecute,System.Security.AccessControl.AccessControlType.Allow);
        NativeBoundary.Need(readRule.FileSystemRights==(System.Security.AccessControl.FileSystemRights.ReadAndExecute|System.Security.AccessControl.FileSystemRights.Synchronize),"ALLOW_SYNCHRONIZE_REPRESENTATION");pass++;
        foreach(var t in new[]{valid with{AppContainer=false},valid with{Capabilities=1},valid with{Capabilities=-1},
            valid with{PackageMatches=false},valid with{LowIntegrity=false},valid with{Elevated=true},valid with{InOwnedJob=false}})
        {
            try{NativeBoundary.ValidateToken(t);return 1;}
            catch(BoundaryFailure e)when(e.Message=="TOKEN_BOUNDARY_REFUSED"){pass++;}
        }
        foreach(var path in new[]{"relative","\\\\server\\share","."})
        {
            try{NativeBoundary.ValidateRoot(path);return 1;}
            catch(BoundaryFailure e)when(e.Message=="ROOT_LOCAL_REQUIRED"){pass++;}
        }
        Console.WriteLine(JsonSerializer.Serialize(new{status="NATIVE_PROBE_PURE_CONTROLS_PASS",passed=pass,nativeCalls=false}));return 0;
    }

    private static int Child(int port)
    {
        // Only synthetic files in the current trial directory; no arbitrary paths, game/model/user input.
        bool readAllowed=false,readDenied=false,writeDenied=false,childDenied=false,networkDenied=false;
        int? socketCode=null,childErrorCode=null;
        string socketFailure="NONE";
        try{readAllowed=File.ReadAllText("allow-read.txt")=="SYNTHETIC_ALLOWED";}catch{ }
        try{_=File.ReadAllText("deny-read.txt");}catch(UnauthorizedAccessException){readDenied=true;}
        try{File.WriteAllText("deny-write.txt","UNEXPECTED_WRITE");}catch(UnauthorizedAccessException){writeDenied=true;}
        try
        {
            using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
            var task=socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback,port));
            task.WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
        }
        catch(SocketException e){socketFailure="SOCKET_ERROR";socketCode=e.ErrorCode;networkDenied=e.SocketErrorCode==SocketError.AccessDenied;}
        catch(TimeoutException){socketFailure="TIMEOUT";}
        catch(TypeInitializationException){socketFailure="TYPE_INITIALIZATION";}
        catch(PlatformNotSupportedException){socketFailure="PLATFORM_UNSUPPORTED";}
        catch{socketFailure="OTHER";} // No messages, paths or parent environment are recorded.
        try
        {
            var start=new ProcessStartInfo {FileName=Path.Combine(Environment.CurrentDirectory,"Workbench.AppContainerProbe.exe"),UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add("--escape-canary");
            using var child=Process.Start(start);
            if(child is not null&&!child.WaitForExit(1000)){child.Kill();child.WaitForExit(1000);}
        }
        catch(System.ComponentModel.Win32Exception e){childErrorCode=e.NativeErrorCode;childDenied=e.NativeErrorCode is 5 or 367;}
        var result=new{schema="workbench.appcontainer-probe-child/v0.3",readAllowed,readDenied,writeDenied,childDenied,networkDenied,socketCode,childErrorCode,socketFailure};
        Console.WriteLine(JsonSerializer.Serialize(result));
        return readAllowed&&readDenied&&writeDenied&&childDenied&&networkDenied?0:2;
    }

    private static byte[] ReadBounded(Stream stream)
    {
        using var buffer=new MemoryStream();var chunk=new byte[1024];
        while(true){int n=stream.Read(chunk);if(n==0)break;NativeBoundary.Need(buffer.Length+n<=8192,"CHILD_OUTPUT_LIMIT");buffer.Write(chunk,0,n);}
        return buffer.ToArray();
    }
    private static int Parent(string directory,string manifestPath)
    {
        CheckWindow();
        string root=NativeBoundary.ValidateRoot(directory);
        string report=Path.Combine(Path.GetDirectoryName(root)!,"NATIVE-RESULT.json");
        NativeBoundary.Need(!File.Exists(report),"CREATE_ONLY_RESULT_REQUIRED");
        // Atomic one-attempt marker precedes every native mutation, including profile creation.
        using(var attempt=new FileStream(Path.Combine(Path.GetDirectoryName(root)!,"NATIVE-ATTEMPT.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            JsonSerializer.Serialize(attempt,new{schema="workbench.native-test-attempt/v0.1",observedAt=DateTimeOffset.UtcNow,
                windowEnd=WindowEnd,maximumTestChildren=1,modelAuthorized=false,source="CURRENT_HUMAN_TWO_HOUR_DEVELOPMENT_REQUEST"});
        using var document=JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var hashes=document.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
        var boundary=new NativeBoundary();
        var listener=new TcpListener(IPAddress.Loopback,0);
        bool control=false,unexpectedConnection=false,sentinelsUnchanged=false;
        string status="INCOMPLETE",stage="START";int? nativeCode=null;
        string? stdoutSha=null,stderrSha=null;JsonElement? child=null;
        var timer=Stopwatch.StartNew();
        try
        {
            stage="PREPARE";boundary.Prepare(root,hashes);CheckWindow();
            stage="LOOPBACK_CONTROL";listener.Start(1);
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            using(var normal=new TcpClient()){normal.Connect(IPAddress.Loopback,port);using var accepted=listener.AcceptTcpClient();control=accepted.Connected;}
            stage="NATIVE_START";boundary.Start(root,port);
            var output=Task.Run(()=>ReadBounded(boundary.Output));var error=Task.Run(()=>ReadBounded(boundary.Error));
            stage="WAIT_CHILD";NativeBoundary.Need(boundary.Wait(10000),"CHILD_TIMEOUT");
            unexpectedConnection=listener.Pending();
            var outBytes=output.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var errBytes=error.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            stdoutSha=Convert.ToHexString(SHA256.HashData(outBytes)).ToLowerInvariant();
            stderrSha=Convert.ToHexString(SHA256.HashData(errBytes)).ToLowerInvariant();
            // A closed diagnostic receipt may describe failures. It is not a PASS
            // and never substitutes for exit, parent observation or validation.
            if(outBytes.Length>0)child=ReadReceipt(outBytes);
            sentinelsUnchanged=File.ReadAllText(Path.Combine(root,"allow-read.txt"))=="SYNTHETIC_ALLOWED"&&
                File.ReadAllText(Path.Combine(root,"deny-read.txt"))=="SYNTHETIC_DENIED"&&
                File.ReadAllText(Path.Combine(root,"deny-write.txt"))=="SYNTHETIC_UNCHANGED";
            NativeBoundary.Need(boundary.ExitCode==0,ExitClassification(boundary.ExitCode));
            stage="CHILD_RECEIPT";
            child=ValidateReceipt(outBytes);
            sentinelsUnchanged=File.ReadAllText(Path.Combine(root,"allow-read.txt"))=="SYNTHETIC_ALLOWED"&&
                File.ReadAllText(Path.Combine(root,"deny-read.txt"))=="SYNTHETIC_DENIED"&&
                File.ReadAllText(Path.Combine(root,"deny-write.txt"))=="SYNTHETIC_UNCHANGED";
            NativeBoundary.Need(sentinelsUnchanged&&control&&!unexpectedConnection&&boundary.ExitCode==0,"PARENT_OBSERVATION_REFUSED");
            status="APPCONTAINER_TEST_PROCESS_BOUNDARY_OBSERVED_NOT_MODEL_QUALIFICATION";
        }
        catch(BoundaryFailure e){status="FAIL_CLOSED";stage=e.Message;nativeCode=e.NativeCode;}
        catch(Exception e){status="FAIL_CLOSED";stage="MANAGED_"+e.GetType().Name;}
        finally{listener.Stop();boundary.Dispose();}
        if(!boundary.CleanupSucceeded){status="CLEANUP_REQUIRES_ATTENTION";}
        var result=new{schema="workbench.appcontainer-probe-result/v0.1",status,stage,nativeCode,
            elapsedSeconds=timer.Elapsed.TotalSeconds,profileCreated=boundary.ProfileCreated,profileRemoved=boundary.ProfileRemoved,
            processCreated=boundary.ProcessCreated,processExited=boundary.ProcessExited,exitCode=boundary.ExitCode,
            cleanupSucceeded=boundary.CleanupSucceeded,token=boundary.Token,jobLimitsVerified=boundary.JobLimitsVerified,loopbackControl=control,unexpectedConnection,
            sentinelsUnchanged,stdoutSha256=stdoutSha,stderrSha256=stderrSha,child,
            modelStarted=false,gameAccessed=false,globalWindowsPolicyChanged=false,productionIsolationProvider=false,
            realLease=false,display=false};
        using(var file=new FileStream(report,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,result,new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(JsonSerializer.Serialize(result));
        return status=="APPCONTAINER_TEST_PROCESS_BOUNDARY_OBSERVED_NOT_MODEL_QUALIFICATION"?0:3;
    }
}
