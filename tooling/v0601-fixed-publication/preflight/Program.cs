using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.V0601PublicationPreflight;

internal sealed record BoundFile(string RelativePath, string Sha256, int Bytes);
internal sealed record Spec(string Root, string Head, string First, string Second, string Tag,
    string PreTag, string Lease, string AppSha, int DeltaCount, IReadOnlyDictionary<string, BoundFile> Evidence)
{
    internal const string Remote = "https://github.com/Matawaka/workbench.git";
    internal const string Confirmation = "EXPORT-EXACT-V0601-PUBLICATION-PREFLIGHT";
    internal static Spec Fixed() => new(@"K:\Matawaka\Workbench",
        "58b9430fc544998a8e40ba00b6757cc630ba9081", "ea852feeb0e8d92a8977bb251693e7e977913dca",
        "ac083598711caa0c399cc0d2c385b980c083024a", "workbench-v0.60.1-accepted", "workbench-v0.55.2-accepted",
        "27c29a0d53ef448e9a42a65709a7a9eb", "97559c29e9ecfce6fcdb78a1975ae4fc60a9dd2d017a37ed0835f0b7c0217b9d", 79,
        new Dictionary<string, BoundFile>(StringComparer.Ordinal) {
            ["import"] = new("artifacts/convergence-v0601/public-main-ac083598711caa0c399cc0d2c385b980c083024a.json", "08422305e99b9440641ab0d85f2685f357323c2745da7f19df596abdbcd3438b",1539),
            ["build"] = new("artifacts/update-applies/apply-build-v0.14-20260908-152159275.json", "69becfc3a8582feebe8d158dfc60189c31e0556880bbc676f7e41251743f0bd2",26191),
            ["manifest"] = new("artifacts/checkpoints/v0.60.1-source-manifest-20260908-152159119.json", "22de05b99a0de2684fa719c541bbfeea4923defe6ee98deee412ba52fd5baebf",13927),
            ["acceptance"] = new("artifacts/acceptance/v0.60.1-20260908-152211331.json", "4e619bf59d0033fbf75f5a32eef31bc486c7eee25227051447229c77af2d6d82",279470),
            ["checkpoint"] = new("artifacts/acceptance/checkpoint-v0.60.1-20260908-152212795.json", "a817d84f3ffd6d8d9bb3839bca4c8e266512f92f538050b1d70686e242021f0c",7712),
            ["bootstrap"] = new("artifacts/transition-bootstrap/transition-bootstrap-v0.40-27c29a0d53ef448e9a42a65709a7a9eb.json", "7ccd660f3d2077f4dc5c708204af4b9d17f109ac9b14f361d92e7592c8b41a7d",3498)
        });
}

internal static class Safe
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static void Need(bool value, string code) { if (!value) throw new InvalidDataException(code); }
    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    internal static string GitBlob(byte[] data) => Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {data.Length}\0").Concat(data).ToArray())).ToLowerInvariant();
    internal static bool Oid(string s) => Regex.IsMatch(s, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant);
    internal static bool Relative(string s) => !string.IsNullOrEmpty(s) && !Path.IsPathRooted(s) && !s.Any(char.IsControl) &&
        s.IndexOfAny(['\\', ':', '"', '*', '?', '<', '>', '|']) < 0 && s.Split('/').All(p => p.Length>0 && p!="." && p!=".." &&
        !p.EndsWith(' ') && !p.EndsWith('.') && !Regex.IsMatch(p.Split('.')[0], "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
    internal static string Under(string root, string relative)
    {
        Need(Relative(relative), "UNSAFE_RELATIVE_PATH");
        var path=Path.GetFullPath(Path.Combine(root, relative.Replace('/',Path.DirectorySeparatorChar)));
        Need(path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "PATH_ESCAPE");
        NoReparse(path); return path;
    }
    internal static void NoReparse(string path)
    {
        var p=Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(p)) {
            if (File.Exists(p)||Directory.Exists(p)) Need((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"REPARSE_PATH_REFUSED");
            var parent=Path.GetDirectoryName(p); if(parent==p) break; p=parent!;
        }
    }
    internal static byte[] Read(string path, int maximum=8*1024*1024)
    {
        NoReparse(path); Need(File.Exists(path), "REQUIRED_LOCAL_FILE_MISSING");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Need(stream.Length<=maximum,"LOCAL_FILE_BYTE_LIMIT"); var data=new byte[checked((int)stream.Length)]; stream.ReadExactly(data); return data;
    }
    internal static JsonElement Parse(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes, new JsonDocumentOptions{MaxDepth=64}); Unique(doc.RootElement); return doc.RootElement.Clone();
    }
    private static void Unique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object) {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in value.EnumerateObject()){Need(names.Add(p.Name),"DUPLICATE_JSON_PROPERTY");Unique(p.Value);}
        } else if(value.ValueKind==JsonValueKind.Array) foreach(var item in value.EnumerateArray()) Unique(item);
    }
    internal static string S(JsonElement e,string name)=>e.GetProperty(name).GetString()??throw new InvalidDataException("NULL_REQUIRED_STRING");
    internal static bool B(JsonElement e,string name)=>e.GetProperty(name).GetBoolean();
    internal static void Eq(JsonElement e,string name,string expected)=>Need(S(e,name)==expected,"BINDING_MISMATCH_"+name);
    internal static void True(JsonElement e,params string[] names){foreach(var n in names)Need(B(e,n),"REQUIRED_TRUE_"+n);}
    internal static void False(JsonElement e,params string[] names){foreach(var n in names)Need(!B(e,n),"FORBIDDEN_EFFECT_"+n);}
}

