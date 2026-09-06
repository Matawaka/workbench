using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.V0552PublicImport;

internal sealed record ImportRequest(
    string Schema,
    string RequestId,
    string RepositoryRoot,
    string ExpectedLocalHead,
    string ExpectedLocalTag,
    string FixedRemoteUrl,
    string ExpectedPublicMain,
    string ExpectedPublicFirstParent);

internal sealed record ImportPreview(
    string Schema,
    string RequestId,
    string RepositoryRoot,
    string LocalHead,
    string LocalTag,
    string FixedRemoteUrl,
    string ExpectedPublicMain,
    string ExpectedPublicFirstParent,
    bool PublicCommitAlreadyPresentLocally,
    string RefsDigestBefore,
    string ObjectDatabaseDigestBefore,
    bool NetworkAccessPerformed,
    bool GitRefMutationPerformed,
    bool SourceMutationPerformed,
    string Status);

internal sealed record ImportReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string RepositoryRoot,
    string LocalHeadBefore,
    string LocalHeadAfter,
    string LocalTag,
    string LocalTagCommitAfter,
    string FixedRemoteUrl,
    string RemoteMainObserved,
    string ImportedPublicCommit,
    string ImportedPublicFirstParent,
    bool PublicCommitAlreadyPresentBefore,
    bool PublicCommitPresentAfter,
    string RefsDigestBefore,
    string RefsDigestAfter,
    string ObjectDatabaseDigestBefore,
    string ObjectDatabaseDigestAfter,
    bool GitObjectDatabaseStateChanged,
    bool FixedGitNetworkReadPerformed,
    bool NetworkAccessPerformed,
    bool RemoteWritePerformed,
    bool GitRefMutationPerformed,
    bool GitTagMutationPerformed,
    bool SourceMutationPerformed,
    bool WorkbenchRuntimeExecutionPerformed,
    bool ModelInvocationPerformed,
    bool ArtifactAcquisitionPerformed,
    bool RuntimeMaterializationPerformed,
    bool AutomaticRetryPerformed,
    string Status,
    string Note);

