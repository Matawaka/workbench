using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

const string PredecessorCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
const string PredecessorTag = "workbench-v0.55.2-accepted";
const string TargetVersion = "0.60.1";
const string TargetTag = "workbench-v0.60.1-accepted";

var sourceRoot = FindRepositoryRoot();
var targetHead = Git(sourceRoot, "rev-parse", "HEAD").Trim();
var deltas = ReadSourceDelta(sourceRoot, PredecessorCommit, targetHead);
Require(deltas.Count > 0, "cumulative source delta is empty");
Require(deltas.Count <= 500, $"cumulative source delta exceeds v0.10 bound: {deltas.Count}");
Require(deltas.All(x => x.Status is "A" or "M"),
    "cumulative v0.55.2 -> v0.60.1 package contains a non Add/Replace operation");

var tempRoot = Path.Combine(Path.GetTempPath(), "matawaka-v0601-update-path-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    if (string.IsNullOrWhiteSpace(dotnetRoot) || !File.Exists(Path.Combine(dotnetRoot, "dotnet.exe")))
        throw new InvalidDataException("DOTNET_ROOT/dotnet.exe is unavailable for qualification setup.");

    var sharedPackages = Path.Combine(tempRoot, "nuget-packages");
    Directory.CreateDirectory(sharedPackages);
    RunProcess(
        Path.Combine(dotnetRoot, "dotnet.exe"),
        sourceRoot,
        TimeSpan.FromMinutes(5),
        "restore", Path.Combine(sourceRoot, "Matawaka.Workbench.sln"), "--packages", sharedPackages, "--nologo");

    var packagePath = Path.Combine(tempRoot, "workbench-v0.60.1-cumulative.zip");
    CreatePackage(sourceRoot, packagePath, deltas, TargetVersion, TargetTag);

    var happy = PrepareWorkspace(sourceRoot, tempRoot, "happy", dotnetRoot, sharedPackages);
    PreseedBuildAssets(sourceRoot, happy.RepositoryRoot);
    RequireCleanPredecessor(happy.RepositoryRoot);

    var intake = new LocalUpdateIntakeService();
    var materializer = new LocalUpdateMaterializationService(intake);
    var stagedPlanner = new StagedUpdateApplyPlanService();
    var applyBuild = new BoundedUpdateApplyBuildService(stagedPlanner);
    var orchestrator = new MaintenanceUpdateOrchestratorService(intake, materializer, stagedPlanner, applyBuild);

    // Hostile 1: package bytes changed after the read-only preview.
    var tamperedPackage = Path.Combine(tempRoot, "tampered-after-preview.zip");
    File.Copy(packagePath, tamperedPackage);
    var tamperedPreview = await orchestrator.PrepareAsync(tamperedPackage, happy.WorkspaceRoot, CancellationToken.None);
    await using (var append = new FileStream(tamperedPackage, FileMode.Append, FileAccess.Write, FileShare.None))
        append.WriteByte(0x42);
    await ExpectInvalidAsync(
        () => orchestrator.ExecuteConfirmedAsync(tamperedPreview, happy.WorkspaceRoot, CancellationToken.None),
        "package mutation after preview");
    RequireCleanPredecessor(happy.RepositoryRoot);

    // Hostile 2: a dirty predecessor must refuse materialization before tracked source apply.
    var dirtyPreview = await orchestrator.PrepareAsync(packagePath, happy.WorkspaceRoot, CancellationToken.None);
    var readme = Path.Combine(happy.RepositoryRoot, "README.md");
    await File.AppendAllTextAsync(readme, "\nV0601_QUALIFICATION_DIRTY_SENTINEL\n", new UTF8Encoding(false));
    await ExpectInvalidAsync(
        () => orchestrator.ExecuteConfirmedAsync(dirtyPreview, happy.WorkspaceRoot, CancellationToken.None),
        "dirty predecessor");
    Git(happy.RepositoryRoot, "checkout", "--", "README.md");
    RequireCleanPredecessor(happy.RepositoryRoot);

    // Hostile 3: staging file-set expansion is rejected before source mutation.
    var planned = await intake.PlanAsync(packagePath, happy.WorkspaceRoot, CancellationToken.None);
    var materialized = await materializer.MaterializeAsync(
        packagePath, planned.Receipt, happy.WorkspaceRoot, CancellationToken.None);
    var unexpected = Path.Combine(materialized.Receipt.StagingRoot, "payload", "unexpected-v0601.txt");
    await File.WriteAllTextAsync(unexpected, "not in manifest", new UTF8Encoding(false));
    await ExpectInvalidAsync(
        () => stagedPlanner.PlanAsync(materialized.Receipt, happy.WorkspaceRoot, CancellationToken.None),
        "extra staged file");
    Directory.Delete(materialized.Receipt.StagingRoot, recursive: true);
    RequireCleanPredecessor(happy.RepositoryRoot);

    // Happy path: exercise the exact accepted v0.10/v0.14 services through the real orchestrator.
    var preview = await orchestrator.PrepareAsync(packagePath, happy.WorkspaceRoot, CancellationToken.None);
    Require(preview.Status == "READY_FOR_EXPLICIT_UPDATE_CANDIDATE_MAINTENANCE_INTENT", "orchestrator preview not READY");
    Require(!preview.EffectAuthorized, "orchestrator preview unexpectedly authorized an effect");
    Require(preview.PredecessorCommit == PredecessorCommit && preview.PredecessorTag == PredecessorTag,
        "orchestrator preview predecessor binding mismatch");
    Require(preview.TargetVersion == TargetVersion && preview.TargetTag == TargetTag,
        "orchestrator preview target binding mismatch");

    var execution = await orchestrator.ExecuteConfirmedAsync(preview, happy.WorkspaceRoot, CancellationToken.None);
    Require(execution.Status == "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED", "orchestrator did not stop at built candidate");
    Require(!execution.LaunchPerformed && !execution.CheckpointAuthorized && !execution.PublicationAuthorized,
        "orchestrator widened launch/checkpoint/publication authority");
    Require(execution.ApplyBuild.Status == "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED", "apply/build receipt status mismatch");
    Require(execution.ApplyBuild.FreshApplyPlanVerified && execution.ApplyBuild.ExactSourceBytesApplied &&
            execution.ApplyBuild.WorkingTreeMatchesPlannedDelta && execution.ApplyBuild.OfflineBuildCompleted &&
            execution.ApplyBuild.OfflineAppPublishCompleted && execution.ApplyBuild.OfflineSemanticHostPublishCompleted,
        "apply/build evidence is incomplete");

    var expectedPaths = deltas.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var sourceChanges = execution.StagedApplyPlan.SourceChanges.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
    Require(sourceChanges.Select(x => x.Path).SequenceEqual(expectedPaths, StringComparer.Ordinal),
        "staged source-change set differs from cumulative Git delta");
    Require(sourceChanges.All(x => x.Action is "Add" or "Replace"), "staged plan contains non Add/Replace action");
    Require(execution.StagedApplyPlan.NoOpCount == 0, "cumulative package contains an unexpected NoOp");

    foreach (var path in expectedPaths)
    {
        var expected = HashFile(Path.Combine(sourceRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        var observed = HashFile(Path.Combine(happy.RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        Require(observed == expected, "applied target source bytes differ: " + path);
    }

    var gitChanged = Git(happy.RepositoryRoot, "status", "--porcelain=v1", "--untracked-files=all")
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Select(ParseStatusPath)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToArray();
    Require(gitChanged.SequenceEqual(expectedPaths, StringComparer.Ordinal),
        "post-apply working tree differs from the package source set");
    Require(Git(happy.RepositoryRoot, "rev-parse", "HEAD").Trim() == PredecessorCommit,
        "apply/build unexpectedly moved Git HEAD");
    Require(string.IsNullOrWhiteSpace(Git(happy.RepositoryRoot, "tag", "--list", TargetTag)),
        "apply/build unexpectedly created target accepted tag");

    var candidateExe = execution.ApplyBuild.CandidateExecutablePath;
    Require(File.Exists(candidateExe), "updater-built candidate executable is missing");
    Require(HashFile(candidateExe) == execution.ApplyBuild.CandidateExecutableSha256,
        "updater-built candidate executable SHA mismatch");
    RunNeutralBrandingSmoke(candidateExe);

    // Hostile 4: a syntactically invalid but correctly hashed Add must reach build,
    // fail there, and the accepted v0.14 transaction must restore the exact predecessor.
    var rollback = PrepareWorkspace(sourceRoot, tempRoot, "rollback", dotnetRoot, sharedPackages);
    PreseedBuildAssets(sourceRoot, rollback.RepositoryRoot);
    RequireCleanPredecessor(rollback.RepositoryRoot);
    var rollbackPackage = Path.Combine(tempRoot, "rollback-build-failure.zip");
    CreateSingleFilePackage(
        rollbackPackage,
        "src/Matawaka.Workbench.App/V0601RollbackProbeBroken.cs",
        "namespace Matawaka.Workbench.App;\nthis is deliberately invalid C sharp;\n",
        TargetVersion,
        "workbench-v0.60.1-rollback-probe");

    var rollbackIntake = new LocalUpdateIntakeService();
    var rollbackMaterializer = new LocalUpdateMaterializationService(rollbackIntake);
    var rollbackPlanner = new StagedUpdateApplyPlanService();
    var rollbackApply = new BoundedUpdateApplyBuildService(rollbackPlanner);
    var rollbackOrchestrator = new MaintenanceUpdateOrchestratorService(
        rollbackIntake, rollbackMaterializer, rollbackPlanner, rollbackApply);
    var rollbackPreview = await rollbackOrchestrator.PrepareAsync(
        rollbackPackage, rollback.WorkspaceRoot, CancellationToken.None);
    await ExpectInvalidAsync(
        () => rollbackOrchestrator.ExecuteConfirmedAsync(rollbackPreview, rollback.WorkspaceRoot, CancellationToken.None),
        "build failure rollback");
    RequireCleanPredecessor(rollback.RepositoryRoot);
    Require(!File.Exists(Path.Combine(rollback.RepositoryRoot, "src", "Matawaka.Workbench.App", "V0601RollbackProbeBroken.cs")),
        "failed build Add survived automatic rollback");
    Require(!Directory.Exists(Path.Combine(rollback.RepositoryRoot, "artifacts", $"app-v{TargetVersion}-gui-update")),
        "failed build candidate directory survived automatic rollback");

    var output = new
    {
        schema = "matawaka.workbench-v0601-cumulative-update-path-qualification/v0.1",
        status = "V0552_TO_V0601_REAL_UPDATER_PATH_QUALIFIED",
        predecessor_commit = PredecessorCommit,
        predecessor_tag = PredecessorTag,
        candidate_source_head = targetHead,
        target_version = TargetVersion,
        target_tag = TargetTag,
        cumulative_file_count = expectedPaths.Length,
        add_count = sourceChanges.Count(x => x.Action == "Add"),
        replace_count = sourceChanges.Count(x => x.Action == "Replace"),
        no_op_count = sourceChanges.Count(x => x.Action == "NoOp"),
        package_sha256 = HashFile(packagePath),
        build_source_manifest_sha256 = execution.ApplyBuild.BuildSourceManifestSha256,
        candidate_executable_sha256 = execution.ApplyBuild.CandidateExecutableSha256,
        semantic_host_executable_sha256 = execution.ApplyBuild.SemanticHostExecutableSha256,
        hostile_package_mutation_refused = true,
        hostile_dirty_predecessor_refused = true,
        hostile_extra_staged_file_refused = true,
        hostile_build_failure_rollback_clean = true,
        updater_built_candidate_neutral_branding_smoke = true,
        launch_performed_by_orchestrator = false,
        checkpoint_authorized_by_orchestrator = false,
        publication_authorized_by_orchestrator = false,
        accepted_tag_created = false,
        public_ref_mutated = false
    };
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

static (string WorkspaceRoot, string RepositoryRoot) PrepareWorkspace(
    string sourceRoot,
    string tempRoot,
    string name,
    string dotnetRoot,
    string sharedPackages)
{
    var workspace = Path.Combine(tempRoot, name, "workspace");
    var repository = Path.Combine(workspace, "Workbench");
    Directory.CreateDirectory(workspace);
    RunProcess("git", Path.GetDirectoryName(repository)!, TimeSpan.FromMinutes(2),
        "clone", "--no-hardlinks", sourceRoot, repository);
    Git(repository, "checkout", "--detach", PredecessorCommit);
    Git(repository, "config", "user.name", "Matawaka v0.60.1 Qualification");
    Git(repository, "config", "user.email", "qualification@localhost.invalid");
    var tagCommit = Git(repository, "rev-list", "-n", "1", PredecessorTag).Trim();
    Require(tagCommit == PredecessorCommit, "sandbox predecessor tag binding mismatch");

    CreateJunction(Path.Combine(workspace, ".dotnet-sdk"), dotnetRoot);
    Directory.CreateDirectory(Path.Combine(workspace, ".nuget"));
    CreateJunction(Path.Combine(workspace, ".nuget", "packages"), sharedPackages);
    Directory.CreateDirectory(Path.Combine(workspace, ".dotnet-home"));
    Directory.CreateDirectory(Path.Combine(workspace, ".tmp"));
    return (workspace, repository);
}

static void PreseedBuildAssets(string sourceRoot, string repositoryRoot)
{
    var sourceProjects = Path.Combine(sourceRoot, "src");
    foreach (var project in Directory.GetDirectories(sourceProjects))
    {
        var sourceObj = Path.Combine(project, "obj");
        if (!Directory.Exists(sourceObj)) continue;
        var destinationObj = Path.Combine(repositoryRoot, "src", Path.GetFileName(project), "obj");
        CopyDirectory(sourceObj, destinationObj);
    }
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source))
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    foreach (var dir in Directory.GetDirectories(source))
        CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
}

static void CreateJunction(string junction, string target)
{
    if (Directory.Exists(junction)) return;
    Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
    var result = RunProcess("cmd.exe", Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(30),
        "/d", "/s", "/c", $"mklink /J \"{junction}\" \"{target}\"");
    Require(Directory.Exists(junction), "junction was not created: " + result.Stdout + result.Stderr);
}

static IReadOnlyList<(string Status, string Path)> ReadSourceDelta(string root, string predecessor, string head)
{
    var text = Git(root, "diff", "--name-status", "--no-renames", predecessor, head);
    var result = new List<(string Status, string Path)>();
    foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = raw.Split('\t');
        if (parts.Length != 2) throw new InvalidDataException("Unexpected git diff --name-status row: " + raw);
        result.Add((parts[0].Trim(), Normalize(parts[1])));
    }
    return result.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
}

static void CreatePackage(
    string sourceRoot,
    string packagePath,
    IReadOnlyList<(string Status, string Path)> deltas,
    string targetVersion,
    string targetTag)
{
    var files = deltas.Select(delta => new
    {
        Path = delta.Path,
        Sha256 = HashFile(Path.Combine(sourceRoot, delta.Path.Replace('/', Path.DirectorySeparatorChar)))
    }).ToArray();
    CreateZip(packagePath, targetVersion, targetTag, files.Select(x => (x.Path, x.Sha256,
        File.ReadAllBytes(Path.Combine(sourceRoot, x.Path.Replace('/', Path.DirectorySeparatorChar))))).ToArray());
}

static void CreateSingleFilePackage(
    string packagePath,
    string path,
    string content,
    string targetVersion,
    string targetTag)
{
    var bytes = new UTF8Encoding(false).GetBytes(content);
    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    CreateZip(packagePath, targetVersion, targetTag, new[] { (Normalize(path), sha, bytes) });
}

static void CreateZip(
    string packagePath,
    string targetVersion,
    string targetTag,
    IReadOnlyList<(string Path, string Sha256, byte[] Bytes)> files)
{
    if (File.Exists(packagePath)) File.Delete(packagePath);
    var manifest = new
    {
        Schema = LocalUpdateIntakeService.ManifestSchema,
        PackageVersion = "0.10",
        TargetVersion = targetVersion,
        PredecessorTag,
        PredecessorCommit,
        TargetTag = targetTag,
        PayloadRoot = "payload/",
        Files = files.OrderBy(x => x.Path, StringComparer.Ordinal).Select(x => new { x.Path, x.Sha256 }).ToArray(),
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

    using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
    var manifestEntry = archive.CreateEntry("workbench-update-manifest.json", CompressionLevel.Optimal);
    manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using (var stream = manifestEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

    foreach (var file in files.OrderBy(x => x.Path, StringComparer.Ordinal))
    {
        var entry = archive.CreateEntry("payload/" + file.Path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(file.Bytes, 0, file.Bytes.Length);
    }
}

static void RunNeutralBrandingSmoke(string candidateExe)
{
    var diag = Path.Combine(Path.GetTempPath(), "Matawaka", "Workbench", "branding-review-v060");
    var smoke = Path.Combine(diag, "smoke.json");
    var failure = Path.Combine(diag, "failure.json");
    if (File.Exists(smoke)) File.Delete(smoke);
    if (File.Exists(failure)) File.Delete(failure);

    RunProcess(candidateExe, Path.GetDirectoryName(candidateExe)!, TimeSpan.FromSeconds(20), "--branding-review-smoke");
    Require(File.Exists(smoke), "updater-built candidate did not emit branding smoke receipt");
    using var document = JsonDocument.Parse(File.ReadAllText(smoke, Encoding.UTF8));
    var root = document.RootElement;
    Require(root.GetProperty("Status").GetString() == "BRANDING_REVIEW_WINDOW_CONTENT_RENDERED",
        "updater-built candidate branding smoke status mismatch");
    Require(root.GetProperty("HistoricalVersionTransitionArtworkActive").GetBoolean() == false,
        "historical transition artwork became active");
    Require(root.GetProperty("ReviewActionReplicaCount").GetInt32() == 4,
        "branding review action replica count mismatch");
    Require(root.GetProperty("SplashRenderedHasVisibleVariation").GetBoolean(),
        "splash visible variation was not rendered");
    Require(root.GetProperty("CurrentBrandingRenderedHasVisibleVariation").GetBoolean(),
        "current branding visible variation was not rendered");
}

static async Task ExpectInvalidAsync(Func<Task> action, string label)
{
    try
    {
        await action();
        throw new Exception("Expected fail-closed refusal was not raised: " + label);
    }
    catch (InvalidDataException)
    {
        // Expected fail-closed path.
    }
}

static void RequireCleanPredecessor(string repositoryRoot)
{
    Require(Git(repositoryRoot, "rev-parse", "HEAD").Trim() == PredecessorCommit,
        "sandbox HEAD moved from exact predecessor");
    Require(string.IsNullOrWhiteSpace(Git(repositoryRoot, "status", "--porcelain=v1", "--untracked-files=all")),
        "sandbox predecessor source is not clean");
    Require(Git(repositoryRoot, "rev-list", "-n", "1", PredecessorTag).Trim() == PredecessorCommit,
        "sandbox predecessor tag moved");
}

static string ParseStatusPath(string line)
{
    var path = line.Length >= 4 ? line[3..].Trim() : line.Trim();
    if (path.Contains(" -> ", StringComparison.Ordinal)) path = path.Split(" -> ", StringSplitOptions.None)[^1].Trim();
    return Normalize(path.Trim('"'));
}

static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static string Git(string root, params string[] args)
{
    var result = RunProcess("git", root, TimeSpan.FromMinutes(2), args);
    return result.Stdout;
}

static (string Stdout, string Stderr) RunProcess(
    string fileName,
    string workingDirectory,
    TimeSpan timeout,
    params string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    psi.Environment["GIT_PAGER"] = "cat";
    psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
    foreach (var arg in args) psi.ArgumentList.Add(arg);
    using var process = new Process { StartInfo = psi };
    if (!process.Start()) throw new InvalidDataException("Unable to start process: " + fileName);
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new InvalidDataException($"Process timed out: {fileName} {string.Join(' ', args)}");
    }
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
        throw new InvalidDataException($"Process failed ({process.ExitCode}): {fileName} {string.Join(' ', args)}\n{stderr}\n{stdout}");
    return (stdout, stderr);
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")) && File.Exists(Path.Combine(current.FullName, "Matawaka.Workbench.sln")))
            return current.FullName;
        current = current.Parent;
    }
    throw new InvalidDataException("Unable to locate Workbench repository root.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
