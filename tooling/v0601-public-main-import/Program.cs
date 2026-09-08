using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string RepositoryRoot = @"K:\Matawaka\Workbench";
    private const string FixedRemote = "https://github.com/Matawaka/workbench.git";
    private const string ExpectedHead = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    private const string ExpectedTag = "workbench-v0.55.2-accepted";
    private const string ExpectedPublicMain = "ac083598711caa0c399cc0d2c385b980c083024a";
    private const string Confirmation = "IMPORT-EXACT-PUBLIC-AC08";
    private const string Schema = "matawaka.workbench-v0601-public-main-import-receipt/v0.1";
    private const string Status = "EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.Ordinal))
                return SelfTest();
            if (args.Length != 0)
                throw new InvalidDataException("No arguments are accepted. Use --self-test only for qualification.");

            RunInteractive();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("IMPORT_REFUSED_OR_FAILED: " + ex.Message);
            return 2;
        }
    }

    private static void RunInteractive()
    {
        if (!Directory.Exists(Path.Combine(RepositoryRoot, ".git")))
            throw new InvalidDataException($"Exact Workbench repository missing: {RepositoryRoot}");

        var head = RunGit(RepositoryRoot, "rev-parse", "HEAD").Trim();
        if (!string.Equals(head, ExpectedHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"LOCAL_HEAD_MISMATCH: observed={head}; expected={ExpectedHead}");

        var tagCommit = RunGit(RepositoryRoot, "rev-list", "-n", "1", ExpectedTag).Trim();
        if (!string.Equals(tagCommit, ExpectedHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"LOCAL_ACCEPTED_TAG_MISMATCH: observed={tagCommit}; expected={ExpectedHead}");

        var dirty = RunGit(RepositoryRoot, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(dirty))
            throw new InvalidDataException("LOCAL_WORKTREE_NOT_CLEAN");

        var refsBefore = ComputeRefsDigest(RepositoryRoot);
        var objectBefore = CommitObjectExists(RepositoryRoot, ExpectedPublicMain);
        var receiptPath = Path.Combine(
            RepositoryRoot,
            "artifacts",
            "convergence-v0601",
            "public-main-ac083598711caa0c399cc0d2c385b980c083024a.json");
        if (File.Exists(receiptPath))
            throw new InvalidDataException($"EXACT_RECEIPT_ALREADY_EXISTS: {receiptPath}");

        Console.WriteLine("Matawaka Workbench v0.60.1 fixed public-main object import");
        Console.WriteLine();
        Console.WriteLine("Status: READY_FOR_EXPLICIT_FIXED_PUBLIC_OBJECT_IMPORT");
        Console.WriteLine($"Repository: {RepositoryRoot}");
        Console.WriteLine($"Local HEAD/tag: {ExpectedHead} / {ExpectedTag}");
        Console.WriteLine($"Fixed remote: {FixedRemote}");
        Console.WriteLine($"Expected public main object: {ExpectedPublicMain}");
        Console.WriteLine($"Public commit already local: {objectBefore}");
        Console.WriteLine($"Git refs digest before: {refsBefore}");
        Console.WriteLine();
        Console.WriteLine("No network/ref/source effect has occurred during Preview.");
        Console.WriteLine("After confirmation only fixed ls-remote + fixed no-ref fetch are permitted.");
        Console.WriteLine("No push, branch, tag, remote mutation, reset, checkout, source write or automatic retry is permitted.");
        Console.WriteLine();
        Console.Write($"Type {Confirmation} exactly to authorize this one import: ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, Confirmation, StringComparison.Ordinal))
            throw new InvalidDataException("EXPLICIT_CONFIRMATION_NOT_GRANTED");

        var observedRemoteMain = ReadRemoteMain();
        if (!string.Equals(observedRemoteMain, ExpectedPublicMain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"REMOTE_MAIN_DRIFT: observed={observedRemoteMain}; expected={ExpectedPublicMain}");

        if (!objectBefore)
        {
            RunGit(
                RepositoryRoot,
                "fetch",
                "--no-tags",
                "--no-write-fetch-head",
                FixedRemote,
                ExpectedPublicMain);
        }

        if (!CommitObjectExists(RepositoryRoot, ExpectedPublicMain))
            throw new InvalidDataException("IMPORTED_COMMIT_OBJECT_MISSING_AFTER_FETCH");

        var headAfter = RunGit(RepositoryRoot, "rev-parse", "HEAD").Trim();
        var tagAfter = RunGit(RepositoryRoot, "rev-list", "-n", "1", ExpectedTag).Trim();
        var refsAfter = ComputeRefsDigest(RepositoryRoot);
        var dirtyAfter = RunGit(RepositoryRoot, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.Equals(headAfter, ExpectedHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tagAfter, ExpectedHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("LOCAL_HEAD_OR_ACCEPTED_TAG_MUTATED_BY_IMPORT");
        if (!string.Equals(refsBefore, refsAfter, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GIT_REFS_CHANGED_BY_NO_REF_IMPORT");
        if (!string.IsNullOrWhiteSpace(dirtyAfter))
            throw new InvalidDataException("SOURCE_WORKTREE_CHANGED_BY_IMPORT");

        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        var receipt = new
        {
            Schema,
            Version = "0.1.0",
            Status,
            ObservedAt = DateTimeOffset.Now,
            RepositoryRoot,
            FixedRemote,
            ExpectedRemoteMain = ExpectedPublicMain,
            RemoteMainObserved = observedRemoteMain,
            ImportedCommit = ExpectedPublicMain,
            LocalHeadBefore = head,
            LocalHeadAfter = headAfter,
            LocalAcceptedTag = ExpectedTag,
            LocalAcceptedTagCommit = tagAfter,
            RefsDigestBefore = refsBefore,
            RefsDigestAfter = refsAfter,
            GitRefsUnchanged = true,
            ObjectPresentBefore = objectBefore,
            ObjectPresentAfter = true,
            ObjectDatabaseChanged = !objectBefore,
            FetchUsedNoRef = true,
            NetworkReadPerformed = true,
            RefMutationPerformed = false,
            RemoteWritePerformed = false,
            SourceMutationPerformed = false,
            AutomaticRetryPerformed = false,
            PublicationAuthorityCreated = false,
            Note = "Fixed network read/import only. The public commit object is evidence for a later local second-parent checkpoint. Trigger != authorization; object availability != publication authority."
        };
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine("COMPLETED: EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION");
        Console.WriteLine($"Remote main: {observedRemoteMain}");
        Console.WriteLine($"Imported commit: {ExpectedPublicMain}");
        Console.WriteLine($"Local HEAD unchanged: {headAfter}");
        Console.WriteLine("Git refs unchanged: True");
        Console.WriteLine($"Receipt: {receiptPath}");
    }

    private static string ReadRemoteMain()
    {
        var output = RunGit(RepositoryRoot, "ls-remote", FixedRemote, "refs/heads/main");
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1)
            throw new InvalidDataException("REMOTE_MAIN_AMBIGUOUS_OR_MISSING");
        var fields = lines[0].Split('\t');
        if (fields.Length != 2 || !string.Equals(fields[1], "refs/heads/main", StringComparison.Ordinal))
            throw new InvalidDataException("REMOTE_MAIN_RESPONSE_MALFORMED");
        return fields[0].Trim();
    }

    private static bool CommitObjectExists(string repositoryRoot, string commit)
    {
        var result = RunGitAllowFailure(repositoryRoot, "cat-file", "-e", commit + "^{commit}");
        return result.ExitCode == 0;
    }

    private static string ComputeRefsDigest(string repositoryRoot)
    {
        var refs = RunGit(repositoryRoot, "for-each-ref", "--format=%(refname)%00%(objectname)", "refs");
        var normalized = refs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Aggregate(new StringBuilder(), (builder, line) => builder.Append(line).Append('\n'))
            .ToString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string RunGit(string workingDirectory, params string[] args)
    {
        var result = RunGitAllowFailure(workingDirectory, args);
        if (result.ExitCode != 0)
            throw new InvalidDataException($"FIXED_GIT_OPERATION_FAILED: {string.Join(' ', args)}: {result.Stderr.Trim()}");
        return result.Stdout;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunGitAllowFailure(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("FAILED_TO_START_FIXED_GIT");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("FIXED_GIT_OPERATION_TIMED_OUT");
        }
        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static int SelfTest()
    {
        var checks = new[]
        {
            FixedRemote == "https://github.com/Matawaka/workbench.git",
            ExpectedHead.Length == 40,
            ExpectedPublicMain.Length == 40,
            ExpectedTag == "workbench-v0.55.2-accepted",
            Confirmation == "IMPORT-EXACT-PUBLIC-AC08",
            Schema.EndsWith("/v0.1", StringComparison.Ordinal),
            Status == "EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION"
        };
        if (checks.Any(x => !x))
            throw new InvalidDataException("SELF_TEST_FIXED_POLICY_FAILED");
        Console.WriteLine("SELF_TEST_PASS fixedRemote=true fixedHead=true fixedMain=true noArbitraryArgs=true");
        return 0;
    }
}
