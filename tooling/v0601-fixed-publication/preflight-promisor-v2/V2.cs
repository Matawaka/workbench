using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.V0601PublicationPreflight;

internal sealed record ProofSpec(Spec Accepted,string TagObject,string Tree,int CurrentCount,int HistoryCount);
internal sealed record ClosureState(int CurrentTreePresentObjects,int CurrentTreeMissingBoundaryObjects,
    int FullHistoryPresentObjects,int FullHistoryMissingBoundaryObjects,bool ReachableClosureComplete,
    string CurrentObjectSetSha256,string HistoryObjectSetSha256);
internal sealed record V2Snapshot(GitState Git,EvidenceState Evidence,ClosureState Closure,string GitStoreByteDigest);
internal static class V2
{
    internal const string GitPath=@"K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe";
    internal const string GitSha="c470d205517c7a53ceca321df16a6e4549fcd52b576ab4d09536d36f26fda5a9";
    internal const string Confirmation="EXPORT-EXACT-V0601-PROMISOR-PREFLIGHT-V2";
    internal const string Status="LOCAL_PROMISOR_PREFLIGHT_VERIFIED_NO_REMOTE_EFFECTS";
    internal static readonly string[] Six={"162aa48105259c20b067ba43e48db35db4f9d34a","3b542376bae6ed3ab5a5fdd232cdfd96b453c586","5fdd8744827ddd202e317174c937761fb20a81e2","81aab74193548f4061853abb4a6cfcaea25975c2","c363c499acc2fd5a93624e3ad51d9320ea48e7af","f68e906e127777b1cf918b75829b8f692cecc682"};
    internal static ProofSpec Fixed()
    {
        var s=Spec.Fixed();var e=new Dictionary<string,BoundFile>(s.Evidence,StringComparer.Ordinal);
        e.Add("recovery",new("artifacts/object-recovery-v0601/six-blobs-58b9430fc544998a8e40ba00b6757cc630ba9081.json","178d8111daf10842d28a807af63ccbf488ffc22b1cdf0bfb25f1507a46729452",3090));
        e.Add("recovery-attempt",new("artifacts/object-recovery-v0601/attempt-six-blobs-58b9430fc544998a8e40ba00b6757cc630ba9081.json","e6a4f3412da70c20a3fd280d6f548976ee1b223bce8134bb85a89faa1dc79f0d",902));
        return new(s with{Evidence=e},"c07409c7973f1a4181e94168ba172bba0d56355f","a6baa9d2d90fb9f00e54ac4c2373b7d9b8aab4e1",583,1384);
    }
    internal static void CheckConfigEntry(string entry)
    {
        var a=entry.Split('\n',2);var key=a[0];
        if(!key.EndsWith(".promisor",StringComparison.OrdinalIgnoreCase))return;
        Safe.Need(Regex.IsMatch(key,@"\Aremote\.[^\r\n]+\.promisor\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),"UNQUALIFIED_PROMISOR_KEY");
        Safe.Need(a.Length==2&&new[]{"true","false","1","0","yes","no","on","off"}.Contains(a[1].ToLowerInvariant()),"UNQUALIFIED_PROMISOR_BOOLEAN");
        // An active flag is supported only by guarded local reads. It never permits transport.
    }
    internal static bool Admitted(string[] a)
    {
        if(a.Length==0)return false;
        static bool Rev(string x)=>Safe.Oid(x)||x=="HEAD"||Regex.IsMatch(x,@"\Arefs/tags/workbench-v0\.(55\.2|60\.1)-accepted(\^\{commit\})?\z",RegexOptions.CultureInvariant);
        switch(a[0]) {
            case "--version":return a.Length==1;
            case "config":return a.SequenceEqual(new[]{"config","--local","--no-includes","--null","--list"});
            case "rev-parse":return a.Length==3&&a[1]=="--verify"&&Rev(a[2]);
            case "symbolic-ref":return a.SequenceEqual(new[]{"symbolic-ref","--quiet","HEAD"});
            case "cat-file":return a.Length==3&&new[]{"-t","commit","tag","blob"}.Contains(a[1])&&Safe.Oid(a[2]);
            case "for-each-ref":return a.SequenceEqual(new[]{"for-each-ref","--format=%(refname) %(objectname)"});
            case "ls-tree":return a.Length==5&&a.Take(4).SequenceEqual(new[]{"ls-tree","-r","-z","--full-tree"})&&Rev(a[4]);
            case "ls-files":return a.SequenceEqual(new[]{"ls-files","--stage","-z"})||a.SequenceEqual(new[]{"ls-files","-v","-z"})||a.SequenceEqual(new[]{"ls-files","--others","--exclude-standard","-z"});
            case "diff-tree":return a.Length==8&&a.Take(6).SequenceEqual(new[]{"diff-tree","--no-commit-id","--name-status","--no-renames","-r","-z"})&&Safe.Oid(a[6])&&Safe.Oid(a[7]);
            case "hash-object":return a.SequenceEqual(new[]{"hash-object","--stdin-paths"});
            case "rev-list":return (a.Length==6||a.Length==7)&&a.Take(4).SequenceEqual(new[]{"rev-list","--objects","--no-object-names","--missing=print"})&&a[^1]=="--"&&a.Skip(4).Take(a.Length-5).All(Safe.Oid);
            default:return false;
        }
    }
    internal static (int Count,string Digest) Objects(string text)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var line in text.Split('\n',StringSplitOptions.RemoveEmptyEntries)) {
            var oid=line.TrimEnd('\r');Safe.Need(!oid.StartsWith('?'),"REACHABLE_OBJECT_MISSING_NO_FETCH");
            Safe.Need(Safe.Oid(oid)&&ids.Add(oid),"MALFORMED_OR_DUPLICATE_OBJECT_ENUMERATION");
            Safe.Need(ids.Count<=20000,"OBJECT_ENUMERATION_LIMIT");
        }
        Safe.Need(ids.Count>0,"EMPTY_OBJECT_ENUMERATION");return(ids.Count,Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n",ids.Order(StringComparer.Ordinal)))));
    }
    internal static async Task<ClosureState> Closure(ProofSpec proof,GitRead git)
    {
        var tree=Objects(await git.Text("rev-list","--objects","--no-object-names","--missing=print",proof.Tree,"--"));
        var history=Objects(await git.Text("rev-list","--objects","--no-object-names","--missing=print",proof.Accepted.Head,proof.TagObject,"--"));
        Safe.Need(tree.Count==proof.CurrentCount&&history.Count==proof.HistoryCount,"RECOVERY_CLOSURE_COUNT_MISMATCH");
        return new(tree.Count,0,history.Count,0,true,tree.Digest,history.Digest);
    }
    internal static string StoreDigest(string root)
    {
        var store=Safe.Under(root,".git");Safe.Need(Directory.Exists(store),"DIRECT_GIT_DIRECTORY_REQUIRED");
        var files=new List<string>();long total=0;int nodes=0;
        void Walk(string path,string relative,int depth) {
            Safe.Need(depth<=64,"GIT_STORE_DEPTH_LIMIT");Safe.NoReparse(path);
            foreach(var child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal)) {
                Safe.Need(++nodes<=30000,"GIT_STORE_ENTRY_LIMIT");Safe.NoReparse(child);
                var name=Path.GetFileName(child);var rel=relative+name;
                Safe.Need(!name.Any(char.IsControl)&&!name.Contains('\t'),"UNQUALIFIED_GIT_STORE_NAME");
                if(Directory.Exists(child)){Walk(child,rel+"/",depth+1);continue;}
                Safe.Need(!name.EndsWith(".lock",StringComparison.OrdinalIgnoreCase),"GIT_OPERATION_IN_PROGRESS");
                var bytes=Safe.Read(child,256*1024*1024);total+=bytes.Length;Safe.Need(total<=512L*1024*1024,"GIT_STORE_BYTE_LIMIT");
                files.Add($"{rel}\t{bytes.Length}\t{Safe.Hash(bytes)}");
            }
        }
        Walk(store,"",0);return Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n",files.Order(StringComparer.Ordinal))));
    }
    internal static void Recovery(ProofSpec proof)
    {
        var s=proof.Accepted;
        JsonElement Read(string key) {var pin=s.Evidence[key];var bytes=Safe.Read(Safe.Under(s.Root,pin.RelativePath));Safe.Need(bytes.Length==pin.Bytes&&Safe.Hash(bytes)==pin.Sha256,"RECOVERY_RAW_BYTES_MISMATCH");return Safe.Parse(bytes);}
        var r=Read("recovery");var a=Read("recovery-attempt");
        Safe.Eq(r,"Schema","matawaka.workbench-offline-six-blob-import-receipt/v0.1");Safe.Eq(r,"Status","SIX_HISTORICAL_BLOBS_IMPORTED_OFFLINE_NO_REF_MUTATION");
        Safe.Eq(a,"Schema","matawaka.workbench-six-blob-import-attempt/v0.1");Safe.Eq(a,"State","ATTEMPT_CONSUMED_NO_RETRY");
        foreach(var x in new[]{r,a}){Safe.Eq(x,"AcceptedHead",s.Head);Safe.Eq(x,"AcceptedTagObject",proof.TagObject);Safe.False(x,"PublicationAuthorized","RetryAuthorized");}
        Safe.Eq(r,"AcceptedTag","refs/tags/"+s.Tag);Safe.Eq(r,"CurrentTree",proof.Tree);Safe.Eq(r,"GitExecutableSha256",GitSha);
        Safe.Eq(r,"AttemptReceiptSha256",s.Evidence["recovery-attempt"].Sha256);Safe.Eq(a,"ExplicitConfirmation","IMPORT-EXACT-V0601-SIX-HISTORICAL-BLOBS");Safe.False(a,"NetworkAuthorized");
        Safe.True(r,"ReachableClosureComplete","PreexistingObjectFilesUnchanged","OriginalCanonicalReceiptsUnchanged","ObjectDatabaseChanged");
        Safe.False(r,"RefMutationPerformed","SourceMutationPerformed","ConfigMutationPerformed","IndexMutationPerformed","NetworkOperationImplemented","WholeWorkingTreeVerified","WholeRuntimeBinaryTreeVerified","OsNetworkIsolationProven");
        Safe.Need(r.GetProperty("ImportedRawBytes").GetInt32()==17695,"RECOVERY_BYTE_COUNT_MISMATCH");
        Safe.Need(r.GetProperty("CurrentTreePresentObjects").GetInt32()==proof.CurrentCount&&r.GetProperty("FullHistoryPresentObjects").GetInt32()==proof.HistoryCount&&r.GetProperty("CurrentTreeMissingBoundaryObjects").GetInt32()==0&&r.GetProperty("FullHistoryMissingBoundaryObjects").GetInt32()==0,"RECOVERY_COUNT_MISMATCH");
        foreach(var (x,key) in new[]{(r,"ImportedObjects"),(a,"Objects")})Safe.Need(x.GetProperty(key).EnumerateArray().Select(v=>v.GetString()).SequenceEqual(Six),"RECOVERY_OID_SET_MISMATCH");
        Safe.Need(r.GetProperty("AddedObjectFiles").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(Six.Select(o=>"objects/"+o[..2]+"/"+o[2..])),"RECOVERY_LOOSE_PATH_MISMATCH");
        foreach(var stem in new[]{"GitMetadataSha256","TrackedSourceDigest"})Safe.Need(Safe.S(r,stem+"Before")==Safe.S(r,stem+"After"),"RECOVERY_PROTECTED_STATE_MISMATCH");
    }
    internal static string GitObject(string type,byte[] data)=>Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"{type} {data.Length}\0").Concat(data).ToArray())).ToLowerInvariant();
    internal static async Task<V2Snapshot> Check(ProofSpec proof,GitRead git)
    {
        var before=StoreDigest(proof.Accepted.Root);Recovery(proof);
        var accepted=await Inspect.All(proof.Accepted,git);
        Safe.Need(accepted.Git.TagObject==proof.TagObject&&accepted.Git.Tree==proof.Tree,"EXACT_ACCEPTED_TAG_OR_TREE_MISMATCH");
        Safe.Need(GitObject("tag",await git.Run("cat-file","tag",proof.TagObject))==proof.TagObject,"TAG_RAW_OBJECT_ID_MISMATCH");
        Safe.Need(GitObject("commit",await git.Run("cat-file","commit",proof.Accepted.Head))==proof.Accepted.Head,"COMMIT_RAW_OBJECT_ID_MISMATCH");
        var closure=await Closure(proof,git);var after=StoreDigest(proof.Accepted.Root);
        Safe.Need(before==after,"GIT_STORE_CHANGED_DURING_PREFLIGHT");
        return new(accepted.Git,accepted.Evidence,closure,after);
    }
    internal static void Export(string path,byte[] bytes,bool confirmed,bool snapshotsEqual,TimeSpan age)
    {
        Safe.Need(confirmed,"RECEIPT_CONFIRMATION_REQUIRED");Safe.Need(snapshotsEqual,"LOCAL_SNAPSHOT_CHANGED_NO_WRITE");
        Safe.Need(age>=TimeSpan.Zero&&age<=TimeSpan.FromMinutes(5),"PREVIEW_EXPIRED_NO_WRITE");Safe.Parse(bytes);
        Safe.NoReparse(path);Safe.Need(!File.Exists(path),"PREFLIGHT_RECEIPT_ALREADY_EXISTS_NO_OVERWRITE");
        var directory=Path.GetDirectoryName(path)!;Safe.NoReparse(directory);Directory.CreateDirectory(directory);Safe.NoReparse(directory);
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){file.Write(bytes);file.Flush(true);}
        Safe.Need(Safe.Hash(Safe.Read(path))==Safe.Hash(bytes),"NEW_RECEIPT_READBACK_MISMATCH");
    }
}
internal static class EntryPointV2
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length==0,"ARGUMENTS_NOT_ACCEPTED");Safe.Need(OperatingSystem.IsWindows(),"WINDOWS_OPERATOR_HOST_REQUIRED");
            var proof=V2.Fixed();var spec=proof.Accepted;var self=Environment.ProcessPath??throw new InvalidDataException("SELF_PATH_MISSING");Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(spec.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"PREFLIGHT_MUST_BE_OUTSIDE_ACCEPTED_REPOSITORY");
            var output=Safe.Under(spec.Root,"artifacts/publication-v0601/preflight-promisor-v2-"+spec.Head+".json");
            Safe.Need(!File.Exists(output),"PREFLIGHT_RECEIPT_ALREADY_EXISTS_NO_OVERWRITE");
            var git=new GitRead(V2.GitPath,spec.Root,V2.GitSha);
            Safe.Need(await git.Text("--version")=="git version 2.55.0.windows.4","PINNED_GIT_VERSION_MISMATCH");
            var first=await V2.Check(proof,git);var clock=Stopwatch.StartNew();
            Console.WriteLine("LOCAL PREFLIGHT V2. No network, credentials, object/source/ref/config/index mutation or publication.");
            Console.WriteLine($"Repository: {spec.Root}\nAccepted HEAD: {first.Git.Head}\nTree: {first.Git.Tree}\nTag object: {first.Git.TagObject}\nTag peeled commit: {first.Git.TagPeeledCommit}\nSource files verified: {first.Git.SourceFileCount}\nCurrent tree: {first.Closure.CurrentTreePresentObjects}/0 missing\nReachable history: {first.Closure.FullHistoryPresentObjects}/0 missing\nOrdered parents: {spec.First}, {spec.Second}\nReceipt: {output}\nRemote publication: NOT AUTHORIZED");
            Console.WriteLine($"To write ONLY a NEW local preflight receipt, enter exactly:\n{V2.Confirmation}");
            if(Console.ReadLine()!=V2.Confirmation){Console.WriteLine("CANCELLED_NO_WRITE");return 2;}
            Safe.Need(clock.Elapsed<=TimeSpan.FromMinutes(5),"PREVIEW_EXPIRED_NO_WRITE");
            var fresh=await V2.Check(proof,git);Safe.Need(first==fresh,"LOCAL_SNAPSHOT_CHANGED_NO_WRITE");
            var receipt=new{Schema="matawaka.workbench-v0601-publication-preflight/v0.2",Status=V2.Status,ObservedAt=DateTimeOffset.UtcNow,RepositoryRoot=spec.Root,Version="0.60.1",AcceptedTag=spec.Tag,Snapshot=fresh,CanonicalEvidence=spec.Evidence,ToolSha256=Safe.Hash(Safe.Read(self,128*1024*1024)),GitReadCalls=git.Calls,ExplicitConfirmation=V2.Confirmation,NewLocalReceiptWriteConfirmed=true,ExplicitNoLazyFetch=true,GitTransportsDenied=true,GitStoreBytesUnchanged=true,TrackedSourceMatchesAcceptedGitTree=true,IndexMatchesAcceptedGitTree=true,ExactCommitDeltaVerified=true,LaunchHandoffClaimVerified=true,NetworkReadPerformed=false,RemoteWritePerformed=false,CredentialAccessPerformed=false,SourceMutationPerformed=false,GitRefMutationPerformed=false,ConfigMutationPerformed=false,IndexMutationPerformed=false,ObjectDatabaseMutationPerformed=false,PublicationAuthorityCreated=false,RetryAuthorityCreated=false,LiveProcessReobserved=false,EntireRuntimeBinaryTreeVerified=false,OsNetworkIsolationProven=false,OsConcurrencyExclusionProven=false,Note="Fresh local pre-publication evidence only. Active promisor configuration is preserved; all Git invocations use explicit no-lazy-fetch and denied transports. Source equality uses Git's qualified normalization, while exact changed payloads also match original raw SHA256/size bindings. Eight historical receipts are verified without rewriting or replay. Launch/handoff/claim are historical byte links, not live process authority. Stable snapshots do not prove OS concurrency exclusion. This is not permission to publish; a separately qualified exact publisher and new human confirmation remain required."};
            var bytes=JsonSerializer.SerializeToUtf8Bytes(receipt,new JsonSerializerOptions{WriteIndented=true});V2.Export(output,bytes,true,first==fresh,clock.Elapsed);
            Console.WriteLine($"COMPLETED: {V2.Status}\nReceipt: {output}\nReceipt SHA-256: {Safe.Hash(bytes)}\nSTOP. Return this JSON. No push, Update, Accept or import replay.");return 0;
        }catch(Exception ex){Console.Error.WriteLine("REFUSED: "+(ex is InvalidDataException?ex.Message:ex.GetType().Name));Console.Error.WriteLine("No remote operation is implemented. A new local receipt may exist if its write was interrupted. Do not overwrite, bypass or automatically retry.");return 1;}
    }
}
