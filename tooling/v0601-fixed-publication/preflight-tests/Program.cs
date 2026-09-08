using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;

internal static class Tests
{
    private static readonly List<object> Results=new();
    private static readonly string Git=GitRead.Locate();
    private static string G(string root,params string[] args)
    {
        var p=new ProcessStartInfo{FileName=Git,WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var key in p.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(key);
        p.Environment["GIT_CONFIG_NOSYSTEM"]="1";p.Environment["GIT_CONFIG_GLOBAL"]=OperatingSystem.IsWindows()?"NUL":"/dev/null";
        p.Environment["GIT_AUTHOR_NAME"]="Fixture";p.Environment["GIT_AUTHOR_EMAIL"]="fixture@example.invalid";
        p.Environment["GIT_COMMITTER_NAME"]="Fixture";p.Environment["GIT_COMMITTER_EMAIL"]="fixture@example.invalid";
        foreach(var a in args)p.ArgumentList.Add(a);
        using var child=Process.Start(p)!;var stdout=child.StandardOutput.ReadToEnd();var stderr=child.StandardError.ReadToEnd();child.WaitForExit(30000);
        if(child.ExitCode!=0)throw new Exception("Fixture Git setup failed: "+string.Join(' ',args)+" "+stderr);return stdout.TrimEnd('\r','\n');
    }
    private sealed class Fixture:IDisposable
    {
        internal string Root {get;}=Path.Combine(Path.GetTempPath(),"v0601-preflight-test-"+Guid.NewGuid().ToString("N"));
        internal Spec Spec {get;}
        internal Fixture()
        {
            Directory.CreateDirectory(Root);G(Root,"init","-q");File.WriteAllText(Path.Combine(Root,".gitignore"),"artifacts/\n");File.WriteAllText(Path.Combine(Root,"a.txt"),"first\n");
            G(Root,"add","--all");G(Root,"commit","-qm","first");var first=G(Root,"rev-parse","HEAD");
            G(Root,"tag","workbench-v0.55.2-accepted",first);
            File.WriteAllText(Path.Combine(Root,"a.txt"),"second\n");G(Root,"add","a.txt");G(Root,"commit","-qm","second");var second=G(Root,"rev-parse","HEAD");
            File.WriteAllText(Path.Combine(Root,"a.txt"),"accepted\n");G(Root,"add","a.txt");var tree=G(Root,"write-tree");
            var head=G(Root,"commit-tree",tree,"-p",first,"-p",second,"-m","accepted fixture");
            G(Root,"update-ref","HEAD",head);G(Root,"tag","-a","workbench-v0.60.1-accepted",head,"-m","accepted fixture");
            Spec=new(Root,head,first,second,"workbench-v0.60.1-accepted","workbench-v0.55.2-accepted","fixture-lease",new string('0',64),1,new Dictionary<string,BoundFile>());
        }
        internal Task<GitState> Read(Spec? spec=null)=>Inspect.GitState(spec??Spec,new GitRead(Git,Root));
        public void Dispose(){try{Directory.Delete(Root,true);}catch{}}
    }
    private static async Task Pass(string name,Func<Task> test){await test();Results.Add(new{Id=name,Passed=true});Console.WriteLine("PASS "+name);}
    private static async Task Refused(string name,Action<Fixture> mutate,Func<Fixture,Task>? read=null)
    {
        await Pass(name,async()=>{using var f=new Fixture();mutate(f);try{if(read is null)await f.Read();else await read(f);}catch(InvalidDataException){return;}throw new Exception("Expected refusal: "+name);});
    }
    private static void Throws(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Expected InvalidDataException");}
    private static async Task Main()
    {
        await Pass("full-source-index-tag-two-parent-read-only-happy-path",async()=>{using var f=new Fixture();var before=G(f.Root,"for-each-ref");var index=Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git","index")));var a=await f.Read();var b=await f.Read();Safe.Need(a==b&&a.SourceFileCount==2&&a.TagObjectType=="tag","SNAPSHOT_EXPECTED");Safe.Need(before==G(f.Root,"for-each-ref")&&index==Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git","index"))),"LOCAL_REFS_INDEX_MUTATED");});
        await Refused("wrong-head-refused",f=>{},f=>f.Read(f.Spec with{Head=f.Spec.First}));
        await Refused("wrong-parent-order-refused",f=>{},f=>f.Read(f.Spec with{First=f.Spec.Second,Second=f.Spec.First}));
        await Refused("dirty-tracked-source-refused",f=>File.AppendAllText(Path.Combine(f.Root,"a.txt"),"drift"));
        await Refused("staged-source-drift-refused",f=>{File.AppendAllText(Path.Combine(f.Root,"a.txt"),"drift");G(f.Root,"add","a.txt");});
        await Refused("extra-untracked-source-refused",f=>File.WriteAllText(Path.Combine(f.Root,"extra.txt"),"unplanned"));
        await Refused("historical-v060-tag-refused",f=>G(f.Root,"tag","workbench-v0.60-accepted",f.Spec.First));
        await Refused("wrong-target-tag-refused",f=>{G(f.Root,"tag","-d",f.Spec.Tag);G(f.Root,"tag","-a",f.Spec.Tag,f.Spec.First,"-m","wrong fixture");});
        await Refused("lightweight-target-tag-refused",f=>{G(f.Root,"tag","-d",f.Spec.Tag);G(f.Root,"tag",f.Spec.Tag,f.Spec.Head);});
        await Refused("assume-unchanged-refused",f=>G(f.Root,"update-index","--assume-unchanged","a.txt"));
        await Refused("skip-worktree-refused",f=>G(f.Root,"update-index","--skip-worktree","a.txt"));
        await Refused("local-includes-refused-before-source-filtering",f=>G(f.Root,"config","include.path","missing-config"));
        await Refused("local-external-filter-refused",f=>G(f.Root,"config","filter.hostile.clean","echo INJECTED"));
        await Refused("promisor-config-refused",f=>G(f.Root,"config","remote.origin.promisor","true"));
        await Refused("git-object-alternates-refused",f=>{Directory.CreateDirectory(Path.Combine(f.Root,".git/objects/info"));File.WriteAllText(Path.Combine(f.Root,".git/objects/info/alternates"),"missing\n");});
        await Refused("duplicate-payload-refused",f=>{},async f=>{var m=Safe.Parse(JsonSerializer.SerializeToUtf8Bytes(new{Files=new[]{new{Path="a.txt",Sha256=Safe.Hash(File.ReadAllBytes(Path.Combine(f.Root,"a.txt")))},new{Path="a.txt",Sha256="wrong"}}}));await Inspect.GitState(f.Spec,new GitRead(Git,f.Root),m,Safe.Parse("{\"SourceChanges\":[]}"u8.ToArray()));});
        await Pass("unsafe-paths-and-duplicate-json-refused",()=>{foreach(var p in new[]{"../a","/root","C:/x","a\\b","a:stream","a/CON.txt","a/..","a/","a. ","a\nx"})Safe.Need(!Safe.Relative(p),"UNSAFE_PATH_ADMITTED");Throws(()=>Safe.Parse("{\"x\":1,\"x\":2}"u8.ToArray()));return Task.CompletedTask;});
        await Pass("network-and-mutation-command-family-refused",async()=>{using var f=new Fixture();var r=new GitRead(Git,f.Root);foreach(var command in new[]{"push","fetch","ls-remote","checkout","reset","update-ref","commit","tag"}){try{await r.Run(command);throw new Exception("Command admitted");}catch(InvalidDataException){}}Safe.Need(r.Calls==0,"FORBIDDEN_COMMAND_PROCESS_STARTED");});
        await Pass("config-and-hash-object-write-forms-refused",async()=>{using var f=new Fixture();var r=new GitRead(Git,f.Root);try{await r.Run("config","user.name","new");throw new Exception("config write admitted");}catch(InvalidDataException){}try{await r.Run("hash-object","-w","a.txt");throw new Exception("object write admitted");}catch(InvalidDataException){}Safe.Need(r.Calls==0,"WRITE_FORM_STARTED");});
        var report=new{Schema="matawaka.workbench-v0601-preflight-qualification/v0.1",Passed=true,Checks=Results,OperatingSystem=System.Runtime.InteropServices.RuntimeInformation.OSDescription,ProductionRemoteContacted=false,OperatorHostAccessed=false,ProductionPublicationPerformed=false};
        File.WriteAllText("preflight-qualification.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PREFLIGHT_QUALIFICATION_PASS "+Results.Count);
    }
}
