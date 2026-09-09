using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Matawaka.V0601PublicationPreflight;

namespace Matawaka.V0601FixedPublisher;

// This assembly has a zero-argument operator entry point. Internal injection is used only by
// a separately compiled fixture entry point; there is no CLI/config override for production.
internal sealed record PublishPlan(ProofSpec Proof, string GitExe, string Endpoint,
    string StageRoot, string OutputRoot, BoundFile Preflight, string GitRoot)
{
    internal const string Confirm = "PUBLISH-EXACT-V0601-58B9430-C07409C";
    internal const string MainRef = "refs/heads/main";
    internal const string TagRef = "refs/tags/workbench-v0.60.1-accepted";
    internal const string HistoricalRef = "refs/tags/workbench-v0.60-accepted";
    internal static PublishPlan Fixed() => new(V2.Fixed(), V2.GitPath, Spec.Remote,
        @"K:\Matawaka\Tools\WorkbenchV0601FixedPublicationV1",
        @"K:\Matawaka\Workbench\artifacts\publication-v0601",
        new("artifacts/publication-v0601/preflight-promisor-v2-58b9430fc544998a8e40ba00b6757cc630ba9081.json",
            "c7a7dae81f93c783629f3fc5686aec5a2fb45533d95a5413628effa5763e1671", 6187),
        @"K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit");
    internal string Attempt => Path.Combine(OutputRoot, "attempt-publish-fixed-v1-"+Proof.Accepted.Head+".json");
    internal string Result => Path.Combine(OutputRoot, "publication-fixed-v1-"+Proof.Accepted.Head+".json");
    internal string Outcome => Path.Combine(OutputRoot, "outcome-publish-fixed-v1-"+Proof.Accepted.Head+".json");
}
internal sealed record RemoteState(string? Main, string? Tag, string? Peeled, string? Historical, string? HistoricalPeeled);
internal sealed record NativeResult(int ExitCode, byte[] Out, byte[] Error);
internal sealed record FilePin(string Path, long Bytes, string Sha256);

internal static class Files
{
    internal static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{WriteIndented=true});
    internal static void New(string path, byte[] bytes)
    {
        Safe.NoReparse(path); Safe.Need(!File.Exists(path),"EXISTING_EVIDENCE_NO_OVERWRITE");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);Safe.NoReparse(path);
        using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {f.Write(bytes);f.Flush(true);}
        Safe.Need(Safe.Hash(Safe.Read(path,16*1024*1024))==Safe.Hash(bytes),"NEW_FILE_READBACK_MISMATCH");
    }
    internal static string Digest(string root, string? omit=null)
    {
        var lines=new List<string>();long bytes=0;int count=0;
        void Walk(string p,string prefix,int depth) {
            Safe.NoReparse(p);Safe.Need(depth<=64,"STAGE_DEPTH_LIMIT");
            foreach(var child in Directory.EnumerateFileSystemEntries(p).Order(StringComparer.Ordinal)) {
                Safe.NoReparse(child);Safe.Need(++count<=30000,"STAGE_FILE_LIMIT");var name=Path.GetFileName(child);var relative=prefix+name;
                Safe.Need(Safe.Relative(relative),"STAGE_PATH_REFUSED");
                if(Directory.Exists(child)){Walk(child,relative+"/",depth+1);continue;}
                if(relative==omit)continue;var b=Safe.Read(child,256*1024*1024);bytes+=b.Length;Safe.Need(bytes<=512L*1024*1024,"STAGE_BYTE_LIMIT");
                lines.Add(relative+"\t"+b.Length+"\t"+Safe.Hash(b));
            }
        }
        Walk(root,"",0);return Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n",lines.Order(StringComparer.Ordinal))));
    }
}