internal sealed class GitRead
{
    internal string Exe {get;}
    internal string ExeSha {get;}
    internal string Root {get;}
    internal int Calls {get;private set;}
    internal GitRead(string exe,string root){Exe=Path.GetFullPath(exe);Root=Path.GetFullPath(root);ExeSha=Safe.Hash(Safe.Read(Exe,32*1024*1024));}
    internal static string Locate()
    {
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            if(!Path.IsPathFullyQualified(directory)) continue;
            var path=Path.Combine(directory,OperatingSystem.IsWindows()?"git.exe":"git");
            if(File.Exists(path)) {Safe.NoReparse(path);return Path.GetFullPath(path);}
        }
        throw new InvalidDataException("INSTALLED_GIT_NOT_FOUND");
    }
    // Closed command set. There is no ls-remote/fetch/push/config-write/commit/tag-write/checkout/reset command.
    internal async Task<byte[]> Run(params string[] args)
    {
        var allowed=new[]{"config","rev-parse","symbolic-ref","cat-file","for-each-ref","ls-tree","ls-files","diff-tree","hash-object","--version"};
        Safe.Need(args.Length>0 && allowed.Contains(args[0]),"NON_READ_COMMAND_REFUSED");
        Safe.Need(args[0]!="config" || args.SequenceEqual(new[]{"config","--local","--no-includes","--null","--list"}),"CONFIG_WRITE_REFUSED");
        Safe.Need(args[0]!="hash-object" || args.SequenceEqual(new[]{"hash-object","--stdin-paths"}),"OBJECT_WRITE_REFUSED");
        return await Process(args,null);
    }
    internal Task<byte[]> HashPaths(string[] paths)
    {
        Safe.Need(paths.All(Safe.Relative),"UNSAFE_HASH_PATH");
        // Git's --stdin-paths understands C-style quoted filenames; restrict JSON quoting to ASCII here.
        Safe.Need(paths.All(p=>p.All(c=>c>=32 && c<127)),"NON_ASCII_HASH_PATH_NOT_QUALIFIED");
        return Process(new[]{"hash-object","--stdin-paths"},Safe.Utf8.GetBytes(string.Join("\n",paths.Select(p=>JsonSerializer.Serialize(p)))+"\n"));
    }
    private async Task<byte[]> Process(string[] args,byte[]? input)
    {
        Safe.Need(Safe.Hash(Safe.Read(Exe,32*1024*1024))==ExeSha,"GIT_IMAGE_DRIFT"); Calls++;
        var psi=new ProcessStartInfo{FileName=Exe,WorkingDirectory=Root,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        var keep=new[]{"SystemRoot","WINDIR","PATH","TEMP","TMP","COMSPEC"}.Select(k=>(k,v:Environment.GetEnvironmentVariable(k))).ToArray();
        psi.Environment.Clear();foreach(var (k,v) in keep)if(v is not null)psi.Environment[k]=v;
        psi.Environment["GIT_CONFIG_NOSYSTEM"]="1";psi.Environment["GIT_CONFIG_GLOBAL"]=OperatingSystem.IsWindows()?"NUL":"/dev/null";
        psi.Environment["GIT_TERMINAL_PROMPT"]="0";psi.Environment["GIT_NO_REPLACE_OBJECTS"]="1";psi.Environment["GIT_NO_LAZY_FETCH"]="1";psi.Environment["GIT_OPTIONAL_LOCKS"]="0";
        foreach(var a in new[]{"--no-optional-locks","-c","core.fsmonitor=false","-c","core.untrackedCache=false","-c","core.hooksPath="+Path.Combine(Root,".git","preflight-no-hooks"),"-c","core.attributesFile="+(OperatingSystem.IsWindows()?"NUL":"/dev/null"),"-c","core.commitGraph=false"}.Concat(args))psi.ArgumentList.Add(a);
        using var process=new System.Diagnostics.Process{StartInfo=psi};Safe.Need(process.Start(),"LOCAL_GIT_START_FAILED");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try {
            var output=Drain(process.StandardOutput.BaseStream,8*1024*1024,timeout.Token);
            var error=Drain(process.StandardError.BaseStream,128*1024,timeout.Token);
            if(input is not null)await process.StandardInput.BaseStream.WriteAsync(input,timeout.Token);process.StandardInput.Close();
            await Task.WhenAll(output,error,process.WaitForExitAsync(timeout.Token));
            Safe.Need(process.ExitCode==0,"LOCAL_GIT_READ_FAILED_"+args[0]);return await output;
        } catch {try{process.Kill(entireProcessTree:true);}catch{}throw;}
    }
    private static async Task<byte[]> Drain(Stream stream,int max,CancellationToken ct)
    {
        using var memory=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=await stream.ReadAsync(buffer,ct))!=0){Safe.Need(memory.Length+n<=max,"GIT_OUTPUT_BYTE_LIMIT");memory.Write(buffer,0,n);}return memory.ToArray();
    }
    internal async Task<string> Text(params string[] args)=>Safe.Utf8.GetString(await Run(args)).TrimEnd('\r','\n');
}

