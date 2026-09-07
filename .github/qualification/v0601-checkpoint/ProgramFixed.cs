using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

internal static class ProgramFixed
{
    private const string FirstParent = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    private const string FirstParentTag = "workbench-v0.55.2-accepted";
    private const string SecondParent = "6541dc32182c970c8e1a6ade426a6cee7086511b";
    private const string TargetVersion = "0.60.1";
    private const string TargetTag = "workbench-v0.60.1-accepted";
    private const string HistoricalV060Tag = "workbench-v0.60-accepted";
    private const string ChildWorkspaceEnv = "MATAWAKA_V0601_CHECKPOINT_CHILD_WORKSPACE";
    private const string ChildResultEnv = "MATAWAKA_V0601_CHECKPOINT_CHILD_RESULT";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main()
    {
        try
        {
            var childWorkspace = Environment.GetEnvironmentVariable(ChildWorkspaceEnv);
            if (!string.IsNullOrWhiteSpace(childWorkspace))
                return await RunChildAsync(childWorkspace);
            await RunParentAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("V0601_CHECKPOINT_QUALIFICATION_FAILED: " + ex);
            return 2;
        }
    }

    private static async Task RunParentAsync()
    {
        var sourceRoot = FindRepositoryRoot();
        var processPath = Environment.ProcessPath;
        Require(!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath), "qualification process image unavailable");
        var tempRoot = Path.Combine(Path.GetTempPath(), "matawaka-v0601-checkpoint-fixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var workspace = Path.Combine(tempRoot, "workspace");
            var repository = Path.Combine(workspace, "Workbench");
            Directory.CreateDirectory(workspace);
            Run("git", tempRoot, "clone", "--no-hardlinks", sourceRoot, repository);
            Git(repository, "checkout", "--detach", FirstParent);
            Git(repository, "config", "user.name", "Matawaka v0.60.1 Checkpoint Qualification");
            Git(repository, "config", "user.email", "checkpoint-qualification@localhost.invalid");
            Require(Git(repository, "rev-parse", "HEAD").Trim() == FirstParent, "sandbox predecessor HEAD mismatch");
            Require(Git(repository, "rev-list", "-n", "1", FirstParentTag).Trim() == FirstParent, "sandbox predecessor tag mismatch");
            Require(string.IsNullOrWhiteSpace(Git(repository, "tag", "--list", TargetTag)), "sandbox target tag unexpectedly exists");
            Require(string.IsNullOrWhiteSpace(Git(repository, "tag", "--list", HistoricalV060Tag)), "historical v0.60 tag unexpectedly exists");
            Run("git", repository, "cat-file", "-e", SecondParent + "^{commit}");

            var changedPath = "src/Matawaka.Workbench.App/V0601CheckpointQualificationPayload.cs";
            var changedFull = Path.Combine(repository, changedPath.Replace('/', Path.DirectorySeparatorChar));
            var changedBytes = new UTF8Encoding(false).GetBytes(
                "namespace Matawaka.Workbench.App;\ninternal static class V0601CheckpointQualificationPayload { internal const string Marker = \"exact-v0601-checkpoint\"; }\n");
            await File.WriteAllBytesAsync(changedFull, changedBytes);
            var changedSha = Sha256(changedFull);
            Require(StatusPaths(repository).SequenceEqual(new[] { changedPath }, StringComparer.Ordinal), "sandbox changed set mismatch");

            WriteImportReceipt(repository);

            var appDir = Path.Combine(repository, "artifacts", "app-v0.60.1-gui-update");
            var semanticDir = Path.Combine(repository, "artifacts", "semantic-host-v0.60.1");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(semanticDir);
            var candidateExe = Path.Combine(appDir, "Matawaka.Workbench.App.exe");
            var semanticExe = Path.Combine(semanticDir, "Matawaka.Workbench.SemanticHost.exe");
            File.Copy(processPath!, candidateExe, overwrite: true);
            File.Copy(processPath!, semanticExe, overwrite: true);
            var candidateSha = Sha256(candidateExe);
            var semanticSha = Sha256(semanticExe);

            var checkpointsDir = Path.Combine(repository, "artifacts", "checkpoints");
            Directory.CreateDirectory(checkpointsDir);
            var manifestPath = Path.Combine(checkpointsDir, "v0.60.1-source-manifest-checkpoint-qualification.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            {
                Schema = "matawaka.workbench-build-source-manifest/v0.60",
                Version = TargetVersion,
                PredecessorGitSha = FirstParent,
                ObservedAt = DateTimeOffset.Now,
                Files = new[] { new { Path = changedPath, Sha256 = changedSha } }
            }, JsonOptions), new UTF8Encoding(false));
            var manifestSha = Sha256(manifestPath);

            var change = new WorkbenchStagedSourceChange(changedPath, "Add", null, changedSha, changedBytes.Length);
            var authority = new WorkbenchUpdateApplyBuildAuthorityReceipt(
                BoundedUpdateApplyBuildService.AuthoritySchema,
                "qualification-fixture",
                "workbench.update.apply-source-and-build",
                repository,
                TargetVersion,
                TargetTag,
                FirstParentTag,
                FirstParent,
                Path.Combine(repository, ".workbench", "qualification-staging"),
                new string('a', 64),
                processPath!,
                Sha256(processPath!),
                "sandbox checkpoint qualification fixture; no production authority",
                true, true, true, true, true,
                false, false, false, false, false, false,
                new[] { "qualification-only exact sandbox launch" },
                new[] { "no remote publication", "no production accepted identity" });

            var buildReceipt = new WorkbenchUpdateApplyBuildReceipt(
                BoundedUpdateApplyBuildService.ReceiptSchema,
                BoundedUpdateApplyBuildService.Version,
                DateTimeOffset.Now,
                TargetVersion,
                TargetTag,
                FirstParentTag,
                FirstParent,
                Path.Combine(repository, ".workbench", "qualification-staging"),
                Path.Combine(repository, "artifacts", "update-apply-plans", "qualification-plan.json"),
                new string('b', 64),
                new[] { change },
                authority,
                true, true, true, true, true, true,
                manifestPath,
                manifestSha,
                candidateExe,
                candidateSha,
                semanticExe,
                semanticSha,
                "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED",
                new[] { "qualification fixture only", "no checkpoint or publication authority in build receipt" },
                "Sandbox receipt used only to exercise accepted launch/bootstrap/checkpoint mechanics.");

            var applyDir = Path.Combine(repository, "artifacts", "update-applies");
            Directory.CreateDirectory(applyDir);
            var buildReceiptPath = Path.Combine(applyDir, "apply-build-v0.14-v0601-checkpoint-qualification.json");
            await File.WriteAllTextAsync(buildReceiptPath, JsonSerializer.Serialize(buildReceipt, JsonOptions), new UTF8Encoding(false));

            var bootstrap = new TransitionBootstrapV040Service();
            var prepared = await bootstrap.PrepareAsync(
                buildReceipt, buildReceiptPath, workspace,
                "qualification fixture representing one explicit Update Workbench transition",
                CancellationToken.None);

            var resultPath = Path.Combine(repository, "artifacts", "qualification-v0601", "checkpoint-child-result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            Environment.SetEnvironmentVariable(ChildWorkspaceEnv, workspace);
            Environment.SetEnvironmentVariable(ChildResultEnv, resultPath);

            var launcher = new BoundedUpdateApplyBuildService(new StagedUpdateApplyPlanService());
            var launched = await launcher.LaunchCandidateAsync(buildReceipt, workspace, CancellationToken.None);
            var handoff = await new CandidateLaunchHandoffV039Service().ObserveAndPersistAsync(
                launched.Receipt, launched.ArtifactPath, workspace, CancellationToken.None);
            var activated = await bootstrap.ActivateAsync(
                prepared.Lease, prepared.LeasePath,
                launched.Receipt, launched.ArtifactPath,
                handoff.Receipt, handoff.ArtifactPath,
                workspace, CancellationToken.None);

            Environment.SetEnvironmentVariable(ChildWorkspaceEnv, null);
            Environment.SetEnvironmentVariable(ChildResultEnv, null);

            using (var child = Process.GetProcessById(launched.Receipt.ProcessId))
            {
                if (!child.WaitForExit(60_000))
                {
                    try { child.Kill(entireProcessTree: true); } catch { }
                    throw new InvalidDataException("checkpoint qualification child timed out");
                }
            }
            // Process.GetProcessById() is an observation handle, not the Process instance
            // that performed Start(). ExitCode is intentionally not read from it.
            Require(File.Exists(resultPath), "checkpoint qualification child canonical result missing after process exit");

            using var resultDoc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, Encoding.UTF8));
            var r = resultDoc.RootElement;
            Require(r.GetProperty("status").GetString() == "V0601_ONE_SHOT_TWO_PARENT_CHECKPOINT_QUALIFIED", "child status mismatch");
            foreach (var field in new[]
            {
                "two_parent_commit_created", "parent_order_verified", "target_tag_peels_to_new_head",
                "working_tree_clean", "historical_v060_tag_absent", "one_shot_replay_refused",
                "hostile_state_refused", "hostile_pid_refused", "hostile_process_sha_refused",
                "hostile_acceptance_tamper_refused", "hostile_import_tamper_refused",
                "hostile_source_drift_refused", "bootstrap_completed_accepted"
            }) Require(r.GetProperty(field).GetBoolean(), "child evidence false: " + field);
            Require(!r.GetProperty("remote_push_allowed").GetBoolean(), "remote push authority widened");
            Require(!r.GetProperty("network_access_allowed").GetBoolean(), "network authority widened");
            Require(!r.GetProperty("automatic_retry_allowed").GetBoolean(), "retry authority widened");

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "matawaka.workbench-v0601-checkpoint-sandbox-qualification/v0.2",
                status = "V0601_FIRST_BOOT_CHECKPOINT_PATH_QUALIFIED",
                predecessor = FirstParent,
                predecessor_tag = FirstParentTag,
                second_parent = SecondParent,
                target_tag = TargetTag,
                candidate_executable_sha256 = candidateSha,
                launch_receipt_created = launched.Receipt.Status == "CANDIDATE_LAUNCHED_NOT_ACCEPTED",
                handoff_verified = handoff.Receipt.Status == CandidateLaunchHandoffV039Service.SuccessStatus,
                bootstrap_activated = activated.State == TransitionBootstrapV040Service.ActivatedState,
                child = JsonSerializer.Deserialize<object>(r.GetRawText()),
                publication_performed = false,
                remote_ref_mutated = false,
                process_exit_observed_without_unsupported_exit_code_read = true
            }, JsonOptions));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ChildWorkspaceEnv, null);
            Environment.SetEnvironmentVariable(ChildResultEnv, null);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunChildAsync(string workspace)
    {
        var resultPath = Environment.GetEnvironmentVariable(ChildResultEnv)
            ?? throw new InvalidDataException("child result path missing");
        var repository = Path.Combine(workspace, "Workbench");
        var bootstrap = new TransitionBootstrapV040Service();
        var claim = await bootstrap.TryClaimFirstBootAsync(workspace, TargetVersion, TargetTag, CancellationToken.None)
            ?? throw new InvalidDataException("child could not claim exact first-boot bootstrap lease");
        var replay = await bootstrap.TryClaimFirstBootAsync(workspace, TargetVersion, TargetTag, CancellationToken.None);
        var oneShotReplayRefused = replay is null;

        var processPath = Environment.ProcessPath ?? throw new InvalidDataException("child process path missing");
        var processSha = Sha256(processPath);
        var observation = new WorkbenchAcceptanceProviderObservation(
            "checkpoint-qualification-fixture",
            new string('1', 64), new string('2', 64), "qualification-only", true, true,
            new string('3', 64), true, true, true, true, true, true, true,
            new string('4', 64), new string('5', 64));
        var acceptance = new WorkbenchAcceptanceReceipt(
            LocalCheckpointV0601Service.AcceptanceSchema,
            TargetVersion,
            "checkpoint-qualification-" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            true,
            processSha,
            observation,
            observation,
            "NOT_RUN_BY_CHECKPOINT_FIXTURE",
            Array.Empty<string>(),
            new[] { new WorkbenchAcceptanceCheck("qualification-fixture", true, "passing synthetic checkpoint input", "passing synthetic checkpoint input") },
            new[] { "fixture does not substitute for the real Workbench v0.60.1 acceptance harness" },
            "Synthetic passing acceptance is used only to exercise checkpoint evidence binding; the real first-boot application still runs WorkbenchV0601AcceptanceHarness.");

        var acceptanceDir = Path.Combine(repository, "artifacts", "acceptance");
        Directory.CreateDirectory(acceptanceDir);
        var acceptancePath = Path.Combine(acceptanceDir, "v0.60.1-checkpoint-qualification.json");
        var acceptanceBytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(acceptance, JsonOptions));
        await File.WriteAllBytesAsync(acceptancePath, acceptanceBytes);

        var checkpoint = new LocalCheckpointV0601Service();
        var hostileStateRefused = await ExpectInvalidAsync(() => checkpoint.PreviewAsync(
            workspace, acceptancePath, acceptance,
            claim.Lease with { State = TransitionBootstrapV040Service.ActivatedState }, CancellationToken.None));
        var hostilePidRefused = await ExpectInvalidAsync(() => checkpoint.PreviewAsync(
            workspace, acceptancePath, acceptance,
            claim.Lease with { ProcessId = Environment.ProcessId + 1 }, CancellationToken.None));
        var hostileProcessShaRefused = await ExpectInvalidAsync(() => checkpoint.PreviewAsync(
            workspace, acceptancePath, acceptance,
            claim.Lease with { CandidateExecutableSha256 = new string('0', 64) }, CancellationToken.None));

        var candidate = await checkpoint.PreviewAsync(workspace, acceptancePath, acceptance, claim.Lease, CancellationToken.None);

        await File.AppendAllTextAsync(acceptancePath, " \n", new UTF8Encoding(false));
        var hostileAcceptanceTamperRefused = await ExpectInvalidAsync(() => checkpoint.AcceptFromBootstrapAsync(candidate, claim.Lease, CancellationToken.None));
        await File.WriteAllBytesAsync(acceptancePath, acceptanceBytes);

        var importPath = candidate.PublicImportReceiptPath;
        var importBytes = await File.ReadAllBytesAsync(importPath);
        await File.AppendAllTextAsync(importPath, " \n", new UTF8Encoding(false));
        var hostileImportTamperRefused = await ExpectInvalidAsync(() => checkpoint.AcceptFromBootstrapAsync(candidate, claim.Lease, CancellationToken.None));
        await File.WriteAllBytesAsync(importPath, importBytes);

        var changedPath = candidate.ChangedFiles.Single();
        var changedFull = Path.Combine(repository, changedPath.Replace('/', Path.DirectorySeparatorChar));
        var sourceBytes = await File.ReadAllBytesAsync(changedFull);
        await File.AppendAllTextAsync(changedFull, "// drift\n", new UTF8Encoding(false));
        var hostileSourceDriftRefused = await ExpectInvalidAsync(() => checkpoint.AcceptFromBootstrapAsync(candidate, claim.Lease, CancellationToken.None));
        await File.WriteAllBytesAsync(changedFull, sourceBytes);

        var accepted = await checkpoint.AcceptFromBootstrapAsync(candidate, claim.Lease, CancellationToken.None);
        var checkpointPath = await LocalCheckpointV0601Service.WriteReceiptAsync(workspace, accepted, CancellationToken.None);
        var completed = await bootstrap.FinalizeAcceptedAsync(claim, acceptancePath, checkpointPath, CancellationToken.None);

        var parentParts = Git(repository, "rev-list", "--parents", "-n", "1", accepted.NewHead)
            .Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parentOrderVerified = parentParts.Length == 3 && parentParts[1] == FirstParent && parentParts[2] == SecondParent;
        var peeled = Git(repository, "rev-list", "-n", "1", TargetTag).Trim();
        var historical = Git(repository, "tag", "--list", HistoricalV060Tag).Trim();
        var clean = string.IsNullOrWhiteSpace(Git(repository, "status", "--porcelain=v1", "--untracked-files=all"));
        var afterCompletedReplay = await bootstrap.TryClaimFirstBootAsync(workspace, TargetVersion, TargetTag, CancellationToken.None);

        var result = new
        {
            schema = "matawaka.workbench-v0601-checkpoint-child-qualification/v0.2",
            status = "V0601_ONE_SHOT_TWO_PARENT_CHECKPOINT_QUALIFIED",
            new_head = accepted.NewHead,
            tag = accepted.Tag,
            first_parent = accepted.FirstParent,
            second_parent = accepted.SecondParent,
            two_parent_commit_created = accepted.TwoParentCommitCreated,
            parent_order_verified = accepted.ParentOrderVerified && parentOrderVerified,
            target_tag_peels_to_new_head = peeled == accepted.NewHead,
            working_tree_clean = accepted.WorkingTreeCleanAfterCommit && clean,
            historical_v060_tag_absent = string.IsNullOrWhiteSpace(historical),
            one_shot_replay_refused = oneShotReplayRefused && afterCompletedReplay is null,
            hostile_state_refused = hostileStateRefused,
            hostile_pid_refused = hostilePidRefused,
            hostile_process_sha_refused = hostileProcessShaRefused,
            hostile_acceptance_tamper_refused = hostileAcceptanceTamperRefused,
            hostile_import_tamper_refused = hostileImportTamperRefused,
            hostile_source_drift_refused = hostileSourceDriftRefused,
            bootstrap_completed_accepted = completed.State == TransitionBootstrapV040Service.CompletedState,
            remote_push_allowed = accepted.RemotePushAllowed,
            network_access_allowed = accepted.NetworkAccessAllowed,
            automatic_retry_allowed = accepted.AutomaticRetryAllowed,
            publication_performed = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
        return 0;
    }

    private static void WriteImportReceipt(string repository)
    {
        var refs = Git(repository, "for-each-ref", "--format=%(refname)%00%(objectname)", "refs");
        var normalized = string.Join("\n", refs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x, StringComparer.Ordinal)) + "\n";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        var dir = Path.Combine(repository, "artifacts", "convergence-v0601");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, PublicMainImportReceiptVerifierV0601.ReceiptFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Schema = PublicMainImportReceiptVerifierV0601.Schema,
            Version = "0.1.0",
            Status = PublicMainImportReceiptVerifierV0601.Status,
            ObservedAt = DateTimeOffset.Now,
            RepositoryRoot = repository,
            FixedRemote = PublicMainImportReceiptVerifierV0601.RemoteUrl,
            ExpectedRemoteMain = SecondParent,
            RemoteMainObserved = SecondParent,
            ImportedCommit = SecondParent,
            LocalHeadBefore = FirstParent,
            LocalHeadAfter = FirstParent,
            LocalAcceptedTag = FirstParentTag,
            LocalAcceptedTagCommit = FirstParent,
            RefsDigestBefore = digest,
            RefsDigestAfter = digest,
            GitRefsUnchanged = true,
            ObjectPresentBefore = true,
            ObjectPresentAfter = true,
            ObjectDatabaseChanged = false,
            FetchUsedNoRef = true,
            NetworkReadPerformed = false,
            RefMutationPerformed = false,
            RemoteWritePerformed = false,
            SourceMutationPerformed = false,
            AutomaticRetryPerformed = false,
            PublicationAuthorityCreated = false,
            QualificationFixture = true,
            Note = "Sandbox verifier fixture only; operator-host requires the separately generated canonical no-ref import receipt."
        }, JsonOptions), new UTF8Encoding(false));
    }

    private static async Task<bool> ExpectInvalidAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static string[] StatusPaths(string repository)
        => Git(repository, "status", "--porcelain=v1", "--untracked-files=all")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path.Split(" -> ", StringSplitOptions.None)[^1].Trim() : path)
            .Select(path => path.Trim('"').Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Git(string root, params string[] args) => Run("git", root, args).Stdout;

    private static (string Stdout, string Stderr) Run(string fileName, string workingDirectory, params string[] args)
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
        using var process = Process.Start(psi) ?? throw new InvalidDataException("failed to start process: " + fileName);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("process timed out: " + fileName);
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"process failed ({process.ExitCode}): {fileName} {string.Join(' ', args)}\n{stderr}\n{stdout}");
        return (stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) && File.Exists(Path.Combine(current.FullName, "Matawaka.Workbench.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidDataException("repository root unavailable");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
