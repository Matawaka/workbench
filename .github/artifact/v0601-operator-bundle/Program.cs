using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string PredecessorCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
const string PredecessorTag = "workbench-v0.55.2-accepted";
const string TargetHead = "aa2bfa215d3cacca015d40cd8786706aff0a9f19";
const string TargetVersion = "0.60.1";
const string TargetTag = "workbench-v0.60.1-accepted";
const string ExpectedPackageSha256 = "1fa4d657834ec211cd42dc9cebdfd77d2464ec1acf2a05b8f81b86f355999f15";

if (args.Length != 2) throw new InvalidDataException("Usage: Builder <candidateRoot> <outputDir>");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);

Require(Git(root, "rev-parse", "HEAD").Trim() == TargetHead, "candidate HEAD mismatch");
var tag = Git(root, "rev-list", "-n", "1", PredecessorTag).Trim();
Require(tag == PredecessorCommit, "predecessor tag mismatch");

var deltas = ReadDelta(root);
Require(deltas.Count == 71, $"unexpected cumulative file count: {deltas.Count}");
Require(deltas.All(x => x.Status is "A" or "M"), "non Add/Replace operation present");

var package = Path.Combine(output, "workbench-v0.60.1-cumulative.zip");
CreatePackage(root, package, deltas);
var packageSha = HashFile(package);
Require(packageSha == ExpectedPackageSha256, $"package SHA mismatch: observed={packageSha}; expected={ExpectedPackageSha256}");
VerifyPackage(package, deltas, root);

var manifest = new {
    Schema = "matawaka.workbench-v0601-operator-bundle/v0.1",
    Status = "EXACT_OPERATOR_ARTIFACTS_PREPARED_NOT_EXECUTED",
    CandidateSourceHead = TargetHead,
    TargetVersion,
    TargetTag,
    PredecessorCommit,
    PredecessorTag,
    PublicSecondParent = "6541dc32182c970c8e1a6ade426a6cee7086511b",
    UpdatePackageFile = Path.GetFileName(package),
    UpdatePackageBytes = new FileInfo(package).Length,
    UpdatePackageSha256 = packageSha,
    CumulativeFileCount = deltas.Count,
    AddCount = deltas.Count(x => x.Status == "A"),
    ReplaceCount = deltas.Count(x => x.Status == "M"),
    NoOpCount = 0,
    PublicationPerformed = false,
    RemoteRefMutated = false,
    OperatorActionPerformed = false
};
File.WriteAllText(Path.Combine(output, "operator-bundle-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions{WriteIndented=true}), new UTF8Encoding(false));
Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions{WriteIndented=true}));

static List<(string Status,string Path)> ReadDelta(string root) {
    var text = Git(root, "diff", "--name-status", "--no-renames", PredecessorCommit, TargetHead);
    var list = new List<(string,string)>();
    foreach (var line in text.Split(new[]{"\r\n","\n"}, StringSplitOptions.RemoveEmptyEntries)) {
        var p=line.Split('\t'); if(p.Length!=2) throw new InvalidDataException("bad diff row: "+line);
        list.Add((p[0].Trim(), Normalize(p[1])));
    }
    return list.OrderBy(x=>x.Item2,StringComparer.Ordinal).Select(x=>(x.Item1,x.Item2)).ToList();
}

static void CreatePackage(string root,string path,IReadOnlyList<(string Status,string Path)> deltas) {
    if(File.Exists(path)) File.Delete(path);
    var files = deltas.Select(d => new { d.Path, Sha256 = HashFile(Path.Combine(root,d.Path.Replace('/',Path.DirectorySeparatorChar))) }).ToArray();
    var manifest = new {
        Schema = "matawaka.workbench-update-package/v0.10",
        PackageVersion = "0.10",
        TargetVersion,
        PredecessorTag,
        PredecessorCommit,
        TargetTag,
        PayloadRoot = "payload/",
        Files = files.OrderBy(x=>x.Path,StringComparer.Ordinal).Select(x=>new{x.Path,x.Sha256}).ToArray(),
        NetworkAccessRequested = false,
        CatalogMutationRequested = false,
        AgentExecuteRequested = false,
        ArbitraryProcessExecutionRequested = false,
        InstallerScriptExecutionRequested = false,
        NonEffects = new[]{
            "package is inert data until explicit Workbench update authority",
            "no delete or rename operation is represented",
            "no network/catalog/agent/arbitrary-process/installer-script request"
        }
    };
    using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
    var me=archive.CreateEntry("workbench-update-manifest.json",CompressionLevel.Optimal); me.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
    using(var s=me.Open()) using(var w=new StreamWriter(s,new UTF8Encoding(false))) w.Write(JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
    foreach(var f in files.OrderBy(x=>x.Path,StringComparer.Ordinal)) {
        var e=archive.CreateEntry("payload/"+f.Path,CompressionLevel.Optimal); e.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
        using var s=e.Open(); var b=File.ReadAllBytes(Path.Combine(root,f.Path.Replace('/',Path.DirectorySeparatorChar))); s.Write(b,0,b.Length);
    }
}

static void VerifyPackage(string package,IReadOnlyList<(string Status,string Path)> deltas,string root) {
    using var archive=ZipFile.OpenRead(package);
    var entries=archive.Entries.Select(e=>e.FullName).ToArray();
    Require(entries.Length==deltas.Count+1,"zip entry count mismatch");
    Require(entries[0]=="workbench-update-manifest.json","manifest not first");
    foreach(var d in deltas) {
        var name="payload/"+d.Path; var e=archive.GetEntry(name)??throw new InvalidDataException("missing "+name);
        using var s=e.Open(); using var ms=new MemoryStream(); s.CopyTo(ms);
        var observed=Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        var expected=HashFile(Path.Combine(root,d.Path.Replace('/',Path.DirectorySeparatorChar)));
        Require(observed==expected,"payload SHA mismatch: "+d.Path);
    }
}

static string Git(string wd,params string[] a){
    var psi=new ProcessStartInfo("git"){WorkingDirectory=wd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}; foreach(var x in a)psi.ArgumentList.Add(x);
    using var p=Process.Start(psi)??throw new InvalidDataException("git start failed"); var o=p.StandardOutput.ReadToEndAsync(); var e=p.StandardError.ReadToEndAsync(); if(!p.WaitForExit(60000)){try{p.Kill(true);}catch{} throw new InvalidDataException("git timeout");} Task.WaitAll(o,e); if(p.ExitCode!=0)throw new InvalidDataException("git failed: "+e.Result); return o.Result;
}
static string HashFile(string p){using var s=File.OpenRead(p);return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();}
static string Normalize(string p)=>p.Replace('\\','/').Trim('/');
static void Require(bool c,string m){if(!c)throw new InvalidDataException(m);}
