using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matawaka.V0601PublicationPreflight;

internal static class EvidenceTests
{
    private static readonly List<object> Results=new();
    private static readonly string Git=GitRead.Locate();
    private static string G(string root,params string[] args)
    {
        var p=new ProcessStartInfo{FileName=Git,WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var key in p.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(key);
        foreach(var (k,v) in new[]{("GIT_CONFIG_NOSYSTEM","1"),("GIT_CONFIG_GLOBAL",OperatingSystem.IsWindows()?"NUL":"/dev/null"),("GIT_AUTHOR_NAME","Fixture"),("GIT_AUTHOR_EMAIL","fixture@example.invalid"),("GIT_COMMITTER_NAME","Fixture"),("GIT_COMMITTER_EMAIL","fixture@example.invalid")})p.Environment[k]=v;
        foreach(var arg in args)p.ArgumentList.Add(arg);using var c=Process.Start(p)!;
        var output=c.StandardOutput.ReadToEnd();var error=c.StandardError.ReadToEnd();c.WaitForExit(30000);if(c.ExitCode!=0)throw new Exception("Fixture setup failed: "+error);return output.TrimEnd('\r','\n');
    }
    private sealed class Fixture:IDisposable
    {
        internal string Root {get;}=Path.Combine(Path.GetTempPath(),"v0601-evidence-test-"+Guid.NewGuid().ToString("N"));
        internal Spec Spec {get;private set;}
        internal Dictionary<string,BoundFile> Pins {get;}=new(StringComparer.Ordinal);
        internal string App {get;}
        internal string Claim {get;}
        internal string Launch {get;}
        internal string Handoff {get;}
        internal Fixture()
        {
            Directory.CreateDirectory(Root);G(Root,"init","-q");File.WriteAllText(Path.Combine(Root,".gitignore"),"artifacts/\n");File.WriteAllText(Path.Combine(Root,"src.txt"),"first\n");G(Root,"add","--all");G(Root,"commit","-qm","first");var first=G(Root,"rev-parse","HEAD");G(Root,"tag","workbench-v0.55.2-accepted",first);
            File.WriteAllText(Path.Combine(Root,"src.txt"),"second\n");G(Root,"add","src.txt");G(Root,"commit","-qm","second");var second=G(Root,"rev-parse","HEAD");File.WriteAllText(Path.Combine(Root,"src.txt"),"accepted\n");G(Root,"add","src.txt");var tree=G(Root,"write-tree");var head=G(Root,"commit-tree",tree,"-p",first,"-p",second,"-m","accepted fixture");G(Root,"update-ref","HEAD",head);const string tag="workbench-v0.60.1-accepted";G(Root,"tag","-a",tag,head,"-m","accepted fixture");
            foreach(var (role,bound) in Matawaka.V0601PublicationPreflight.Spec.Fixed().Evidence)Pins.Add(role,bound with{Sha256="pending",Bytes=0});
            var appBytes="fixture application bytes, not a live process"u8.ToArray();App=Path.Combine(Root,"artifacts/app-v0.60.1-gui-update/Matawaka.Workbench.App.exe");Write(App,appBytes);var appSha=Safe.Hash(appBytes);const string lease="fixture-lease";const int pid=1234;
            var start=DateTimeOffset.UtcNow-TimeSpan.FromMinutes(10);var activated=start+TimeSpan.FromSeconds(1);var consumed=start+TimeSpan.FromSeconds(2);var complete=start+TimeSpan.FromSeconds(3);var expiry=start+TimeSpan.FromMinutes(5);
            var source=File.ReadAllBytes(Path.Combine(Root,"src.txt"));var sourceSha=Safe.Hash(source);
            Put("import",new{Status="EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION",ImportedCommit=second,LocalHeadAfter=first,GitRefsUnchanged=true});
            Put("manifest",new{Schema="matawaka.workbench-build-source-manifest/v0.60",Version="0.60.1",PredecessorGitSha=first,Files=new[]{new{Path="src.txt",Sha256=sourceSha}}});
            Put("build",new{Status="CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED",TargetTag=tag,PredecessorCommit=first,BuildSourceManifestPath=PathOf("manifest"),BuildSourceManifestSha256=Pins["manifest"].Sha256,CandidateExecutablePath=App,CandidateExecutableSha256=appSha,FreshApplyPlanVerified=true,ExactSourceBytesApplied=true,WorkingTreeMatchesPlannedDelta=true,OfflineBuildCompleted=true,OfflineAppPublishCompleted=true,OfflineSemanticHostPublishCompleted=true,SourceChanges=new[]{new{Path="src.txt",Action="Replace",StagedSha256=sourceSha,StagedBytes=source.Length}}});
            Put("acceptance",new{Schema="matawaka.workbench-acceptance-receipt/v0.60.1",Passed=true,AppExecutableSha256=appSha,Checks=new[]{new{Id="fixture-only",Passed=true}}});
            Put("checkpoint",new{Schema="matawaka.workbench-local-checkpoint-receipt/v0.60.1",NewHead=head,Tag=tag,FirstParent=first,SecondParent=second,TwoParentCommitCreated=true,ParentOrderVerified=true,WorkingTreeCleanAfterCommit=true,RemotePushAllowed=false,NetworkAccessAllowed=false,AutomaticRetryAllowed=false,AppExecutableSha256=appSha,AcceptanceArtifactPath=PathOf("acceptance"),AcceptanceArtifactSha256=Pins["acceptance"].Sha256,BuildReceiptPath=PathOf("build"),BuildReceiptSha256=Pins["build"].Sha256,BuildSourceManifestPath=PathOf("manifest"),BuildSourceManifestSha256=Pins["manifest"].Sha256,PublicImportReceiptPath=PathOf("import"),PublicImportReceiptSha256=Pins["import"].Sha256});
            Launch=Path.Combine(Root,"artifacts/update-applies/fixture-launch.json");Write(Launch,JsonSerializer.SerializeToUtf8Bytes(new{Status="CANDIDATE_LAUNCHED_NOT_ACCEPTED",CandidateExecutablePath=App,CandidateExecutableSha256=appSha,ProcessId=pid}));var launchSha=Safe.Hash(File.ReadAllBytes(Launch));
            Handoff=Path.Combine(Root,"artifacts/update-applies/fixture-handoff.json");Write(Handoff,JsonSerializer.SerializeToUtf8Bytes(new{Status="CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED",CandidateExecutablePath=App,CandidateExecutableSha256=appSha,ProcessId=pid,CandidateLaunchArtifactSha256=launchSha,LaunchReceiptVerified=true,CandidateObservedAlive=true,ProcessImageMatchedCandidate=true,PredecessorSelfCloseEligible=true,CandidateAcceptanceCreated=false,ExternalProcessTerminationAuthorityCreated=false}));
            Claim=PathOf("bootstrap")+".claim";Write(Claim,Encoding.UTF8.GetBytes($"lease={lease}\npid={pid}\nclaimed={consumed:O}\n"));
            Put("bootstrap",new{State="COMPLETED_ACCEPTED",LeaseId=lease,TargetTag=tag,PredecessorCommit=first,PublishAllowed=false,LifecycleAllowed=false,RetryAuthorized=false,LaunchReceiptVerified=true,CandidateObservedAlive=true,ProcessImageMatchedCandidate=true,Failure=(string?)null,CreatedAt=start,ActivatedAt=activated,ConsumingAt=consumed,CompletedAt=complete,ExpiresAt=expiry,CandidateExecutablePath=App,CandidateExecutableSha256=appSha,ProcessId=pid,BuildReceiptPath=PathOf("build"),BuildReceiptSha256=Pins["build"].Sha256,AcceptanceArtifactPath=PathOf("acceptance"),AcceptanceArtifactSha256=Pins["acceptance"].Sha256,CheckpointReceiptPath=PathOf("checkpoint"),CheckpointReceiptSha256=Pins["checkpoint"].Sha256,LaunchReceiptPath=Launch,LaunchReceiptSha256=launchSha,HandoffReceiptPath=Handoff,HandoffReceiptSha256=Safe.Hash(File.ReadAllBytes(Handoff)),ClaimPath=Claim});
            Spec=new(Root,head,first,second,tag,"workbench-v0.55.2-accepted",lease,appSha,1,Pins);
        }
        internal string PathOf(string role)=>Path.Combine(Root,Pins[role].RelativePath);
        private static void Write(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}
        private void Put(string role,object value){var bytes=JsonSerializer.SerializeToUtf8Bytes(value);Write(PathOf(role),bytes);Pins[role]=Pins[role] with{Sha256=Safe.Hash(bytes),Bytes=bytes.Length};}
        internal void Mutate(string role,string key,JsonNode? value,bool rebind=true){var node=JsonNode.Parse(File.ReadAllBytes(PathOf(role)))!;node[key]=value;var bytes=JsonSerializer.SerializeToUtf8Bytes(node);File.WriteAllBytes(PathOf(role),bytes);if(rebind)Pins[role]=Pins[role] with{Sha256=Safe.Hash(bytes),Bytes=bytes.Length};}
        internal Task<Snapshot> Read()=>Inspect.All(Spec,new GitRead(Git,Root));
        public void Dispose(){try{Directory.Delete(Root,true);}catch{}}
    }
    private static async Task Case(string name,Action<Fixture>? mutation=null,bool reject=true)
    {
        using var f=new Fixture();mutation?.Invoke(f);var refs=G(f.Root,"for-each-ref");var index=Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git/index")));bool refused=false;
        try{var a=await f.Read();if(!reject){var b=await f.Read();Safe.Need(a==b,"EVIDENCE_SNAPSHOT_UNSTABLE");}}catch(InvalidDataException){refused=true;}
        Safe.Need(refused==reject,"WRONG_QUALIFICATION_OUTCOME_"+name);Safe.Need(refs==G(f.Root,"for-each-ref")&&index==Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git/index"))),"EVIDENCE_PREFLIGHT_MUTATED_GIT");Results.Add(new{Id=name,Passed=true});Console.WriteLine("PASS "+name);
    }
    private static async Task Main()
    {
        await Case("complete-six-receipt-source-launch-handoff-claim-positive",reject:false);
        foreach(var role in new[]{"import","build","manifest","acceptance","checkpoint","bootstrap"})await Case("canonical-byte-tamper-"+role,f=>File.AppendAllText(f.PathOf(role)," "));
        await Case("terminal-bootstrap-required-no-replay",f=>f.Mutate("bootstrap","State",JsonValue.Create("CONSUMING")));
        await Case("bootstrap-publication-authority-not-inherited",f=>f.Mutate("bootstrap","PublishAllowed",JsonValue.Create(true)));
        await Case("bootstrap-retry-forbidden",f=>f.Mutate("bootstrap","RetryAuthorized",JsonValue.Create(true)));
        await Case("checkpoint-network-forbidden",f=>f.Mutate("checkpoint","NetworkAccessAllowed",JsonValue.Create(true)));
        await Case("schema-family-preserved-v060-not-rewritten",f=>f.Mutate("manifest","Schema",JsonValue.Create("matawaka.workbench-build-source-manifest/v0.60.1")));
        await Case("checkpoint-parent-binding-required",f=>f.Mutate("checkpoint","SecondParent",JsonValue.Create(f.Spec.First)));
        await Case("acceptance-false-refused",f=>f.Mutate("acceptance","Passed",JsonValue.Create(false)));
        await Case("raw-application-image-drift-refused",f=>File.AppendAllText(f.App,"changed"));
        await Case("launch-byte-drift-refused",f=>File.AppendAllText(f.Launch," "));
        await Case("handoff-byte-drift-refused",f=>File.AppendAllText(f.Handoff," "));
        await Case("claim-wrong-pid-refused",f=>File.WriteAllText(f.Claim,File.ReadAllText(f.Claim).Replace("pid=1234","pid=1235")));
        await Case("claim-missing-refused",f=>File.Delete(f.Claim));
        await Case("claim-extra-line-refused",f=>File.AppendAllText(f.Claim,"untrusted-extra\n"));
        await Case("canonical-path-link-mismatch-refused",f=>f.Mutate("checkpoint","BuildReceiptPath",JsonValue.Create(f.PathOf("acceptance"))));
        var report=new{Schema="matawaka.workbench-v0601-preflight-evidence-qualification/v0.1",Passed=true,Checks=Results,Inputs="synthetic test-only fixtures with internal Spec injection; no production CLI override exists",ProductionRemoteContacted=false,OperatorHostAccessed=false,ProductionPublicationPerformed=false};
        File.WriteAllText("evidence-qualification.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("EVIDENCE_QUALIFICATION_PASS "+Results.Count);
    }
}
