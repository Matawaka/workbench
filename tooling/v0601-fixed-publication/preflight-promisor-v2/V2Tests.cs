using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Matawaka.V0601PublicationPreflight;
internal static class V2Tests
{
    private static readonly string Git=GitRead.Locate();
    private static readonly List<object> Results=new();
    private static string G(string root,params string[] args)
    {
        var p=new ProcessStartInfo{FileName=Git,WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var k in p.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(k);
        p.Environment["GIT_CONFIG_NOSYSTEM"]="1";p.Environment["GIT_CONFIG_GLOBAL"]=OperatingSystem.IsWindows()?"NUL":"/dev/null";
        foreach(var a in args)p.ArgumentList.Add(a);using var c=Process.Start(p)!;
        var output=c.StandardOutput.ReadToEnd();var error=c.StandardError.ReadToEnd();c.WaitForExit(30000);
        if(c.ExitCode!=0)throw new Exception("Fixture setup: "+string.Join(' ',args)+" "+error);return output.TrimEnd('\r','\n');
    }
    private sealed class F:IDisposable
    {
        internal readonly global::EvidenceTests.Fixture Base=new();
        internal ProofSpec Proof;
        internal readonly Dictionary<string,BoundFile> Pins;
        internal string Root=>Base.Root;
        internal F()
        {
            Pins=new(Base.Spec.Evidence,StringComparer.Ordinal);
            var s=Base.Spec with{Evidence=Pins};var tag=G(Root,"rev-parse","refs/tags/"+s.Tag);var tree=G(Root,"rev-parse",s.Head+"^{tree}");
            var n=V2.Objects(G(Root,"rev-list","--objects","--no-object-names",tree)).Count;
            var h=V2.Objects(G(Root,"rev-list","--objects","--no-object-names",s.Head,tag)).Count;
            Proof=new(s,tag,tree,n,h);
            Put("recovery-attempt",new{Schema="matawaka.workbench-six-blob-import-attempt/v0.1",State="ATTEMPT_CONSUMED_NO_RETRY",AcceptedHead=s.Head,AcceptedTagObject=tag,Objects=V2.Six,ExplicitConfirmation="IMPORT-EXACT-V0601-SIX-HISTORICAL-BLOBS",PublicationAuthorized=false,NetworkAuthorized=false,RetryAuthorized=false});
            Put("recovery",new{Schema="matawaka.workbench-offline-six-blob-import-receipt/v0.1",Status="SIX_HISTORICAL_BLOBS_IMPORTED_OFFLINE_NO_REF_MUTATION",AcceptedHead=s.Head,AcceptedTag="refs/tags/"+s.Tag,AcceptedTagObject=tag,CurrentTree=tree,GitExecutableSha256=V2.GitSha,AttemptReceiptSha256=Pins["recovery-attempt"].Sha256,ImportedObjects=V2.Six,ImportedRawBytes=17695,AddedObjectFiles=V2.Six.Select(o=>"objects/"+o[..2]+"/"+o[2..]).ToArray(),CurrentTreePresentObjects=n,CurrentTreeMissingBoundaryObjects=0,FullHistoryPresentObjects=h,FullHistoryMissingBoundaryObjects=0,ReachableClosureComplete=true,GitMetadataSha256Before="fixture",GitMetadataSha256After="fixture",TrackedSourceDigestBefore="fixture",TrackedSourceDigestAfter="fixture",PreexistingObjectFilesUnchanged=true,OriginalCanonicalReceiptsUnchanged=true,ObjectDatabaseChanged=true,RefMutationPerformed=false,SourceMutationPerformed=false,ConfigMutationPerformed=false,IndexMutationPerformed=false,NetworkOperationImplemented=false,PublicationAuthorized=false,RetryAuthorized=false,WholeWorkingTreeVerified=false,WholeRuntimeBinaryTreeVerified=false,OsNetworkIsolationProven=false});
            G(Root,"config","remote.origin.promisor","true");
        }
        private void Put(string role,object value)
        {
            var path="artifacts/object-recovery-v0601/"+role+".json";var bytes=JsonSerializer.SerializeToUtf8Bytes(value);Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root,path))!);File.WriteAllBytes(Path.Combine(Root,path),bytes);Pins.Add(role,new(path,Safe.Hash(bytes),bytes.Length));
        }
        internal void Change(string role,string key,JsonNode? value)
        {
            var pin=Pins[role];var path=Path.Combine(Root,pin.RelativePath);var node=JsonNode.Parse(File.ReadAllBytes(path))!;node[key]=value;var b=JsonSerializer.SerializeToUtf8Bytes(node);File.WriteAllBytes(path,b);Pins[role]=pin with{Bytes=b.Length,Sha256=Safe.Hash(b)};
        }
        internal Task<V2Snapshot> Read()=>V2.Check(Proof,new GitRead(Git,Root));
        public void Dispose()=>Base.Dispose();
    }
    private static string AllBytes(string root)=>Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n",Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p=>Path.GetRelativePath(root,p)+"\t"+Safe.Hash(File.ReadAllBytes(p))))));
    private static async Task Case(string id,Action<F>? mutate=null,bool reject=true)
    {
        using var f=new F();mutate?.Invoke(f);var before=AllBytes(f.Root);bool denied=false;
        try{var a=await f.Read();if(!reject){var b=await f.Read();Safe.Need(a==b&&a.Closure.ReachableClosureComplete,"V2_SNAPSHOT_UNSTABLE");}}catch(InvalidDataException){denied=true;}
        Safe.Need(denied==reject,"V2_UNEXPECTED_RESULT_"+id);Safe.Need(before==AllBytes(f.Root),"V2_REPOSITORY_BYTES_MUTATED_"+id);
        Results.Add(new{Id=id,Passed=true,PreexistingRepositoryFileBytesUnchanged=true});Console.WriteLine("PASS "+id);
    }
    private static void Refuse(Action action,string code)
    {
        try{action();}catch(InvalidDataException e){Safe.Need(e.Message==code,"UNEXPECTED_REFUSAL_"+e.Message);return;}throw new Exception("Expected refusal "+code);
    }
    private static void Drop(F f,string oid){var path=Path.Combine(f.Root,".git","objects",oid[..2],oid[2..]);File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}
    internal static async Task Main()
    {
        await global::Tests.Run();await global::EvidenceTests.Run();
        await Case("eight-evidence-promisor-complete-full-preflight",reject:false);
        await Case("standard-crlf-normalization-preserved",f=>{G(f.Root,"config","core.autocrlf","true");File.WriteAllText(Path.Combine(f.Root,".gitignore"),"artifacts/\r\n");},reject:false);
        await Case("recovery-raw-bytes-tampered",f=>File.AppendAllText(Path.Combine(f.Root,f.Pins["recovery"].RelativePath)," "));
        await Case("attempt-raw-bytes-tampered",f=>File.AppendAllText(Path.Combine(f.Root,f.Pins["recovery-attempt"].RelativePath)," "));
        await Case("recovery-incomplete-status-refused",f=>f.Change("recovery","Status",JsonValue.Create("PREPARED")));
        await Case("recovery-publication-inference-refused",f=>f.Change("recovery","PublicationAuthorized",JsonValue.Create(true)));
        await Case("consumed-attempt-reset-refused",f=>f.Change("recovery-attempt","State",JsonValue.Create("READY")));
        await Case("recovery-history-count-refused",f=>f.Change("recovery","FullHistoryPresentObjects",JsonValue.Create(99)));
        await Case("recovery-object-set-refused",f=>f.Change("recovery","ImportedObjects",new JsonArray(JsonValue.Create(new string('a',40)))));
        await Case("recovery-before-after-drift-refused",f=>f.Change("recovery","GitMetadataSha256After",JsonValue.Create("drift")));
        await Case("exact-tag-object-binding-refused",f=>f.Proof=f.Proof with{TagObject=f.Proof.Accepted.Head});
        await Case("exact-tree-binding-refused",f=>f.Proof=f.Proof with{Tree=f.Proof.Accepted.Head});
        await Case("missing-historical-blob-no-lazy-fetch",f=>Drop(f,G(f.Root,"rev-parse",f.Proof.Accepted.First+":src.txt")));
        await Case("missing-current-blob-no-lazy-fetch",f=>Drop(f,G(f.Root,"rev-parse",f.Proof.Accepted.Head+":src.txt")));
        await Case("missing-parent-commit-no-lazy-fetch",f=>Drop(f,f.Proof.Accepted.First));
        await Case("missing-current-root-tree-no-lazy-fetch",f=>Drop(f,f.Proof.Tree));
        await Case("info-attributes-override-refused",f=>{Directory.CreateDirectory(Path.Combine(f.Root,".git/info"));File.WriteAllText(Path.Combine(f.Root,".git/info/attributes"),"* -text\n");});
        await Case("unsafe-filter-still-refused",f=>G(f.Root,"config","filter.hostile.clean","echo unsafe"));
        await Case("include-still-refused",f=>G(f.Root,"config","include.path","missing-file"));
        await Case("git-lock-in-progress-refused",f=>File.WriteAllText(Path.Combine(f.Root,".git/index.lock"),"existing"));
        await Case("promisor-invalid-boolean-refused",f=>G(f.Root,"config","remote.origin.promisor","perhaps"));
        await Case("launch-handoff-claim-still-required",f=>File.Delete(f.Base.Claim));
        using(var f=new F()) {
            var first=await f.Read();G(f.Root,"config","core.abbrev","11");var second=await f.Read();Safe.Need(first!=second,"BETWEEN_SNAPSHOT_DRIFT_UNDETECTED");
            var path=Path.Combine(f.Root,"artifacts/publication-v0601/fixture.json");var bytes="{\"fixture\":true}"u8.ToArray();
            Refuse(()=>V2.Export(path,bytes,true,first==second,TimeSpan.Zero),"LOCAL_SNAPSHOT_CHANGED_NO_WRITE");Safe.Need(!File.Exists(path),"DRIFT_WROTE_RECEIPT");Results.Add(new{Id="between-snapshot-config-drift-refuses-export",Passed=true});
        }
        using(var f=new F()) {
            var p=Path.Combine(f.Root,"artifacts/publication-v0601/fixture.json");var bytes="{\"fixture\":true}"u8.ToArray();var before=AllBytes(f.Root);
            Refuse(()=>V2.Export(p,bytes,false,true,TimeSpan.Zero),"RECEIPT_CONFIRMATION_REQUIRED");
            Refuse(()=>V2.Export(p,bytes,true,true,TimeSpan.FromMinutes(6)),"PREVIEW_EXPIRED_NO_WRITE");
            Safe.Need(before==AllBytes(f.Root),"UNCONFIRMED_OR_EXPIRED_WRITE");
            V2.Export(p,bytes,true,true,TimeSpan.Zero);Safe.Need(File.ReadAllBytes(p).SequenceEqual(bytes),"RECEIPT_BYTES_CHANGED");
            Refuse(()=>V2.Export(p,bytes,true,true,TimeSpan.Zero),"PREFLIGHT_RECEIPT_ALREADY_EXISTS_NO_OVERWRITE");
            Results.Add(new{Id="confirmed-export-cancel-expiry-exact-bytes-no-overwrite",Passed=true});
            Refuse(()=>{_ =new GitRead(Git,f.Root,new string('0',64));},"PINNED_GIT_IMAGE_MISMATCH");Results.Add(new{Id="wrong-fixed-git-sha-before-process",Passed=true});
            var g=new GitRead(Git,f.Root);foreach(var a in new[]{new[]{"symbolic-ref","HEAD","refs/heads/other"},new[]{"cat-file","--filters",f.Proof.Accepted.Head},new[]{"rev-parse","--git-path","config"},new[]{"ls-files","--debug"}}){try{await g.Run(a);throw new Exception("Argument form accepted");}catch(InvalidDataException){}}
            Safe.Need(g.Calls==0,"FORBIDDEN_FORM_STARTED_GIT");Results.Add(new{Id="closed-read-argument-forms-no-process",Passed=true});
        }
        foreach(var s in new[]{"?"+new string('a',40),new string('a',40)+"\n"+new string('a',40),"not-an-object"}) {try{V2.Objects(s);throw new Exception("Bad enumeration accepted");}catch(InvalidDataException){}}
        Results.Add(new{Id="missing-duplicate-malformed-object-parser",Passed=true});
        using(var f=new F()) {
            G(f.Root,"config","uploadpack.allowFilter","true");var clone=Path.Combine(Path.GetTempPath(),"v2-partial-"+Guid.NewGuid().ToString("N"));
            try {
                G(f.Root,"clone","--no-checkout","--filter=blob:none",new Uri(f.Root+Path.DirectorySeparatorChar).AbsoluteUri,clone);
                var p=f.Proof with{Accepted=f.Proof.Accepted with{Root=clone}};var before=AllBytes(clone);bool refused=false;
                try{await V2.Closure(p,new GitRead(Git,clone));}catch(InvalidDataException e){Safe.Need(e.Message=="REACHABLE_OBJECT_MISSING_NO_FETCH","PARTIAL_CLONE_WRONG_REFUSAL");refused=true;}
                Safe.Need(refused&&before==AllBytes(clone),"PARTIAL_CLONE_HYDRATED_OR_NOT_REFUSED");Results.Add(new{Id="genuine-blob-none-partial-clone-no-hydration",Passed=true});
            }finally{try{Directory.Delete(clone,true);}catch{}}
        }
        var report=new{Schema="matawaka.workbench-promisor-preflight-v2-qualification/v0.1",Passed=true,GitVersion=G(Path.GetTempPath(),"--version"),GitExecutableSha256=Safe.Hash(Safe.Read(Git,32*1024*1024)),Checks=Results,LegacySourceChecks=19,LegacyEvidenceChecks=21,Inputs="Synthetic receipts and disposable repositories; fixture Spec injection is internal and not exposed by production CLI",ProductionRemoteContacted=false,OperatorHostAccessed=false,ProductionPublicationPerformed=false};
        File.WriteAllText("promisor-v2-qualification.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PROMISOR_V2_ALL_PASS "+Results.Count);
    }
}
