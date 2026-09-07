using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string PredecessorCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
const string PredecessorTag = "workbench-v0.55.2-accepted";
const string TargetHead = "e466543964d97cf25a81fd571125d9b0282371dd";
const string TargetVersion = "0.60.1";
const string TargetTag = "workbench-v0.60.1-accepted";
const string ManifestSchema = "matawaka.workbench-update-package/v0.10";
const string ExpectedPackageSha256 = "bfbcd4bcebb211230c145c1ce564e5896e5612007b18bfb663e4ca8e04ef92a5";

if (args.Length != 2)
    throw new InvalidDataException("usage: <candidate-repository-root> <output-zip>");

var sourceRoot = Path.GetFullPath(args[0]);
var packagePath = Path.GetFullPath(args[1]);
if (!Directory.Exists(Path.Combine(sourceRoot, ".git")))
    throw new InvalidDataException("candidate repository missing");
if (Git(sourceRoot, "rev-parse", "HEAD").Trim() != TargetHead)
    throw new InvalidDataException("candidate source head mismatch");
if (Git(sourceRoot, "rev-list", "-n", "1", PredecessorTag).Trim() != PredecessorCommit)
    throw new InvalidDataException("predecessor tag binding mismatch");

var deltas = new List<(string Status, string Path)>();
var diff = Git(sourceRoot, "diff", "--name-status", "--no-renames", PredecessorCommit, TargetHead);
foreach (var line in diff.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
{
    var parts = line.Split('\t');
    if (parts.Length != 2) throw new InvalidDataException("unexpected git diff row: " + line);
    deltas.Add((parts[0].Trim(), Normalize(parts[1])));
}
deltas = deltas.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
if (deltas.Count != 79) throw new InvalidDataException($"expected 79 cumulative files, observed {deltas.Count}");
if (deltas.Count(x => x.Status == "A") != 73 || deltas.Count(x => x.Status == "M") != 6)
    throw new InvalidDataException("Add/Replace counts differ from qualified transition");
if (deltas.Any(x => x.Status is not ("A" or "M")))
    throw new InvalidDataException("non Add/Replace operation present");

var files = deltas.Select(delta =>
{
    var full = Path.Combine(sourceRoot, delta.Path.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(full)) throw new InvalidDataException("target file missing: " + delta.Path);
    return (Path: delta.Path, Sha256: HashFile(full), Bytes: File.ReadAllBytes(full));
}).ToArray();

Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
if (File.Exists(packagePath)) File.Delete(packagePath);
var manifest = new
{
    Schema = ManifestSchema,
    PackageVersion = "0.10",
    TargetVersion,
    PredecessorTag,
    PredecessorCommit,
    TargetTag,
    PayloadRoot = "payload/",
    Files = files.Select(x => new { x.Path, x.Sha256 }).ToArray(),
    NetworkAccessRequested = false,
    CatalogMutationRequested = false,
    AgentExecuteRequested = false,
    ArbitraryProcessExecutionRequested = false,
    InstallerScriptExecutionRequested = false,
    NonEffects = new[]
    {
        "package is inert data until explicit Workbench update authority",
        "no delete or rename operation is represented",
        "no network/catalog/agent/arbitrary-process/installer-script request"
    }
};

using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
{
    var manifestEntry = archive.CreateEntry("workbench-update-manifest.json", CompressionLevel.Optimal);
    manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using (var stream = manifestEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

    foreach (var file in files)
    {
        var entry = archive.CreateEntry("payload/" + file.Path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(file.Bytes, 0, file.Bytes.Length);
    }
}

var packageSha = HashFile(packagePath);
if (packageSha != ExpectedPackageSha256)
    throw new InvalidDataException($"deterministic package SHA mismatch: expected={ExpectedPackageSha256} observed={packageSha}");

Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = "matawaka.workbench-v0601-operator-package-build/v0.1",
    status = "EXACT_QUALIFIED_PACKAGE_REPRODUCED",
    predecessor_commit = PredecessorCommit,
    target_source_head = TargetHead,
    target_version = TargetVersion,
    target_tag = TargetTag,
    cumulative_file_count = files.Length,
    add_count = deltas.Count(x => x.Status == "A"),
    replace_count = deltas.Count(x => x.Status == "M"),
    package_sha256 = packageSha
}, new JsonSerializerOptions { WriteIndented = true }));

static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static string Git(string root, params string[] arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    psi.Environment["GIT_PAGER"] = "cat";
    psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
    foreach (var argument in arguments) psi.ArgumentList.Add(argument);
    using var process = Process.Start(psi) ?? throw new InvalidDataException("failed to start git");
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60_000))
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new InvalidDataException("git timed out");
    }
    Task.WaitAll(stdoutTask, stderrTask);
    var stdout = stdoutTask.Result;
    var stderr = stderrTask.Result;
    if (process.ExitCode != 0)
        throw new InvalidDataException("git failed: " + stderr.Trim());
    return stdout;
}