// Read-sharing handles prevent in-place changes to existing pinned files on Windows.
// This is not a claim that the entire OS/concurrent namespace is isolated.
internal sealed class ReadLocks : IDisposable
{
    private readonly List<FileStream> held = new();
    internal void Hold(string path)
    {
        Safe.NoReparse(path);held.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
    }
    internal void Tree(string root)
    {
        void Walk(string path,int depth) {
            Safe.NoReparse(path);Safe.Need(depth<=64&&held.Count<30000,"LOCK_TREE_LIMIT");
            foreach(var p in Directory.EnumerateFileSystemEntries(path)) {Safe.NoReparse(p);if(Directory.Exists(p))Walk(p,depth+1);else Hold(p);}
        }
        Walk(root,0);
    }
    public void Dispose(){foreach(var f in held)f.Dispose();held.Clear();}
}
internal static class ToolTree
{
    internal static string Verify(string root, FilePin[] pins, ReadLocks locks)
    {
        Safe.NoReparse(root);Safe.Need(pins.Length>0&&pins.Length<=500,"TOOL_MANIFEST_COUNT");
        var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pin in pins) {
            Safe.Need(expected.Add(pin.Path)&&Safe.Relative(pin.Path),"TOOL_MANIFEST_PATH");var p=Safe.Under(root,pin.Path);locks.Hold(p);
            var bytes=Safe.Read(p,64*1024*1024);Safe.Need(bytes.LongLength==pin.Bytes&&Safe.Hash(bytes)==pin.Sha256,"PINNED_TOOL_TREE_MISMATCH");
        }
        var observed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string path,string prefix,int depth){Safe.NoReparse(path);Safe.Need(depth<=32,"TOOL_TREE_DEPTH");foreach(var p in Directory.EnumerateFileSystemEntries(path)){Safe.NoReparse(p);var rel=prefix+Path.GetFileName(p);if(Directory.Exists(p))Walk(p,rel+"/",depth+1);else Safe.Need(observed.Add(rel)&&expected.Contains(rel),"EXTRA_TOOL_FILE_REFUSED");}}
        Walk(root,"",0);Safe.Need(observed.SetEquals(expected),"TOOL_FILE_SET_MISMATCH");return Safe.Hash(Files.Json(pins));
    }
    internal static FilePin[] Embedded()
    {
        using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("mingit-files.json")??throw new InvalidDataException("TOOL_MANIFEST_MISSING");
        return JsonSerializer.Deserialize<FilePin[]>(s)??throw new InvalidDataException("TOOL_MANIFEST_INVALID");
    }
}
internal static class Credential
{
    internal static string Header(string token)
    {
        Safe.Need(Regex.IsMatch(token,@"\A(?:github_pat_|ghp_)[A-Za-z0-9_]{20,240}\z",RegexOptions.CultureInvariant),"TOKEN_FORMAT_REFUSED");
        return "Authorization: Basic "+Convert.ToBase64String(Encoding.ASCII.GetBytes("x-access-token:"+token));
    }
    internal static string ReadLocal()
    {
        Safe.Need(!Console.IsInputRedirected,"INTERACTIVE_LOCAL_CREDENTIAL_INPUT_REQUIRED");
        Console.WriteLine("Enter the GitHub token locally (hidden). It is NOT written to disk, command arguments or receipts. Escape cancels.");
        var chars=new List<char>();
        try {
            while(true) {
                var key=Console.ReadKey(intercept:true);
                if(key.Key==ConsoleKey.Escape)throw new InvalidDataException("CREDENTIAL_INPUT_CANCELLED");
                if(key.Key==ConsoleKey.Enter){Console.WriteLine();return Header(new string(chars.ToArray()));}
                if(key.Key==ConsoleKey.Backspace){if(chars.Count>0)chars.RemoveAt(chars.Count-1);continue;}
                Safe.Need(key.KeyChar>=32&&key.KeyChar<127&&chars.Count<255,"TOKEN_INPUT_REFUSED");chars.Add(key.KeyChar);
            }
        }finally{for(var i=0;i<chars.Count;i++)chars[i]='\0';chars.Clear();}
    }
}
internal static class Guard
{
    internal static string Quote(string s) => "'"+s.Replace("'","'\"'\"'")+"'";
    internal static string Text(PublishPlan p)
    {
        var h=p.Proof.Accepted.Head;var b=p.Proof.Accepted.Second;var t=p.Proof.TagObject;
        Safe.Need(Safe.Oid(h)&&Safe.Oid(b)&&Safe.Oid(t),"GUARD_IDENTITY_REFUSED");
        return "#!/bin/sh\nset -eu\n[ \"$#\" -eq 2 ] || exit 91\n"+
            "[ \"$1\" = "+Quote(p.Endpoint)+" ] && [ \"$2\" = "+Quote(p.Endpoint)+" ] || exit 92\n"+
            "seen_main=0\nseen_tag=0\nwhile IFS=' ' read -r local_ref local_oid remote_ref remote_oid extra; do\n"+
            "[ -z \"${extra:-}\" ] || exit 93\ncase \"$remote_ref\" in\n"+
            "refs/heads/main)\n[ \"$seen_main\" -eq 0 ] || exit 94\n"+
            $"[ \"$local_ref\" = '{h}' ] && [ \"$local_oid\" = '{h}' ] && [ \"$remote_oid\" = '{b}' ] || exit 95\nseen_main=1\n;;\n"+
            "refs/tags/workbench-v0.60.1-accepted)\n[ \"$seen_tag\" -eq 0 ] || exit 96\n"+
            $"[ \"$local_ref\" = '{t}' ] && [ \"$local_oid\" = '{t}' ] && [ \"$remote_oid\" = '0000000000000000000000000000000000000000' ] || exit 97\nseen_tag=1\n;;\n"+
            "*) exit 98 ;;\nesac\ndone\n[ \"$seen_main\" -eq 1 ] && [ \"$seen_tag\" -eq 1 ] || exit 99\n"+
            "set -C\nprintf '%s\\n' 'V0601_EXACT_ADVERTISEMENT_VERIFIED' > ./guard-consumed\n";
    }
}
internal sealed class IsolatedTransport
{
    private readonly PublishPlan plan;
    private readonly string gitSha;
    internal string Repo => Path.Combine(plan.StageRoot,"transport.git");
    internal string Home => Path.Combine(plan.StageRoot,"empty-home");
    internal string Hook => Path.Combine(Repo,"hooks","pre-push");
    internal int PushCalls {get;private set;}
    internal int ReadCalls {get;private set;}
    internal bool PushMayHaveStarted {get;private set;}
    internal string InitialDigest {get;private set;}="";
    internal IsolatedTransport(PublishPlan p){plan=p;gitSha=Safe.Hash(Safe.Read(p.GitExe,32*1024*1024));}
    internal void Create()
    {
        Safe.NoReparse(plan.StageRoot);Safe.Need(!Directory.Exists(plan.StageRoot)&&!File.Exists(plan.StageRoot),"TRANSPORT_STAGE_ALREADY_EXISTS_NO_REUSE");
        Directory.CreateDirectory(plan.StageRoot);Safe.NoReparse(plan.StageRoot);
        Files.New(Path.Combine(plan.StageRoot,"session.json"),Files.Json(new{Schema="matawaka.fixed-publisher-isolated-view/v0.1",AcceptedHead=plan.Proof.Accepted.Head,TagObject=plan.Proof.TagObject,Endpoint=plan.Endpoint,SecretsIncluded=false}));
        Directory.CreateDirectory(Home);Directory.CreateDirectory(Path.Combine(Repo,"refs","heads"));Directory.CreateDirectory(Path.Combine(Repo,"refs","tags"));Directory.CreateDirectory(Path.Combine(Repo,"objects"));
        Files.New(Path.Combine(Repo,"HEAD"),"ref: refs/heads/unborn-publication-view\n"u8.ToArray());
        Files.New(Path.Combine(Repo,"config"),"[core]\n\trepositoryformatversion = 0\n\tbare = true\n\tlogallrefupdates = false\n"u8.ToArray());
        var source=Safe.Under(plan.Proof.Accepted.Root,".git/objects");int count=0;long total=0;
        void Copy(string path,string relative,int depth) {
            Safe.NoReparse(path);Safe.Need(depth<=64,"OBJECT_COPY_DEPTH");
            foreach(var file in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal)) {
                Safe.NoReparse(file);Safe.Need(++count<=30000,"OBJECT_COPY_COUNT");var rel=relative+Path.GetFileName(file);Safe.Need(Safe.Relative(rel),"OBJECT_COPY_PATH");
                if(Directory.Exists(file)){Copy(file,rel+"/",depth+1);continue;}
                Safe.Need(!rel.EndsWith(".lock",StringComparison.OrdinalIgnoreCase)&&rel!="info/alternates"&&rel!="info/http-alternates","OBJECT_COPY_INDIRECTION_REFUSED");
                var bytes=Safe.Read(file,256*1024*1024);total+=bytes.Length;Safe.Need(total<=512L*1024*1024,"OBJECT_COPY_BYTE_LIMIT");Files.New(Safe.Under(Repo,"objects/"+rel),bytes);
            }
        }
        Copy(source,"",0);Files.New(Hook,Safe.Utf8.GetBytes(Guard.Text(plan)));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(Hook,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        InitialDigest=Files.Digest(Repo);
    }
    internal void Unchanged() => Safe.Need(Files.Digest(Repo,"guard-consumed")==InitialDigest,"ISOLATED_TRANSPORT_BYTES_CHANGED");
    internal bool GuardVerified() => File.Exists(Path.Combine(Repo,"guard-consumed"))&&Safe.Read(Path.Combine(Repo,"guard-consumed")).SequenceEqual("V0601_EXACT_ADVERTISEMENT_VERIFIED\n"u8.ToArray());
    internal string[] ReadArgs() => new[]{"ls-remote","--exit-code",plan.Endpoint,PublishPlan.MainRef,PublishPlan.TagRef,PublishPlan.TagRef+"^{}",PublishPlan.HistoricalRef,PublishPlan.HistoricalRef+"^{}"};
    internal string[] PushArgs() => new[]{"push","--porcelain","--atomic","--no-follow-tags","--no-signed","--recurse-submodules=no","--no-progress",plan.Endpoint,plan.Proof.Accepted.Head+":"+PublishPlan.MainRef,plan.Proof.TagObject+":"+PublishPlan.TagRef};
    internal ProcessStartInfo StartInfo(string[] args,string? header)
    {
        var read=args.SequenceEqual(ReadArgs());var push=args.SequenceEqual(PushArgs());Safe.Need(read||push,"CLOSED_TRANSPORT_COMMAND_REFUSED");Safe.Need(!read||header is null,"CREDENTIALS_FOR_PUBLIC_READ_REFUSED");
        var psi=new ProcessStartInfo{FileName=plan.GitExe,WorkingDirectory=Repo,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        psi.Environment.Clear();
        foreach(var key in new[]{"SystemRoot","WINDIR"}){var v=Environment.GetEnvironmentVariable(key);if(v is not null)psi.Environment[key]=v;}
        var path=OperatingSystem.IsWindows()?string.Join(Path.PathSeparator,Path.Combine(plan.GitRoot,"cmd"),Path.Combine(plan.GitRoot,"mingw64","bin"),Path.Combine(plan.GitRoot,"usr","bin"),Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")??@"C:\Windows","System32")):"/usr/bin:/bin";
        foreach(var (key,value) in new[]{("PATH",path),("HOME",Home),("USERPROFILE",Home),("XDG_CONFIG_HOME",Home),("TEMP",Home),("TMP",Home),("GIT_CONFIG_NOSYSTEM","1"),("GIT_CONFIG_GLOBAL",OperatingSystem.IsWindows()?"NUL":"/dev/null"),("GIT_TERMINAL_PROMPT","0"),("GIT_NO_REPLACE_OBJECTS","1"),("GIT_NO_LAZY_FETCH","1"),("GIT_OPTIONAL_LOCKS","0"),("GIT_PROTOCOL_FROM_USER","0"),("GIT_ALLOW_PROTOCOL","https"),("LC_ALL","C")})psi.Environment[key]=value;
        if(OperatingSystem.IsWindows())psi.Environment["GIT_EXEC_PATH"]=Path.Combine(plan.GitRoot,"mingw64","bin");
        var config=new List<(string,string)>{("core.hooksPath",Path.GetDirectoryName(Hook)!.Replace('\\','/')),("core.fsmonitor","false"),("core.untrackedCache","false"),("core.commitGraph","false"),("credential.helper",""),("credential.interactive","false"),("push.followTags","false"),("push.negotiate","false"),("push.gpgSign","false"),("push.recurseSubmodules","no"),("gc.auto","0"),("maintenance.auto","false"),("protocol.allow","never"),("protocol.https.allow","always"),("protocol.file.allow","never"),("protocol.ext.allow","never"),("http.followRedirects","false"),("http.sslVerify","true"),("http.proxy",""),("http.maxRetries","0"),("http.retryAfter","0"),("http.maxRetryTime","0"),("http.lowSpeedLimit","1"),("http.lowSpeedTime","30")};
        if(OperatingSystem.IsWindows()){config.Add(("http.sslBackend","openssl"));config.Add(("http.sslCAInfo",Path.Combine(plan.GitRoot,"mingw64","etc","ssl","certs","ca-bundle.crt")));}
#if QUALIFICATION
        // Compiled ONLY in the disposable test harness, never in the delivered publisher.
        if(Uri.TryCreate(plan.Endpoint,UriKind.Absolute,out var fixtureUri)&&fixtureUri.IsFile){psi.Environment["GIT_ALLOW_PROTOCOL"]="file";config.RemoveAll(x=>x.Item1=="protocol.file.allow");config.Add(("protocol.file.allow","always"));}
        if(Environment.GetEnvironmentVariable("FIXTURE_TLS_CA") is { } fixtureCa){config.RemoveAll(x=>x.Item1=="http.sslCAInfo");config.Add(("http.sslCAInfo",fixtureCa));}
#endif
        if(header is not null){Safe.Need(header.StartsWith("Authorization: Basic ",StringComparison.Ordinal)&&!header.Any(char.IsControl),"CREDENTIAL_HEADER_REFUSED");config.Add(("http."+plan.Endpoint+".extraHeader",header));}
        psi.Environment["GIT_CONFIG_COUNT"]=config.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for(int i=0;i<config.Count;i++){psi.Environment["GIT_CONFIG_KEY_"+i]=config[i].Item1;psi.Environment["GIT_CONFIG_VALUE_"+i]=config[i].Item2;}
        foreach(var arg in new[]{"--no-lazy-fetch","--no-optional-locks","--no-replace-objects","--git-dir="+Repo}.Concat(args))psi.ArgumentList.Add(arg);
        return psi;
    }
    internal async Task<NativeResult> Read()
    {
        Safe.Need(ReadCalls<2,"READ_BUDGET_CONSUMED");ReadCalls++;return await Native(StartInfo(ReadArgs(),null),false);
    }
    internal async Task<NativeResult> Push(string header)
    {
        Safe.Need(PushCalls==0,"PUSH_ATTEMPT_ALREADY_CONSUMED");PushCalls++;Unchanged();return await Native(StartInfo(PushArgs(),header),true);
    }
    private async Task<NativeResult> Native(ProcessStartInfo psi,bool push)
    {
        Safe.Need(Safe.Hash(Safe.Read(plan.GitExe,32*1024*1024))==gitSha,"GIT_IMAGE_DRIFT");
        using var process=new Process{StartInfo=psi};
        if(push)PushMayHaveStarted=true; // Conservative before Start: uncertainty never authorizes retry.
        Safe.Need(process.Start(),"TRANSPORT_START_FAILED");process.StandardInput.BaseStream.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        async Task<byte[]> Drain(Stream stream,int max){using var data=new MemoryStream();var b=new byte[8192];int n;while((n=await stream.ReadAsync(b,timeout.Token))>0){Safe.Need(data.Length+n<=max,"TRANSPORT_OUTPUT_LIMIT");data.Write(b,0,n);}return data.ToArray();}
        try{var output=Drain(process.StandardOutput.BaseStream,512*1024);var error=Drain(process.StandardError.BaseStream,512*1024);await Task.WhenAll(output,error,process.WaitForExitAsync(timeout.Token));return new(process.ExitCode,await output,await error);}
        catch{try{process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}catch{}throw;}
        finally{psi.Environment.Clear();}
    }
    internal static RemoteState Parse(NativeResult r)
    {
        Safe.Need(r.ExitCode==0,"REMOTE_READ_NOT_SUCCESSFUL");var map=new Dictionary<string,string>(StringComparer.Ordinal);
        var permitted=new[]{PublishPlan.MainRef,PublishPlan.TagRef,PublishPlan.TagRef+"^{}",PublishPlan.HistoricalRef,PublishPlan.HistoricalRef+"^{}"};
        foreach(var row in Safe.Utf8.GetString(r.Out).Split('\n',StringSplitOptions.RemoveEmptyEntries)){
            var f=row.TrimEnd('\r').Split('\t');Safe.Need(f.Length==2&&Safe.Oid(f[0])&&permitted.Contains(f[1])&&map.TryAdd(f[1],f[0]),"REMOTE_REF_OUTPUT_REFUSED");
        }
        return new(map.GetValueOrDefault(PublishPlan.MainRef),map.GetValueOrDefault(PublishPlan.TagRef),map.GetValueOrDefault(PublishPlan.TagRef+"^{}"),map.GetValueOrDefault(PublishPlan.HistoricalRef),map.GetValueOrDefault(PublishPlan.HistoricalRef+"^{}"));
    }
    internal static void Before(PublishPlan p,RemoteState r)=>Safe.Need(r.Main==p.Proof.Accepted.Second&&r.Tag is null&&r.Peeled is null&&r.Historical is null&&r.HistoricalPeeled is null,"REMOTE_PREDECESSOR_OR_TAG_DRIFT");
    internal static bool Target(PublishPlan p,RemoteState r)=>r.Main==p.Proof.Accepted.Head&&r.Tag==p.Proof.TagObject&&r.Peeled==p.Proof.Accepted.Head&&r.Historical is null&&r.HistoricalPeeled is null;
}

internal static class FixedPublisher
{
    internal static V2Snapshot ReadPreflight(PublishPlan p)
    {
        var bytes=Safe.Read(Safe.Under(p.Proof.Accepted.Root,p.Preflight.RelativePath));Safe.Need(bytes.Length==p.Preflight.Bytes&&Safe.Hash(bytes)==p.Preflight.Sha256,"AUDITED_PREFLIGHT_RAW_BYTES_MISMATCH");var j=Safe.Parse(bytes);
        Safe.Eq(j,"Schema","matawaka.workbench-v0601-publication-preflight/v0.2");Safe.Eq(j,"Status",V2.Status);
        Safe.False(j,"PublicationAuthorityCreated","RetryAuthorityCreated","NetworkReadPerformed","RemoteWritePerformed");
        return JsonSerializer.Deserialize<V2Snapshot>(j.GetProperty("Snapshot"))??throw new InvalidDataException("AUDITED_SNAPSHOT_MISSING");
    }
    internal static void Unused(PublishPlan p)
    {
        foreach(var file in new[]{p.Attempt,p.Result,p.Outcome}){Safe.NoReparse(file);Safe.Need(!File.Exists(file),"PUBLICATION_ATTEMPT_OR_RESULT_EXISTS_NO_RETRY");}
        Safe.NoReparse(p.StageRoot);Safe.Need(!Directory.Exists(p.StageRoot)&&!File.Exists(p.StageRoot),"TRANSPORT_STAGE_ALREADY_EXISTS_NO_REUSE");
    }
    internal static void Confirm(string? token,TimeSpan age)
    {
        Safe.Need(token==PublishPlan.Confirm,"PUBLICATION_CONFIRMATION_REQUIRED");Safe.Need(age>=TimeSpan.Zero&&age<=TimeSpan.FromMinutes(5),"PUBLICATION_PREVIEW_EXPIRED");
    }
    internal static async Task<string> Execute(PublishPlan p,V2Snapshot expected,Func<Task<V2Snapshot>> verify,
        string token,TimeSpan age,string credentialHeader,string toolSha,string toolTreeDigest)
    {
        var clock=Stopwatch.StartNew();Confirm(token,age);Unused(p);
        Safe.Need(ReadPreflight(p)==expected&&await verify()==expected,"FRESH_LOCAL_INPUTS_DIFFER_FROM_AUDITED_PREFLIGHT");
        Confirm(token,age+clock.Elapsed);
        var attempt=Files.Json(new{Schema="matawaka.workbench-fixed-publication-attempt/v0.1",State="ATTEMPT_CONSUMED_NO_RETRY",CreatedAt=DateTimeOffset.UtcNow,
            ExplicitConfirmation=token,PreflightSha256=p.Preflight.Sha256,ToolSha256=toolSha,MinGitTreeManifestDigest=toolTreeDigest,
            Endpoint=p.Endpoint,ExpectedOldMain=p.Proof.Accepted.Second,AcceptedHead=p.Proof.Accepted.Head,AcceptedTagObject=p.Proof.TagObject,
            AllowedRefs=new[]{PublishPlan.MainRef,PublishPlan.TagRef},MaxPushInvocations=1,MaxRemoteReadInvocations=2,AtomicRequired=true,ExactAdvertisedOldMainGuardRequired=true,
            CredentialStorageAllowed=false,SourceMutationAllowed=false,SourceGitRefMutationAllowed=false,RetryAuthorized=false,
            Note="Consumption precedes every network operation. This attempt is not a publication-success receipt. Preserve it after any result; no automatic retry or cleanup."});
        Files.New(p.Attempt,attempt);
        var t=new IsolatedTransport(p);RemoteState? before=null,after=null;int? exit=null;bool localStable=false;bool guard=false;
        try {
            t.Create();using var stageLocks=new ReadLocks();stageLocks.Tree(t.Repo);
            Confirm(token,age+clock.Elapsed);before=IsolatedTransport.Parse(await t.Read());IsolatedTransport.Before(p,before);
            Safe.Need(ReadPreflight(p)==expected&&await verify()==expected,"LOCAL_DRIFT_BEFORE_PUSH");Confirm(token,age+clock.Elapsed);Safe.Need(Safe.Hash(Safe.Read(p.Attempt))==Safe.Hash(attempt),"ATTEMPT_BYTES_CHANGED");
            var result=await t.Push(credentialHeader);exit=result.ExitCode;guard=t.GuardVerified();
            // One bounded post-attempt read is permitted even after a rejected/uncertain push, never a push retry.
            after=IsolatedTransport.Parse(await t.Read());localStable=ReadPreflight(p)==expected&&await verify()==expected;t.Unchanged();
            var ok=exit==0&&guard&&IsolatedTransport.Target(p,after)&&localStable;
            if(!ok)throw new InvalidDataException("PUSH_OR_READBACK_NOT_VERIFIED");
            var receipt=Files.Json(new{Schema="matawaka.workbench-fixed-publication-receipt/v0.1",Status="EXACT_V0601_TWO_REF_PUBLICATION_VERIFIED",ObservedAt=DateTimeOffset.UtcNow,
                Version="0.60.1",Endpoint=p.Endpoint,AcceptedHead=p.Proof.Accepted.Head,AcceptedTag=PublishPlan.TagRef,AcceptedTagObject=p.Proof.TagObject,
                PreflightSha256=p.Preflight.Sha256,AttemptReceiptSha256=Safe.Hash(attempt),ToolSha256=toolSha,MinGitTreeManifestDigest=toolTreeDigest,
                Snapshot=expected,RemoteBefore=before,RemoteAfter=after,PushInvocations=t.PushCalls,RemoteReadInvocations=t.ReadCalls,PushExitCode=exit,
                AtomicRequested=true,ExactAdvertisedOldMainGuardVerified=guard,IsolatedTransportContextVerified=true,SourceGitStoreUnchanged=localStable,
                NetworkReadPerformed=true,RemoteWritePerformed=true,CredentialUsedForExactPushOnly=true,CredentialStored=false,
                SourceMutationPerformed=false,SourceGitRefMutationPerformed=false,SourceConfigMutationPerformed=false,SourceIndexMutationPerformed=false,SourceObjectMutationPerformed=false,
                HistoricalReceiptsUnchanged=true,CommitOrTagRecreated=false,RetryAuthorized=false,FurtherPublicationAuthorized=false,
                OsNetworkIsolationProven=false,OsConcurrencyExclusionProven=false,WholeRuntimeBinaryTreeVerified=false,
                Note="One ordinary atomic push with exact advertised predecessor guard, exact local raw objects, and one successful post-readback. Independent GitHub readback is still required for external closure. No application/runtime/model/display/action authority is granted."});
            Files.New(p.Result,receipt);return "EXACT_V0601_TWO_REF_PUBLICATION_VERIFIED";
        }catch(Exception ex) {
            var code=ex is InvalidDataException?ex.Message:ex is OperationCanceledException?"TRANSPORT_TIMEOUT":ex.GetType().Name;
            var status=t.PushMayHaveStarted?"PUBLICATION_OUTCOME_UNVERIFIED_NO_RETRY":"STOPPED_BEFORE_PUSH_NO_RETRY";
            var outcome=Files.Json(new{Schema="matawaka.workbench-fixed-publication-outcome/v0.1",Status=status,ObservedAt=DateTimeOffset.UtcNow,SafeReason=code,
                PreflightSha256=p.Preflight.Sha256,AttemptReceiptSha256=Safe.Hash(attempt),PushInvocations=t.PushCalls,RemoteReadInvocations=t.ReadCalls,
                PushMayHaveStarted=t.PushMayHaveStarted,PushExitCode=exit,RemoteBefore=before,RemoteAfter=after,ExactAdvertisedOldMainGuardObserved=guard,
                LocalSnapshotReverified=localStable,PublicationSuccessClaimed=false,RemoteMutationProvenAbsent=!t.PushMayHaveStarted,RetryAuthorized=false,
                Note="No retry, fallback, rollback, reset or evidence overwrite. An unverified outcome is not proof that no remote effect occurred. Raw transport output and credential values are deliberately omitted."});
            try{Files.New(p.Outcome,outcome);}catch{ /* Original consumed attempt remains the minimum durable evidence. */ }
            throw new InvalidDataException(status+"__"+code);
        }
    }
}
internal static class PublisherEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length==0,"ARGUMENTS_NOT_ACCEPTED");Safe.Need(OperatingSystem.IsWindows(),"WINDOWS_OPERATOR_HOST_REQUIRED");
            var p=PublishPlan.Fixed();FixedPublisher.Unused(p);var self=Environment.ProcessPath??throw new InvalidDataException("SELF_PATH_MISSING");Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(p.Proof.Accepted.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"PUBLISHER_MUST_BE_OUTSIDE_REPOSITORY");
            using var tools=new ReadLocks();tools.Hold(self);var toolSha=Safe.Hash(Safe.Read(self,128*1024*1024));var toolTree=ToolTree.Verify(p.GitRoot,ToolTree.Embedded(),tools);
            var git=new GitRead(p.GitExe,p.Proof.Accepted.Root,V2.GitSha);Safe.Need(await git.Text("--version")=="git version 2.55.0.windows.4","PINNED_GIT_VERSION_MISMATCH");
            var expected=FixedPublisher.ReadPreflight(p);Safe.Need(await V2.Check(p.Proof,git)==expected,"CURRENT_STATE_DIFFERS_FROM_AUDITED_PREFLIGHT");
            Console.WriteLine($"LOCAL PUBLICATION PREVIEW — NO REMOTE OR CREDENTIAL ACCESS YET\nRepository: {p.Proof.Accepted.Root}\nDestination: {p.Endpoint}\nmain: {p.Proof.Accepted.Second} -> {p.Proof.Accepted.Head}\nTag: ABSENT -> {p.Proof.TagObject}\nTag ref: {PublishPlan.TagRef}\nPreflight SHA-256: {p.Preflight.Sha256}\n");
            Console.WriteLine("Effects after confirmation: new local attempt/result files and isolated transport copy; at most two public ref reads and ONE atomic exact two-ref push. Token is entered locally, hidden, used in child environment only. No stored credentials are read. No source/config/index/tag changes, no force, no retry, no Update/Accept.\nA timeout or ambiguous result MUST STOP; do not repeat the utility.\n");
            Console.WriteLine("Enter exactly: "+PublishPlan.Confirm);var timer=Stopwatch.StartNew();var token=Console.ReadLine();
            if(token!=PublishPlan.Confirm){Console.WriteLine("CANCELLED_NO_WRITE_NO_REMOTE");return 2;}
            FixedPublisher.Confirm(token,timer.Elapsed);var header=Credential.ReadLocal();FixedPublisher.Confirm(token,timer.Elapsed);
            using var sourceLocks=new ReadLocks();sourceLocks.Tree(Safe.Under(p.Proof.Accepted.Root,".git"));
            var status=await FixedPublisher.Execute(p,expected,()=>V2.Check(p.Proof,git),token,timer.Elapsed,header,toolSha,toolTree);
            header="";Console.WriteLine($"COMPLETED: {status}\nReceipt: {p.Result}\nAttempt: {p.Attempt}\nSTOP. Return both JSON files for independent readback. Do not repeat.");return 0;
        }catch(Exception ex) {
            Console.Error.WriteLine("REFUSED: "+(ex is InvalidDataException?ex.Message:ex.GetType().Name));
            Console.Error.WriteLine("STOP. Preserve attempt/outcome files. A push may have occurred if an attempt was consumed; do not retry, remove evidence, reset, or push manually.");return 1;
        }
    }
}
