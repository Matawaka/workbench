using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;
namespace Matawaka.V0601FixedPublisher;
internal static class PublisherTests
{
    private static readonly List<object> Results=new();
    private static readonly string Git=GitRead.Locate();
    private static string GitRoot=>Path.GetDirectoryName(Path.GetDirectoryName(Git))!;
    private const string DummyToken="github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_TEST_ONLY";
    private static string G(string root,params string[] args)
    {
        var p=new ProcessStartInfo{FileName=Git,WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var k in p.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(k);
        p.Environment["GIT_CONFIG_NOSYSTEM"]="1";p.Environment["GIT_CONFIG_GLOBAL"]=OperatingSystem.IsWindows()?"NUL":"/dev/null";
        foreach(var a in args)p.ArgumentList.Add(a);using var c=Process.Start(p)!;var o=c.StandardOutput.ReadToEnd();var e=c.StandardError.ReadToEnd();c.WaitForExit();if(c.ExitCode!=0)throw new Exception("Fixture setup failed: "+string.Join(' ',args)+" "+e);return o.TrimEnd('\r','\n');
    }
    private sealed class F:IDisposable
    {
        internal readonly V2Tests.F Local=new();
        internal string Root=>Local.Root;
        internal string Temp=Path.Combine(Path.GetTempPath(),"fixed-publication-"+Guid.NewGuid().ToString("N"));
        internal string Remote=>Path.Combine(Temp,"remote.git");
        internal PublishPlan Plan=null!;
        internal V2Snapshot Expected=null!;
        internal async Task Init()
        {
            Directory.CreateDirectory(Temp);G(Temp,"init","--bare","-q",Remote);
            G(Root,"push","-q",new Uri(Remote+Path.DirectorySeparatorChar).AbsoluteUri,Local.Proof.Accepted.Second+":"+PublishPlan.MainRef);
            Expected=await Local.Read();
            var b=Files.Json(new{Schema="matawaka.workbench-v0601-publication-preflight/v0.2",Status=V2.Status,Snapshot=Expected,PublicationAuthorityCreated=false,RetryAuthorityCreated=false,NetworkReadPerformed=false,RemoteWritePerformed=false});
            const string rel="artifacts/publication-v0601/fixture-preflight.json";Files.New(Safe.Under(Root,rel),b);
            Plan=new(Local.Proof,Git,new Uri(Remote+Path.DirectorySeparatorChar).AbsoluteUri,Path.Combine(Temp,"stage"),Path.Combine(Root,"artifacts","publication-v0601"),new(rel,Safe.Hash(b),b.Length),GitRoot);
        }
        internal async Task<string> Run(Func<Task<V2Snapshot>>? verify=null,string? token=null,TimeSpan? age=null)=>await FixedPublisher.Execute(Plan,Expected,verify??Local.Read,token??PublishPlan.Confirm,age??TimeSpan.Zero,Credential.Header(DummyToken),new string('1',64),"fixture-tool-manifest");
        public void Dispose(){Local.Dispose();try{Directory.Delete(Temp,true);}catch{}}
    }
    private static void Need(bool x,string code)=>Safe.Need(x,"TEST_"+code);
    private static async Task Case(string id,Func<Task> action){await action();Results.Add(new{Id=id,Passed=true});Console.WriteLine("PASS "+id);}
    private static async Task Refused(Func<Task> action){try{await action();}catch(InvalidDataException){return;}throw new Exception("Expected refusal");}
    private static void Denied(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Expected policy refusal");}
    private static string Ref(F f,string name)=>G(f.Temp,"--git-dir="+f.Remote,"rev-parse",name);
    private static Dictionary<string,string> AllFiles(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(root,p),p=>Safe.Hash(File.ReadAllBytes(p)));
    private static async Task TransportCase(string id,Action<F> mutate,bool expectSuccess=false)
    {
        await Case(id,async()=>{using var f=new F();await f.Init();var source=V2.StoreDigest(f.Root);var t=new IsolatedTransport(f.Plan);t.Create();mutate(f);
            var r=await t.Push(Credential.Header(DummyToken));Need((r.ExitCode==0)==expectSuccess,id);
            if(expectSuccess)Need(t.GuardVerified()&&Ref(f,PublishPlan.MainRef)==f.Plan.Proof.Accepted.Head&&Ref(f,PublishPlan.TagRef)==f.Plan.Proof.TagObject,"EXACT_REFS");
            Need(source==V2.StoreDigest(f.Root),"SOURCE_STORE_UNCHANGED");await Refused(()=>t.Push(Credential.Header(DummyToken)));});
    }
    internal static async Task Main()
    {
        await Case("complete-orchestrator-exact-atomic-two-ref-and-byte-preservation",async()=>{
            using var f=new F();await f.Init();var before=AllFiles(f.Root);Need(await f.Run()=="EXACT_V0601_TWO_REF_PUBLICATION_VERIFIED","COMPLETE");
            var report=Safe.Parse(Safe.Read(f.Plan.Result));Need(report.GetProperty("PushInvocations").GetInt32()==1&&report.GetProperty("RemoteReadInvocations").GetInt32()==2,"BUDGETS");
            Need(Ref(f,PublishPlan.MainRef)==f.Plan.Proof.Accepted.Head&&Ref(f,PublishPlan.TagRef)==f.Plan.Proof.TagObject&&Ref(f,PublishPlan.TagRef+"^{commit}")==f.Plan.Proof.Accepted.Head,"TARGETS");
            foreach(var (p,h) in before)Need(Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,p)))==h,"PREEXISTING_FILE_UNCHANGED");
            Need(!G(f.Temp,"--git-dir="+f.Remote,"for-each-ref","--format=%(refname)").Contains(PublishPlan.HistoricalRef+"\n"),"NO_OLD_TAG");
            foreach(var p in Directory.EnumerateFiles(f.Temp,"*",SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(f.Plan.OutputRoot))) {
                var bytes=File.ReadAllBytes(p);var text=Encoding.Latin1.GetString(bytes);Need(!text.Contains(DummyToken)&&!text.Contains(Convert.ToBase64String(Encoding.ASCII.GetBytes("x-access-token:"+DummyToken))),"NO_CREDENTIAL_ON_DISK");
            }
            var attempt=Safe.Hash(Safe.Read(f.Plan.Attempt));await Refused(()=>f.Run());Need(Safe.Hash(Safe.Read(f.Plan.Attempt))==attempt,"CONSUMED_ATTEMPT_UNCHANGED");
        });
        await Case("cancel-and-expiry-before-attempt-network-or-stage",async()=>{using var f=new F();await f.Init();var before=AllFiles(f.Root);await Refused(()=>f.Run(token:"CANCEL"));await Refused(()=>f.Run(age:TimeSpan.FromMinutes(6)));Need(before.OrderBy(x=>x.Key).SequenceEqual(AllFiles(f.Root).OrderBy(x=>x.Key))&&!Directory.Exists(f.Plan.StageRoot),"NO_EFFECT");});
        await Case("raw-preflight-tamper-before-effects",async()=>{using var f=new F();await f.Init();File.AppendAllText(Safe.Under(f.Root,f.Plan.Preflight.RelativePath)," ");await Refused(()=>f.Run());Need(!File.Exists(f.Plan.Attempt),"NO_ATTEMPT");});
        await Case("fresh-source-drift-before-attempt",async()=>{using var f=new F();await f.Init();File.AppendAllText(Path.Combine(f.Root,"src.txt"),"drift");await Refused(()=>f.Run());Need(!File.Exists(f.Plan.Attempt),"NO_ATTEMPT");});
        await Case("remote-drift-before-push-consumes-once-and-records-no-push",async()=>{using var f=new F();await f.Init();G(f.Temp,"--git-dir="+f.Remote,"update-ref",PublishPlan.MainRef,f.Plan.Proof.Accepted.First);await Refused(()=>f.Run());var o=Safe.Parse(Safe.Read(f.Plan.Outcome));Need(o.GetProperty("PushInvocations").GetInt32()==0&&!o.GetProperty("PushMayHaveStarted").GetBoolean(),"NO_PUSH");await Refused(()=>f.Run());});
        await Case("local-drift-after-remote-read-before-push",async()=>{using var f=new F();await f.Init();int calls=0;async Task<V2Snapshot> Read(){if(++calls==2)File.AppendAllText(Path.Combine(f.Root,"src.txt"),"drift");return await f.Local.Read();}await Refused(()=>f.Run(Read));var o=Safe.Parse(Safe.Read(f.Plan.Outcome));Need(o.GetProperty("PushInvocations").GetInt32()==0,"NO_PUSH");});
        await TransportCase("transport-exact-advertisement-happy-path",_=>{},true);
        await TransportCase("guard-refuses-ancestor-drift-even-though-fast-forward",f=>G(f.Temp,"--git-dir="+f.Remote,"update-ref",PublishPlan.MainRef,f.Plan.Proof.Accepted.First));
        await TransportCase("guard-refuses-target-main-tag-only-fallback",f=>G(f.Root,"push","-q",f.Plan.Endpoint,f.Plan.Proof.Accepted.Head+":"+PublishPlan.MainRef));
        await TransportCase("guard-refuses-existing-same-annotated-tag",f=>G(f.Root,"push","-q",f.Plan.Endpoint,f.Plan.Proof.TagObject+":"+PublishPlan.TagRef));
        await TransportCase("guard-refuses-existing-conflicting-tag",f=>G(f.Temp,"--git-dir="+f.Remote,"update-ref",PublishPlan.TagRef,f.Plan.Proof.Accepted.Second));
        await TransportCase("atomic-unsupported-refuses-no-fallback",f=>G(f.Temp,"--git-dir="+f.Remote,"config","receive.advertiseAtomic","false"));
        await Case("server-rejects-both-refs-atomic-no-retry",async()=>{using var f=new F();await f.Init();var hook=Path.Combine(f.Remote,"hooks","pre-receive");File.WriteAllText(hook,"#!/bin/sh\nexit 1\n",new UTF8Encoding(false));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(hook,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);await Refused(()=>f.Run());Need(Ref(f,PublishPlan.MainRef)==f.Plan.Proof.Accepted.Second,"ATOMIC_MAIN_UNCHANGED");var o=Safe.Parse(Safe.Read(f.Plan.Outcome));Need(o.GetProperty("PushMayHaveStarted").GetBoolean()&&!o.GetProperty("RemoteMutationProvenAbsent").GetBoolean(),"NO_FALSE_ZERO_EFFECT_CLAIM");await Refused(()=>f.Run());});
        await Case("post-advertisement-main-race-atomic-cas-refusal",async()=>{using var f=new F();await f.Init();var t=new IsolatedTransport(f.Plan);t.Create();File.AppendAllText(t.Hook,"\n"+Guard.Quote(Git.Replace('\\','/'))+" --git-dir="+Guard.Quote(f.Remote.Replace('\\','/'))+" update-ref refs/heads/main "+f.Plan.Proof.Accepted.First+"\n",new UTF8Encoding(false));
            // Recreate the fixture baseline only: production never rebinds a changed hook.
            var prop=typeof(IsolatedTransport).GetProperty("InitialDigest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;prop.SetValue(t,Files.Digest(t.Repo));
            var r=await t.Push(Credential.Header(DummyToken));Need(r.ExitCode!=0&&Ref(f,PublishPlan.MainRef)==f.Plan.Proof.Accepted.First,"RACE_REJECTED");Need(!G(f.Temp,"--git-dir="+f.Remote,"for-each-ref","--format=%(refname)").Contains(PublishPlan.TagRef),"TAG_NOT_PARTIALLY_CREATED");});
        await Case("isolated-view-does-not-inherit-source-config-or-hooks",async()=>{using var f=new F();await f.Init();G(f.Root,"config","url.https://invalid.example/.insteadOf",f.Plan.Endpoint);G(f.Root,"config","push.followTags","true");G(f.Root,"config","core.hooksPath","hostile-hooks");G(f.Root,"config","push.pushOption","unwanted");var t=new IsolatedTransport(f.Plan);t.Create();var r=await t.Push(Credential.Header(DummyToken));Need(r.ExitCode==0&&t.GuardVerified(),"SOURCE_CONFIG_IGNORED");});
        await Case("credential-runtime-environment-only-redirect-tls-retry-guards",async()=>{using var f=new F();await f.Init();var t=new IsolatedTransport(f.Plan);t.Create();Environment.SetEnvironmentVariable("GIT_TRACE_CURL","hostile-secret-log");Environment.SetEnvironmentVariable("HTTPS_PROXY","http://invalid.example");try{var p=t.StartInfo(t.PushArgs(),Credential.Header(DummyToken));Need(!p.ArgumentList.Any(x=>x.Contains(DummyToken)||x.Contains("Authorization"))&&!p.Environment.ContainsKey("GIT_TRACE_CURL")&&!p.Environment.ContainsKey("HTTPS_PROXY"),"SECRETS_SANITIZED");var values=new Dictionary<string,string>();for(int i=0;i<int.Parse(p.Environment["GIT_CONFIG_COUNT"]!);i++)values.Add(p.Environment["GIT_CONFIG_KEY_"+i]!,p.Environment["GIT_CONFIG_VALUE_"+i]!);Need(values["http.followRedirects"]=="false"&&values["http.sslVerify"]=="true"&&values["http.maxRetries"]=="0"&&values["credential.helper"]==""&&values["http."+f.Plan.Endpoint+".extraHeader"]==Credential.Header(DummyToken),"TRANSPORT_SECURITY");var read=t.StartInfo(t.ReadArgs(),null);Need(!read.Environment.Values.Any(v=>v?.Contains("Authorization: Basic")==true),"PUBLIC_READ_NO_SECRET");}finally{Environment.SetEnvironmentVariable("GIT_TRACE_CURL",null);Environment.SetEnvironmentVariable("HTTPS_PROXY",null);}});
        await Case("closed-push-arguments-no-force-extra-refs-or-config",async()=>{using var f=new F();await f.Init();var t=new IsolatedTransport(f.Plan);foreach(var args in new[]{new[]{"push","--force"},t.PushArgs().Concat(new[]{"HEAD:refs/heads/extra"}).ToArray(),new[]{"config","user.name","changed"},new[]{"fetch",f.Plan.Endpoint},new[]{"push","--no-verify"}})Denied(()=>t.StartInfo(args,Credential.Header(DummyToken)));Need(t.PushCalls==0&&t.ReadCalls==0,"NO_NATIVE_CALL");});
        await Case("remote-parser-duplicate-extra-malformed-refused",()=>{foreach(var text in new[]{"bad",new string('a',40)+"\trefs/heads/other\n",new string('a',40)+"\trefs/heads/main\n"+new string('b',40)+"\trefs/heads/main\n"})Denied(()=>IsolatedTransport.Parse(new(0,Encoding.UTF8.GetBytes(text),Array.Empty<byte>())));return Task.CompletedTask;});
        await Case("credential-input-policy-rejects-controls-and-invalid-token",()=>{foreach(var token in new[]{"password",DummyToken+"\n",DummyToken+"\"",new string('a',400)})Denied(()=>Credential.Header(token));return Task.CompletedTask;});
        await Case("tool-tree-exact-set-and-tamper-rejection",()=>{var root=Path.Combine(Path.GetTempPath(),"tool-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"git.exe");File.WriteAllText(file,"fixture");var pins=new[]{new FilePin("git.exe",7,Safe.Hash(File.ReadAllBytes(file)))};try{using(var l=new ReadLocks())ToolTree.Verify(root,pins,l);File.WriteAllText(Path.Combine(root,"extra.dll"),"unexpected");using(var l=new ReadLocks())Denied(()=>ToolTree.Verify(root,pins,l));File.Delete(Path.Combine(root,"extra.dll"));File.WriteAllText(file,"changed");using(var l=new ReadLocks())Denied(()=>ToolTree.Verify(root,pins,l));}finally{Directory.Delete(root,true);}return Task.CompletedTask;});
        await Case("existing-stage-refused-not-reused",async()=>{using var f=new F();await f.Init();Directory.CreateDirectory(f.Plan.StageRoot);await Refused(()=>f.Run());Need(!File.Exists(f.Plan.Attempt),"NO_ATTEMPT");});
        // Run the exact guard against hostile protocol records, without any network operation.
        await Case("guard-input-missing-duplicate-extra-wrong-identity",async()=>{using var f=new F();await f.Init();var t=new IsolatedTransport(f.Plan);t.Create();var h=f.Plan.Proof.Accepted.Head;var tag=f.Plan.Proof.TagObject;var main=$"{h} {h} refs/heads/main {f.Plan.Proof.Accepted.Second}\n";var line=$"{tag} {tag} {PublishPlan.TagRef} {new string('0',40)}\n";var shell=OperatingSystem.IsWindows()?Path.Combine(GitRoot,"usr","bin","sh.exe"):"/bin/sh";
            foreach(var input in new[]{"",main,main+main+line,main+line+"a b refs/heads/extra c\n",main.Replace(h,new string('a',40))+line}){var p=new ProcessStartInfo(shell){WorkingDirectory=t.Repo,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};p.ArgumentList.Add(t.Hook);p.ArgumentList.Add(f.Plan.Endpoint);p.ArgumentList.Add(f.Plan.Endpoint);using var c=Process.Start(p)!;c.StandardInput.Write(input);c.StandardInput.Close();await c.WaitForExitAsync();Need(c.ExitCode!=0,"GUARD_REFUSED");}Need(!t.GuardVerified(),"NO_GUARD_MARKER");});
        var report=new{Schema="matawaka.workbench-fixed-publisher-qualification/v0.1",Passed=true,Checks=Results,GitVersion=G(Path.GetTempPath(),"--version"),OperatingSystem=System.Runtime.InteropServices.RuntimeInformation.OSDescription,ProductionRemoteContacted=false,OperatorHostAccessed=false,ProductionPublicationPerformed=false,Fixtures="Disposable repositories, synthetic canonical receipts and test-only endpoint rebinding. Credentials in tests are dummy tokens only."};
        File.WriteAllBytes("publisher-qualification.json",Files.Json(report));Console.WriteLine("PUBLISHER_QUALIFICATION_PASS "+Results.Count);
    }
}