internal static class Program
{
    private const string RequestSchema = "matawaka.workbench-v0552-public-frontier-import-request/v0.1";
    private const string PreviewSchema = "matawaka.workbench-v0552-public-frontier-import-preview/v0.1";
    private const string ReceiptSchema = "matawaka.workbench-v0552-public-frontier-import-receipt/v0.1";
    private const string Version = "0.1.0";
    private const string RequestId = "v0552-import-public-main-6111fdf82a9e8947a7722e9b603c1e9268a19105-v1";
    private const string RepositoryRoot = @"K:\Matawaka\Workbench";
    private const string LocalHead = "02d81b8559bc7c9676949be0557d20ecb50a9890";
    private const string LocalTag = "workbench-v0.55-accepted";
    private const string RemoteUrl = "https://github.com/Matawaka/workbench.git";
    private const string PublicMain = "6111fdf82a9e8947a7722e9b603c1e9268a19105";
    private const string PublicFirstParent = "65b0b49a513a6b782760a7626d6b768bf7bb7f91";
    private const string Confirmation = "IMPORT-EXACT-PUBLIC-6111";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            var requestPath = args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "public-import-request.json") : Path.GetFullPath(args[0]);
            var request = ParseRequest(File.ReadAllText(requestPath, Encoding.UTF8));
            var preview = Preview(request);

            Console.WriteLine("Matawaka Workbench v0.55.2 exact public-frontier import");
            Console.WriteLine();
            Console.WriteLine($"Status: {preview.Status}");
            Console.WriteLine($"Repository: {preview.RepositoryRoot}");
            Console.WriteLine($"Local HEAD/tag: {preview.LocalHead} / {preview.LocalTag}");
            Console.WriteLine($"Fixed remote: {preview.FixedRemoteUrl}");
            Console.WriteLine($"Expected public main: {preview.ExpectedPublicMain}");
            Console.WriteLine($"Expected public first parent: {preview.ExpectedPublicFirstParent}");
            Console.WriteLine($"Public commit already local: {preview.PublicCommitAlreadyPresentLocally}");
            Console.WriteLine();
            Console.WriteLine("Preview is local/evidence-only: no network, source, ref, tag, runtime or publication effect has occurred.");
            Console.WriteLine("After confirmation the tool may perform only fixed Git network reads and import the exact public commit objects without creating/updating any Git ref.");
            Console.WriteLine();
            Console.Write($"Type {Confirmation} exactly to authorize this one import: ");
            var typed = Console.ReadLine();
            if (!string.Equals(typed, Confirmation, StringComparison.Ordinal))
            {
                Console.WriteLine("CANCELLED_NO_EFFECT");
                Pause();
                return 2;
            }

            var receipt = Execute(request, preview);
            Console.WriteLine();
            Console.WriteLine($"COMPLETED: {receipt.Status}");
            Console.WriteLine($"Remote main: {receipt.RemoteMainObserved}");
            Console.WriteLine($"Imported commit/parent: {receipt.ImportedPublicCommit} / {receipt.ImportedPublicFirstParent}");
            Console.WriteLine($"Local HEAD unchanged: {receipt.LocalHeadAfter}");
            Console.WriteLine($"Git refs unchanged: {!receipt.GitRefMutationPerformed}");
            Console.WriteLine($"Receipt: {ReceiptPath(receipt.RepositoryRoot)}");
            Pause();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PUBLIC_IMPORT_REFUSED_OR_FAILED: " + ex.Message);
            Pause();
            return 1;
        }
    }

    private static ImportPreview Preview(ImportRequest request)
    {
        RequireRequest(request);
        var root = ExactRoot(request.RepositoryRoot);
        var head = ReadOnlyGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();
        if (head != LocalHead) throw Refused("LOCAL_HEAD_MISMATCH", $"expected={LocalHead}; observed={head}");
        var tag = ReadOnlyGit(root, "rev-list", "-n", "1", LocalTag).Trim().ToLowerInvariant();
        if (tag != LocalHead) throw Refused("LOCAL_TAG_MISMATCH", $"{LocalTag} -> {tag}");
        var status = ReadOnlyGit(root, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(status)) throw Refused("LOCAL_WORKTREE_NOT_CLEAN", status.Trim());
        var refs = HashText(ReadOnlyGit(root, "show-ref", "--head", "--dereference"));
        var objects = HashObjectDatabase(root);
        var already = CommitExists(root, PublicMain);
        if (already)
        {
            var parent = ReadOnlyGit(root, "rev-parse", $"{PublicMain}^1").Trim().ToLowerInvariant();
            if (parent != PublicFirstParent) throw Refused("LOCAL_PUBLIC_OBJECT_PARENT_MISMATCH", parent);
        }
        return new ImportPreview(
            PreviewSchema, RequestId, root, head, LocalTag, RemoteUrl, PublicMain, PublicFirstParent,
            already, refs, objects, false, false, false, "READY_FOR_EXPLICIT_FIXED_PUBLIC_OBJECT_IMPORT");
    }

    private static ImportReceipt Execute(ImportRequest request, ImportPreview preview)
    {
        var fresh = Preview(request);
        if (fresh.LocalHead != preview.LocalHead || fresh.RefsDigestBefore != preview.RefsDigestBefore ||
            fresh.ObjectDatabaseDigestBefore != preview.ObjectDatabaseDigestBefore)
            throw Refused("PREVIEW_STALE", "local Git evidence changed after Preview");

        var root = fresh.RepositoryRoot;
        var remoteMain = RunGit(root, allowFailure: false,
            "ls-remote", RemoteUrl, "refs/heads/main").Stdout.Trim();
        var observed = ParseSingleLsRemote(remoteMain, "refs/heads/main");
        if (observed != PublicMain) throw Refused("REMOTE_MAIN_DRIFT", $"expected={PublicMain}; observed={observed}");

        // URL + exact public ref only. --no-write-fetch-head prevents FETCH_HEAD mutation;
        // no refspec destination is supplied, so no local branch/tag/ref is created or moved.
        _ = RunGit(root, allowFailure: false,
            "fetch", "--no-write-fetch-head", "--no-tags", "--filter=blob:none", RemoteUrl, "refs/heads/main");

        if (!CommitExists(root, PublicMain)) throw Refused("PUBLIC_COMMIT_NOT_IMPORTED", PublicMain);
        var parent = ReadOnlyGit(root, "rev-parse", $"{PublicMain}^1").Trim().ToLowerInvariant();
        if (parent != PublicFirstParent) throw Refused("PUBLIC_FIRST_PARENT_MISMATCH", $"expected={PublicFirstParent}; observed={parent}");

        var headAfter = ReadOnlyGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();
        var tagAfter = ReadOnlyGit(root, "rev-list", "-n", "1", LocalTag).Trim().ToLowerInvariant();
        var statusAfter = ReadOnlyGit(root, "status", "--porcelain=v1", "--untracked-files=all");
        var refsAfter = HashText(ReadOnlyGit(root, "show-ref", "--head", "--dereference"));
        var objectsAfter = HashObjectDatabase(root);
        if (headAfter != LocalHead || tagAfter != LocalHead || !string.IsNullOrWhiteSpace(statusAfter))
            throw Refused("LOCAL_FRONTIER_DRIFT", $"head={headAfter}; tag={tagAfter}; status={statusAfter.Trim()}");
        if (refsAfter != fresh.RefsDigestBefore)
            throw Refused("GIT_REF_MUTATION_DETECTED", $"before={fresh.RefsDigestBefore}; after={refsAfter}");

        var receipt = new ImportReceipt(
            ReceiptSchema, Version, DateTimeOffset.Now, RequestId, root,
            fresh.LocalHead, headAfter, LocalTag, tagAfter, RemoteUrl, observed,
            PublicMain, parent, fresh.PublicCommitAlreadyPresentLocally, true,
            fresh.RefsDigestBefore, refsAfter, fresh.ObjectDatabaseDigestBefore, objectsAfter,
            fresh.ObjectDatabaseDigestBefore != objectsAfter,
            true, true, false, false, false, false, false, false, false, false, false,
            "EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION",
            "After explicit operator confirmation, fixed Git performed only ls-remote + no-ref fetch against the fixed Matawaka/workbench remote. The exact public main commit object is locally available for a later two-parent convergence checkpoint. Local HEAD/tag/source and all Git refs remain unchanged; no remote write or Workbench runtime/model effect occurred.");

        var path = ReceiptPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8);
            if (!string.Equals(existing, JsonSerializer.Serialize(receipt, JsonOptions), StringComparison.Ordinal))
                throw Refused("RECEIPT_ALREADY_EXISTS", path);
            return receipt;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false));
        return receipt;
    }

    private static ImportRequest ParseRequest(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw Refused("REQUEST_NOT_OBJECT", "root");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "Schema", "RequestId", "RepositoryRoot", "ExpectedLocalHead", "ExpectedLocalTag",
            "FixedRemoteUrl", "ExpectedPublicMain", "ExpectedPublicFirstParent"
        };
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != expected.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(n => !expected.Contains(n)))
            throw Refused("REQUEST_PROPERTY_SET_REFUSED", string.Join(',', names));
        return JsonSerializer.Deserialize<ImportRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = false })
               ?? throw Refused("REQUEST_DESERIALIZATION_FAILED", "null");
    }

    private static void RequireRequest(ImportRequest r)
    {
        if (r.Schema != RequestSchema || r.RequestId != RequestId ||
            !Path.GetFullPath(r.RepositoryRoot).Equals(Path.GetFullPath(RepositoryRoot), StringComparison.OrdinalIgnoreCase) ||
            !r.ExpectedLocalHead.Equals(LocalHead, StringComparison.OrdinalIgnoreCase) || r.ExpectedLocalTag != LocalTag ||
            r.FixedRemoteUrl != RemoteUrl || !r.ExpectedPublicMain.Equals(PublicMain, StringComparison.OrdinalIgnoreCase) ||
            !r.ExpectedPublicFirstParent.Equals(PublicFirstParent, StringComparison.OrdinalIgnoreCase))
            throw Refused("REQUEST_POLICY_MISMATCH", "request differs from fixed v0.55.2 convergence-import policy");
    }

    private static string ReceiptPath(string root)
        => Path.Combine(root, "artifacts", "convergence-v0552", $"public-main-{PublicMain}.json");

    private static string ExactRoot(string path)
    {
        var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw Refused("REPOSITORY_MISSING", root);
        return root;
    }

    private static bool CommitExists(string root, string sha)
        => RunGit(root, allowFailure: true, "cat-file", "-e", $"{sha}^{{commit}}").ExitCode == 0;

    private static string ParseSingleLsRemote(string text, string expectedRef)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1) throw Refused("REMOTE_REF_CARDINALITY", lines.Length.ToString());
        var parts = lines[0].Split('\t');
        if (parts.Length != 2 || parts[1] != expectedRef || parts[0].Length != 40 || parts[0].Any(c => !Uri.IsHexDigit(c)))
            throw Refused("REMOTE_REF_FORMAT", lines[0]);
        return parts[0].ToLowerInvariant();
    }

    private static string HashObjectDatabase(string root)
    {
        var objects = Path.Combine(root, ".git", "objects");
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (!Directory.Exists(objects)) return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
        foreach (var file in Directory.GetFiles(objects, "*", SearchOption.AllDirectories)
                     .OrderBy(p => Path.GetRelativePath(objects, p).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(objects, file).Replace('\\', '/');
            var info = new FileInfo(file);
            var fileHash = HashFile(file);
            var line = Encoding.UTF8.GetBytes($"{rel}\0{info.Length}\0{fileHash}\n");
            aggregate.AppendData(line);
        }
        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ReadOnlyGit(string root, params string[] args)
    {
        var allowed =
            (args.SequenceEqual(new[] { "rev-parse", "HEAD" })) ||
            (args.Length == 4 && args[0] == "rev-list" && args[1] == "-n" && args[2] == "1" && args[3] == LocalTag) ||
            (args.SequenceEqual(new[] { "status", "--porcelain=v1", "--untracked-files=all" })) ||
            (args.SequenceEqual(new[] { "show-ref", "--head", "--dereference" })) ||
            (args.Length == 2 && args[0] == "rev-parse" && args[1] == $"{PublicMain}^1");
        if (!allowed) throw Refused("UNAPPROVED_LOCAL_GIT_SHAPE", string.Join(' ', args));
        var result = RunGit(root, allowFailure: false, args);
        return result.Stdout;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunGit(string root, bool allowFailure, params string[] args)
    {
        var networkAllowed =
            (args.Length == 3 && args[0] == "ls-remote" && args[1] == RemoteUrl && args[2] == "refs/heads/main") ||
            (args.Length == 6 && args[0] == "fetch" && args[1] == "--no-write-fetch-head" && args[2] == "--no-tags" &&
             args[3] == "--filter=blob:none" && args[4] == RemoteUrl && args[5] == "refs/heads/main");
        var localAllowed = args.Length == 3 && args[0] == "cat-file" && args[1] == "-e" && args[2] == $"{PublicMain}^{{commit}}";
        if (!networkAllowed && !localAllowed && !(args.Length > 0 && args[0] is "rev-parse" or "rev-list" or "status" or "show-ref"))
            throw Refused("UNAPPROVED_GIT_SHAPE", string.Join(' ', args));

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw Refused("GIT_START_FAILED", args[0]);
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(60_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw Refused("GIT_TIMEOUT", args[0]);
        }
        if (!allowFailure && p.ExitCode != 0) throw Refused("GIT_FAILED", $"{string.Join(' ', args)} :: {stderr.Trim()}");
        return (p.ExitCode, stdout, stderr);
    }

    private static string HashFile(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static string HashText(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static InvalidDataException Refused(string c, string d) => new($"{c}: {d}");

    private static void Pause()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            _ = Console.ReadLine();
        }
    }

    private static int SelfTest()
    {
        if (PublicMain.Length != 40 || PublicFirstParent.Length != 40 || LocalHead.Length != 40) throw new Exception("fixed commit shape");
        if (RemoteUrl != "https://github.com/Matawaka/workbench.git") throw new Exception("fixed remote");
        if (RequestId.Contains("v0551", StringComparison.Ordinal)) throw new Exception("failed successor identity reused");
        var req = new ImportRequest(RequestSchema, RequestId, RepositoryRoot, LocalHead, LocalTag, RemoteUrl, PublicMain, PublicFirstParent);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var parsed = ParseRequest(json);
        RequireRequest(parsed);
        var unknown = json.TrimEnd('}', ' ', '\r', '\n') + ",\"Unknown\":true}";
        try { _ = ParseRequest(unknown); throw new Exception("unknown property accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("V0552_PUBLIC_IMPORT_SELF_TEST_PASS closedRequest=true fixedRemote=true noRefDestination=true noForce=true");
        return 0;
    }
}