internal sealed record TreeEntry(string Path,string Mode,string Oid);
internal sealed record GitState(string Head,string Branch,string Tree,string FirstParent,string SecondParent,string TagObject,
    string TagObjectType,string TagPeeledCommit,string RawTagSha256,int RawTagBytes,string RawCommitSha256,int SourceFileCount,
    string FullSourceObservationSha256,string RefsSha256,string IndexSha256,string LocalConfigSha256,string GitExecutableSha256,string GitVersion);
internal sealed record EvidenceState(string Sha256,string ClaimSha256,string LaunchSha256,string HandoffSha256,int HistoricalProcessId,int AcceptanceRecords,int UniqueAcceptanceIds);
internal sealed record Snapshot(GitState Git,EvidenceState Evidence);

internal static class Inspect
{
    internal static async Task<GitState> GitState(Spec spec,GitRead git,JsonElement? manifest=null,JsonElement? build=null)
    {
        var root=spec.Root;Safe.NoReparse(root);Safe.Need(Directory.Exists(Safe.Under(root,".git")),"DIRECT_GIT_DIRECTORY_REQUIRED");
        foreach(var p in new[]{".git/commondir",".git/shallow",".git/info/grafts",".git/objects/info/alternates"})Safe.Need(!File.Exists(Safe.Under(root,p)),"UNQUALIFIED_GIT_LAYOUT");
        var config=await git.Text("config","--local","--no-includes","--null","--list");
        foreach(var entry in config.Split('\0',StringSplitOptions.RemoveEmptyEntries)) {
            var key=entry.Split('\n')[0];
            Safe.Need(!Regex.IsMatch(key,"^(include|includeif|filter|extensions)\\.",RegexOptions.IgnoreCase) && !key.EndsWith(".promisor",StringComparison.OrdinalIgnoreCase),"UNQUALIFIED_GIT_CONFIG");
        }
        var refsBefore=await git.Run("for-each-ref","--format=%(refname) %(objectname)");
        Safe.Need(!Safe.Utf8.GetString(refsBefore).Split('\n').Any(x=>x.StartsWith("refs/replace/",StringComparison.Ordinal)||x.StartsWith("refs/tags/workbench-v0.60-accepted ",StringComparison.Ordinal)),"HISTORICAL_OR_REPLACE_REF_REFUSED");
        var head=await git.Text("rev-parse","--verify","HEAD");Safe.Need(head==spec.Head,"LOCAL_HEAD_MISMATCH");
        var branch=await git.Text("symbolic-ref","--quiet","HEAD");Safe.Need(branch.StartsWith("refs/heads/",StringComparison.Ordinal),"DETACHED_HEAD_REFUSED");
        Safe.Need(await git.Text("rev-parse","--verify","refs/tags/"+spec.PreTag+"^{commit}")==spec.First,"PREDECESSOR_TAG_MISMATCH");
        var tag=await git.Text("rev-parse","--verify","refs/tags/"+spec.Tag);Safe.Need(Safe.Oid(tag),"INVALID_TAG_OBJECT");
        var tagType=await git.Text("cat-file","-t",tag);Safe.Need(tagType=="tag","ANNOTATED_TAG_REQUIRED");
        var peel=await git.Text("rev-parse","--verify","refs/tags/"+spec.Tag+"^{commit}");Safe.Need(peel==head,"TAG_PEEL_MISMATCH");
        var rawTag=await git.Run("cat-file","tag",tag);var tagText=Safe.Utf8.GetString(rawTag);
        Safe.Need(tagText.StartsWith($"object {head}\ntype commit\ntag {spec.Tag}\n",StringComparison.Ordinal),"TAG_BODY_BINDING_MISMATCH");
        var rawCommit=await git.Run("cat-file","commit",head);var headers=Safe.Utf8.GetString(rawCommit).Split("\n\n",2)[0].Split('\n');
        var parents=headers.Where(h=>h.StartsWith("parent ",StringComparison.Ordinal)).Select(h=>h[7..]).ToArray();
        Safe.Need(parents.SequenceEqual(new[]{spec.First,spec.Second}),"ORDERED_PARENTS_MISMATCH");
        var tree=headers.Single(h=>h.StartsWith("tree ",StringComparison.Ordinal))[5..];Safe.Need(Safe.Oid(tree),"INVALID_TREE");
        var entries=Tree(await git.Run("ls-tree","-r","-z","--full-tree",head));Safe.Need(entries.Length>0 && entries.Length<=5000,"SOURCE_TREE_LIMIT");
        var stages=Safe.Utf8.GetString(await git.Run("ls-files","--stage","-z")).Split('\0',StringSplitOptions.RemoveEmptyEntries);
        var expect=entries.Select(e=>$"{e.Mode} {e.Oid} 0\t{e.Path}").Order(StringComparer.Ordinal).ToArray();
        Safe.Need(stages.Order(StringComparer.Ordinal).SequenceEqual(expect),"INDEX_TREE_MISMATCH");
        var flags=Safe.Utf8.GetString(await git.Run("ls-files","-v","-z")).Split('\0',StringSplitOptions.RemoveEmptyEntries);
        Safe.Need(flags.Length==entries.Length && flags.All(f=>f.StartsWith("H ",StringComparison.Ordinal)),"SKIP_OR_ASSUME_UNCHANGED_REFUSED");
        var extra=await git.Text("ls-files","--others","--exclude-standard","-z");Safe.Need(extra.Length==0,"UNTRACKED_SOURCE_REFUSED");
        var paths=entries.Select(e=>e.Path).ToArray();var raw=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
        foreach(var path in paths){var data=Safe.Read(Safe.Under(root,path));total+=data.Length;Safe.Need(total<=100*1024*1024,"SOURCE_BYTE_LIMIT");raw.Add(path,data);}
        var oids=Safe.Utf8.GetString(await git.HashPaths(paths)).TrimEnd('\r','\n').Split('\n').Select(s=>s.TrimEnd('\r')).ToArray();
        Safe.Need(oids.Length==paths.Length,"HASH_COUNT_MISMATCH");
        for(var i=0;i<entries.Length;i++)Safe.Need(oids[i]==entries[i].Oid,"LIVE_SOURCE_GIT_OBJECT_MISMATCH");
        if(manifest is { } m && build is { } b) {
            var files=m.GetProperty("Files").EnumerateArray().ToArray();var changes=b.GetProperty("SourceChanges").EnumerateArray().ToArray();
            Safe.Need(files.Length==spec.DeltaCount && changes.Length==files.Length,"DELTA_COUNT_MISMATCH");
            var delta=Safe.Utf8.GetString(await git.Run("diff-tree","--no-commit-id","--name-status","--no-renames","-r","-z",spec.First,head)).Split('\0',StringSplitOptions.RemoveEmptyEntries);
            Safe.Need(delta.Length==files.Length*2,"COMMIT_DELTA_COUNT_MISMATCH");
            var expectedDelta=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var c in changes){var action=Safe.S(c,"Action");Safe.Need(action is "Add" or "Replace","FORBIDDEN_DELTA_ACTION");expectedDelta.Add(Safe.S(c,"Path"),action=="Add"?"A":"M");}
            for(var i=0;i<delta.Length;i+=2)Safe.Need(expectedDelta.TryGetValue(delta[i+1],out var act)&&act==delta[i],"COMMIT_DELTA_MISMATCH");
            var observed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in files){var p=Safe.S(file,"Path");Safe.Need(observed.Add(p)&&raw.ContainsKey(p),"DUPLICATE_OR_MISSING_DELTA_PATH");var hash=Safe.Hash(raw[p]);Safe.Need(hash==Safe.S(file,"Sha256"),"RAW_PAYLOAD_MISMATCH");var c=changes.Single(c=>Safe.S(c,"Path")==p);Safe.Need(hash==Safe.S(c,"StagedSha256")&&raw[p].Length==c.GetProperty("StagedBytes").GetInt64(),"BUILD_PAYLOAD_MISMATCH");}
        }
        // Re-read raw files to close accidental source movement during normalized Git hashing.
        foreach(var (p,data) in raw)Safe.Need(Safe.Hash(Safe.Read(Safe.Under(root,p)))==Safe.Hash(data),"SOURCE_CHANGED_DURING_PREFLIGHT");
        var sourceDigest=Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n",entries.Select(e=>$"{e.Path}\t{e.Mode}\t{e.Oid}\t{Safe.Hash(raw[e.Path])}"))));
        Safe.Need(refsBefore.SequenceEqual(await git.Run("for-each-ref","--format=%(refname) %(objectname)")),"REFS_CHANGED_DURING_PREFLIGHT");
        Safe.Need(await git.Text("rev-parse","--verify","HEAD")==head,"HEAD_CHANGED_DURING_PREFLIGHT");
        return new(head,branch,tree,spec.First,spec.Second,tag,tagType,peel,Safe.Hash(rawTag),rawTag.Length,Safe.Hash(rawCommit),entries.Length,sourceDigest,Safe.Hash(refsBefore),Safe.Hash(Safe.Read(Safe.Under(root,".git/index"))),Safe.Hash(Safe.Read(Safe.Under(root,".git/config"))),git.ExeSha,await git.Text("--version"));
    }
    private static TreeEntry[] Tree(byte[] data)
    {
        var result=new List<TreeEntry>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in Safe.Utf8.GetString(data).Split('\0',StringSplitOptions.RemoveEmptyEntries)) {
            var pair=line.Split('\t',2);Safe.Need(pair.Length==2,"TREE_PARSE_FAILURE");var meta=pair[0].Split(' ');
            Safe.Need(meta.Length==3 && meta[1]=="blob" && (meta[0] is "100644" or "100755") && Safe.Oid(meta[2]) && Safe.Relative(pair[1]),"UNQUALIFIED_TREE_ENTRY");
            Safe.Need(paths.Add(pair[1]),"CASE_COLLIDING_TREE_PATH");result.Add(new(pair[1],meta[0],meta[2]));
        }
        return result.OrderBy(e=>e.Path,StringComparer.Ordinal).ToArray();
    }
    internal static async Task<Snapshot> All(Spec s,GitRead git)
    {
        var d=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var (role,bound) in s.Evidence) {
            var bytes=Safe.Read(Safe.Under(s.Root,bound.RelativePath));Safe.Need(bytes.Length==bound.Bytes && Safe.Hash(bytes)==bound.Sha256,"CANONICAL_BYTES_MISMATCH_"+role);d.Add(role,Safe.Parse(bytes));
        }
        var i=d["import"];var b=d["build"];var m=d["manifest"];var a=d["acceptance"];var c=d["checkpoint"];var l=d["bootstrap"];
        Safe.Eq(i,"Status","EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION");Safe.Eq(i,"ImportedCommit",s.Second);Safe.Eq(i,"LocalHeadAfter",s.First);Safe.True(i,"GitRefsUnchanged");
        Safe.Eq(m,"Schema","matawaka.workbench-build-source-manifest/v0.60");Safe.Eq(m,"Version","0.60.1");Safe.Eq(m,"PredecessorGitSha",s.First);
        Safe.Eq(c,"Schema","matawaka.workbench-local-checkpoint-receipt/v0.60.1");Safe.Eq(c,"NewHead",s.Head);Safe.Eq(c,"Tag",s.Tag);Safe.Eq(c,"FirstParent",s.First);Safe.Eq(c,"SecondParent",s.Second);Safe.True(c,"TwoParentCommitCreated","ParentOrderVerified","WorkingTreeCleanAfterCommit");Safe.False(c,"RemotePushAllowed","NetworkAccessAllowed","AutomaticRetryAllowed");
        Safe.Eq(a,"Schema","matawaka.workbench-acceptance-receipt/v0.60.1");Safe.True(a,"Passed");Safe.Eq(a,"AppExecutableSha256",s.AppSha);
        var checks=a.GetProperty("Checks").EnumerateArray().ToArray();Safe.Need(checks.Length>0&&checks.All(x=>Safe.B(x,"Passed")),"NEGATIVE_ACCEPTANCE_RECORD");
        Safe.Eq(l,"State","COMPLETED_ACCEPTED");Safe.Eq(l,"LeaseId",s.Lease);Safe.Eq(l,"TargetTag",s.Tag);Safe.Eq(l,"PredecessorCommit",s.First);Safe.False(l,"PublishAllowed","LifecycleAllowed","RetryAuthorized");Safe.True(l,"LaunchReceiptVerified","CandidateObservedAlive","ProcessImageMatchedCandidate");
        Safe.Need(l.GetProperty("Failure").ValueKind==JsonValueKind.Null,"BOOTSTRAP_FAILURE");
        var created=l.GetProperty("CreatedAt").GetDateTimeOffset();var activated=l.GetProperty("ActivatedAt").GetDateTimeOffset();var consumed=l.GetProperty("ConsumingAt").GetDateTimeOffset();var completed=l.GetProperty("CompletedAt").GetDateTimeOffset();var expires=l.GetProperty("ExpiresAt").GetDateTimeOffset();
        Safe.Need(created<=activated&&activated<=consumed&&consumed<=completed&&completed<=expires,"HISTORICAL_BOOTSTRAP_TIME_ORDER");
        foreach(var (record,pathField,hashField,role) in new[]{(c,"AcceptanceArtifactPath","AcceptanceArtifactSha256","acceptance"),(c,"BuildReceiptPath","BuildReceiptSha256","build"),(c,"BuildSourceManifestPath","BuildSourceManifestSha256","manifest"),(c,"PublicImportReceiptPath","PublicImportReceiptSha256","import"),(b,"BuildSourceManifestPath","BuildSourceManifestSha256","manifest"),(l,"BuildReceiptPath","BuildReceiptSha256","build"),(l,"AcceptanceArtifactPath","AcceptanceArtifactSha256","acceptance"),(l,"CheckpointReceiptPath","CheckpointReceiptSha256","checkpoint")}) {
            Safe.Need(SamePath(Safe.S(record,pathField),Safe.Under(s.Root,s.Evidence[role].RelativePath)),"CANONICAL_PATH_LINK_MISMATCH");Safe.Eq(record,hashField,s.Evidence[role].Sha256);
        }
        Safe.True(b,"FreshApplyPlanVerified","ExactSourceBytesApplied","WorkingTreeMatchesPlannedDelta","OfflineBuildCompleted","OfflineAppPublishCompleted","OfflineSemanticHostPublishCompleted");
        Safe.Eq(b,"Status","CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED");Safe.Eq(b,"TargetTag",s.Tag);Safe.Eq(b,"PredecessorCommit",s.First);
        var app=Safe.Under(s.Root,"artifacts/app-v0.60.1-gui-update/Matawaka.Workbench.App.exe");
        foreach(var record in new[]{b,l}){Safe.Need(SamePath(Safe.S(record,"CandidateExecutablePath"),app),"APP_PATH_MISMATCH");Safe.Eq(record,"CandidateExecutableSha256",s.AppSha);}
        Safe.Eq(c,"AppExecutableSha256",s.AppSha);Safe.Need(Safe.Hash(Safe.Read(app,128*1024*1024))==s.AppSha,"APP_BYTES_MISMATCH");
        var pid=l.GetProperty("ProcessId").GetInt32();Safe.Need(pid>0,"BAD_HISTORICAL_PID");
        var launchPath=BoundSubPath(s.Root,Safe.S(l,"LaunchReceiptPath"),"artifacts/update-applies/");
        var handoffPath=BoundSubPath(s.Root,Safe.S(l,"HandoffReceiptPath"),"artifacts/update-applies/");
        var launchBytes=Safe.Read(launchPath);var handoffBytes=Safe.Read(handoffPath);
        Safe.Need(Safe.Hash(launchBytes)==Safe.S(l,"LaunchReceiptSha256") && Safe.Hash(handoffBytes)==Safe.S(l,"HandoffReceiptSha256"),"LAUNCH_HANDOFF_BYTES_MISMATCH");
        var launch=Safe.Parse(launchBytes);var handoff=Safe.Parse(handoffBytes);
        foreach(var record in new[]{launch,handoff}){Safe.Eq(record,"CandidateExecutableSha256",s.AppSha);Safe.Need(SamePath(Safe.S(record,"CandidateExecutablePath"),app)&&record.GetProperty("ProcessId").GetInt32()==pid,"HISTORICAL_PROCESS_LINK_MISMATCH");}
        Safe.Eq(launch,"Status","CANDIDATE_LAUNCHED_NOT_ACCEPTED");Safe.Eq(handoff,"Status","CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED");Safe.Eq(handoff,"CandidateLaunchArtifactSha256",Safe.Hash(launchBytes));
        Safe.True(handoff,"LaunchReceiptVerified","CandidateObservedAlive","ProcessImageMatchedCandidate","PredecessorSelfCloseEligible");Safe.False(handoff,"CandidateAcceptanceCreated","ExternalProcessTerminationAuthorityCreated");
        var claimPath=Safe.Under(s.Root,s.Evidence["bootstrap"].RelativePath)+".claim";Safe.Need(SamePath(Safe.S(l,"ClaimPath"),claimPath),"CLAIM_PATH_MISMATCH");
        var claimBytes=Safe.Read(claimPath,4096);var claim=Safe.Utf8.GetString(claimBytes).TrimEnd('\r','\n').Split('\n').Select(x=>x.TrimEnd('\r')).ToArray();
        Safe.Need(claim.Length==3 && claim[0]=="lease="+s.Lease && claim[1]=="pid="+pid && claim[2].StartsWith("claimed=",StringComparison.Ordinal),"CLAIM_CONTENT_MISMATCH");
        var claimTime=DateTimeOffset.Parse(claim[2][8..],CultureInfo.InvariantCulture);Safe.Need(claimTime>=activated&&claimTime<=consumed,"CLAIM_TIME_MISMATCH");
        var state=await GitState(s,git,m,b);
        var identity=string.Join("\n",s.Evidence.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>$"{x.Key}\t{x.Value.Sha256}\t{x.Value.Bytes}"));
        return new(state,new(Safe.Hash(Safe.Utf8.GetBytes(identity)),Safe.Hash(claimBytes),Safe.Hash(launchBytes),Safe.Hash(handoffBytes),pid,checks.Length,checks.Select(x=>Safe.S(x,"Id")).Distinct(StringComparer.Ordinal).Count()));
    }
    private static bool SamePath(string a,string b)=>Path.GetFullPath(a).Equals(Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    private static string BoundSubPath(string root,string path,string prefix)
    {
        var relative=Path.GetRelativePath(root,Path.GetFullPath(path)).Replace(Path.DirectorySeparatorChar,'/');
        Safe.Need(relative.StartsWith(prefix,StringComparison.Ordinal),"EVIDENCE_SUBPATH_ESCAPE");return Safe.Under(root,relative);
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length==0,"ARGUMENTS_NOT_ACCEPTED");Safe.Need(OperatingSystem.IsWindows(),"WINDOWS_OPERATOR_HOST_REQUIRED");
            var spec=Spec.Fixed();var self=Environment.ProcessPath??throw new InvalidDataException("SELF_PATH_MISSING");Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(spec.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"PREFLIGHT_MUST_BE_OUTSIDE_ACCEPTED_REPOSITORY");
            var output=Safe.Under(spec.Root,"artifacts/publication-v0601/preflight-"+spec.Head+".json");Safe.Need(!File.Exists(output),"PREFLIGHT_RECEIPT_ALREADY_EXISTS_NO_OVERWRITE");
            var git=new GitRead(GitRead.Locate(),spec.Root);var snapshot=await Inspect.All(spec,git);var observedAt=DateTimeOffset.Now;
            Console.WriteLine("LOCAL PREVIEW ONLY — no network, credentials, source/ref mutation or publication.");
            Console.WriteLine($"Repository: {spec.Root}\nAccepted HEAD: {snapshot.Git.Head}\nTree: {snapshot.Git.Tree}\nTag: {spec.Tag}\nTag object: {snapshot.Git.TagObject}\nTag peeled commit: {snapshot.Git.TagPeeledCommit}\nSource files verified: {snapshot.Git.SourceFileCount}\nOrdered parents: {spec.First}, {spec.Second}\nReceipt: {output}\nRemote publication: NOT AUTHORIZED");
            Console.WriteLine($"To write ONLY the new local preflight receipt, enter exactly:\n{Spec.Confirmation}");
            var token=Console.ReadLine();if(token!=Spec.Confirmation){Console.WriteLine("CANCELLED_NO_WRITE");return 2;}
            Safe.Need(DateTimeOffset.Now-observedAt<=TimeSpan.FromMinutes(5),"PREVIEW_EXPIRED_NO_WRITE");
            var fresh=await Inspect.All(spec,git);Safe.Need(snapshot==fresh,"LOCAL_SNAPSHOT_CHANGED_NO_WRITE");
            var receipt=new{Schema="matawaka.workbench-v0601-publication-preflight/v0.1",Status="LOCAL_PUBLICATION_PREFLIGHT_VERIFIED_NO_REMOTE_EFFECTS",ObservedAt=DateTimeOffset.Now,RepositoryRoot=spec.Root,Version="0.60.1",AcceptedTag=spec.Tag,Snapshot=fresh,CanonicalEvidence=spec.Evidence,ToolSha256=Safe.Hash(Safe.Read(self,128*1024*1024)),GitReadCalls=git.Calls,NewLocalReceiptWriteConfirmed=true,NetworkReadPerformed=false,RemoteWritePerformed=false,CredentialAccessPerformed=false,SourceMutationPerformed=false,GitRefMutationPerformed=false,PublicationAuthorityCreated=false,RetryAuthorityCreated=false,LiveProcessReobserved=false,EntireRuntimeBinaryTreeVerified=false,Note="Fresh local publication preflight only. Historical PID is evidence, not current process authority. App EXE hash is not an inventory of all .NET runtime outputs. No consumed bootstrap authority is reused. Separate qualified publisher and new human confirmation remain required."};
            var bytes=JsonSerializer.SerializeToUtf8Bytes(receipt,new JsonSerializerOptions{WriteIndented=true});
            var directory=Path.GetDirectoryName(output)!;Safe.NoReparse(directory);Directory.CreateDirectory(directory);Safe.NoReparse(directory);
            using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){stream.Write(bytes);stream.Flush(true);}
            Safe.Need(Safe.Hash(Safe.Read(output))==Safe.Hash(bytes),"NEW_RECEIPT_READBACK_MISMATCH");
            Console.WriteLine($"COMPLETED: LOCAL_PUBLICATION_PREFLIGHT_VERIFIED_NO_REMOTE_EFFECTS\nReceipt: {output}\nReceipt SHA-256: {Safe.Hash(bytes)}\nSTOP. Do not push or repeat Update/Accept.");return 0;
        } catch(Exception ex) {
            // No raw Git output, credentials, stack traces or arbitrary file contents are printed.
            Console.Error.WriteLine("REFUSED: "+(ex is InvalidDataException?ex.Message:ex.GetType().Name));Console.Error.WriteLine("No remote operation was implemented or attempted. STOP; do not bypass the refusal.");return 1;
        }
    }
}
