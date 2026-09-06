using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchConvergenceEvidenceV0552(
    string RecoveryReceiptPath,
    string RecoveryReceiptSha256,
    string PublicImportReceiptPath,
    string PublicImportReceiptSha256,
    string LocalAcceptedCommit,
    string LocalAcceptedTag,
    string ImportedPublicCommit,
    string ImportedPublicFirstParent,
    string RefsDigest,
    bool GitObjectDatabaseStateChanged,
    bool FixedGitNetworkReadPerformed);

public static class WorkbenchConvergenceEvidenceVerifierV0552
{
    public const string LocalAcceptedCommit = "02d81b8559bc7c9676949be0557d20ecb50a9890";
    public const string LocalAcceptedTag = "workbench-v0.55-accepted";
    public const string PublicMainCommit = "6111fdf82a9e8947a7722e9b603c1e9268a19105";
    public const string PublicMainFirstParent = "65b0b49a513a6b782760a7626d6b768bf7bb7f91";
    public const string FixedRemoteUrl = "https://github.com/Matawaka/workbench.git";
    public const string RecoveryRequestId = "recover-v0551-title-harness-failure-50b6af3831c84032bbcd51d5b03dc7eb-v2";
    public const string RecoveryReceiptSha256 = "eb292179147a469d8ad9a7ea83d844e8115f394f773e94a91ca360430e784d58";
    public const string PublicImportRequestId = "v0552-import-public-main-6111fdf82a9e8947a7722e9b603c1e9268a19105-v1";
    public const string PublicImportReceiptSha256 = "429d769a9caca8537fccb6778249e67969729de94cda4e7616d47598210a38a5";
    public const string PublicImportReceiptFileName = "public-main-6111fdf82a9e8947a7722e9b603c1e9268a19105.json";

    public static WorkbenchConvergenceEvidenceV0552 FindExact(string workspaceRoot)
    {
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var recovery = FindRecoveryReceipt(repo);
        var importPath = Path.Combine(repo, "artifacts", "convergence-v0552", PublicImportReceiptFileName);
        Require(File.Exists(importPath), "public-frontier import receipt missing");
        Require(HashFile(importPath).Equals(PublicImportReceiptSha256, StringComparison.OrdinalIgnoreCase), "public-frontier import receipt SHA-256");
        var import = ValidatePublicImportReceipt(importPath);
        ValidateImportedCommitObject(repo);
        return new WorkbenchConvergenceEvidenceV0552(
            recovery.Path, recovery.Sha256, importPath, PublicImportReceiptSha256,
            LocalAcceptedCommit, LocalAcceptedTag, PublicMainCommit, PublicMainFirstParent,
            import.RefsDigest, import.GitObjectDatabaseStateChanged, import.FixedGitNetworkReadPerformed);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("v0552-local-predecessor", LocalAcceptedCommit.Length == 40, LocalAcceptedCommit, "exact accepted local v0.55"),
        ("v0552-local-tag", LocalAcceptedTag == "workbench-v0.55-accepted", LocalAcceptedTag, "workbench-v0.55-accepted"),
        ("v0552-public-parent", PublicMainCommit.Length == 40 && PublicMainFirstParent.Length == 40, $"{PublicMainCommit}/{PublicMainFirstParent}", "exact public #82 merge / prior public frontier"),
        ("v0552-recovery-receipt", RecoveryReceiptSha256.Length == 64, RecoveryReceiptSha256, "exact canonical recovery receipt"),
        ("v0552-import-receipt", PublicImportReceiptSha256.Length == 64, PublicImportReceiptSha256, "exact canonical no-ref import receipt"),
        ("v0552-distinct-authority-classes", true, "ProvenanceBoundRuntimeExecutionV055Service != BoundedLocalModelInvocationV055Service", "no semantic reinterpretation")
    };

    private static (string Path, string Sha256) FindRecoveryReceipt(string repo)
    {
        var dir = Path.Combine(repo, "artifacts", "recovery-v0551", "receipts");
        Require(Directory.Exists(dir), "recovery receipt directory missing");
        foreach (var path in Directory.GetFiles(dir, "recovery-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (!HashFile(path).Equals(RecoveryReceiptSha256, StringComparison.OrdinalIgnoreCase)) continue;
            ValidateRecoveryReceipt(path);
            return (path, RecoveryReceiptSha256);
        }
        throw new InvalidDataException("Exact v0.55.1 recovery receipt was not found.");
    }

    private static void ValidateRecoveryReceipt(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == "matawaka.workbench-v0551-failed-firstboot-recovery-receipt/v0.1", "recovery schema");
        Require(GetString(o, "RequestId") == RecoveryRequestId, "recovery request id");
        Require(SameSha(GetString(o, "HeadBefore"), LocalAcceptedCommit) && SameSha(GetString(o, "HeadAfter"), LocalAcceptedCommit), "recovery HEAD binding");
        Require(GetString(o, "AcceptedTag") == LocalAcceptedTag && SameSha(GetString(o, "AcceptedTagCommitAfter"), LocalAcceptedCommit), "recovery tag binding");
        Require(GetString(o, "Status") == "EXACT_ACCEPTED_V055_SOURCE_RESTORED", "recovery terminal status");
        Require(GetBool(o, "SourceMutationPerformed") && GetBool(o, "ExactAcceptedSourceRestored") && GetBool(o, "WorkingTreeCleanAfterRecovery"), "recovery completion flags");
        Require(!GetBool(o, "GitCommitPerformed") && !GetBool(o, "GitTagMutationPerformed") && !GetBool(o, "GitRefMutationPerformed") &&
                !GetBool(o, "GitRemoteMutationPerformed") && !GetBool(o, "NetworkAccessPerformed") && !GetBool(o, "ProcessLaunchPerformed") &&
                !GetBool(o, "ProcessTerminationPerformed") && !GetBool(o, "PublicationPerformed") && !GetBool(o, "AutomaticRetryPerformed") &&
                !GetBool(o, "HistoricalReceiptReinterpreted"), "recovery non-effects");
    }

    private sealed record ImportObservation(string RefsDigest, bool GitObjectDatabaseStateChanged, bool FixedGitNetworkReadPerformed);

    private static ImportObservation ValidatePublicImportReceipt(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == "matawaka.workbench-v0552-public-frontier-import-receipt/v0.1", "import schema");
        Require(GetString(o, "RequestId") == PublicImportRequestId, "import request id");
        Require(SameSha(GetString(o, "LocalHeadBefore"), LocalAcceptedCommit) && SameSha(GetString(o, "LocalHeadAfter"), LocalAcceptedCommit), "import local HEAD binding");
        Require(GetString(o, "LocalTag") == LocalAcceptedTag && SameSha(GetString(o, "LocalTagCommitAfter"), LocalAcceptedCommit), "import local tag binding");
        Require(GetString(o, "FixedRemoteUrl") == FixedRemoteUrl, "fixed remote binding");
        Require(SameSha(GetString(o, "RemoteMainObserved"), PublicMainCommit) && SameSha(GetString(o, "ImportedPublicCommit"), PublicMainCommit), "import public main binding");
        Require(SameSha(GetString(o, "ImportedPublicFirstParent"), PublicMainFirstParent), "import public first-parent binding");
        Require(!GetBool(o, "PublicCommitAlreadyPresentBefore") && GetBool(o, "PublicCommitPresentAfter"), "import object transition");
        var refsBefore = GetString(o, "RefsDigestBefore");
        var refsAfter = GetString(o, "RefsDigestAfter");
        Require(refsBefore.Length == 64 && refsBefore.Equals(refsAfter, StringComparison.OrdinalIgnoreCase), "import refs unchanged");
        Require(GetString(o, "ObjectDatabaseDigestBefore").Length == 64 && GetString(o, "ObjectDatabaseDigestAfter").Length == 64 &&
                !GetString(o, "ObjectDatabaseDigestBefore").Equals(GetString(o, "ObjectDatabaseDigestAfter"), StringComparison.OrdinalIgnoreCase), "import object database mutation truthfulness");
        Require(GetBool(o, "GitObjectDatabaseStateChanged") && GetBool(o, "FixedGitNetworkReadPerformed") && GetBool(o, "NetworkAccessPerformed"), "import network/object flags");
        Require(!GetBool(o, "RemoteWritePerformed") && !GetBool(o, "GitRefMutationPerformed") && !GetBool(o, "GitTagMutationPerformed") &&
                !GetBool(o, "SourceMutationPerformed") && !GetBool(o, "WorkbenchRuntimeExecutionPerformed") && !GetBool(o, "ModelInvocationPerformed") &&
                !GetBool(o, "ArtifactAcquisitionPerformed") && !GetBool(o, "RuntimeMaterializationPerformed") && !GetBool(o, "AutomaticRetryPerformed"), "import non-effects");
        Require(GetString(o, "Status") == "EXACT_PUBLIC_MAIN_OBJECT_IMPORTED_NO_REF_MUTATION", "import terminal status");
        return new ImportObservation(refsAfter, true, true);
    }

    private static void ValidateImportedCommitObject(string repo)
    {
        RunGit(repo, "cat-file", "-e", PublicMainCommit + "^{commit}");
        var line = RunGit(repo, "rev-list", "--parents", "-n", "1", PublicMainCommit).Trim();
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Require(parts.Length >= 2 && SameSha(parts[0], PublicMainCommit) && SameSha(parts[1], PublicMainFirstParent), "imported public commit ancestry");
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string RunGit(string root, params string[] args)
    {
        var allowed = (args.Length == 3 && args[0] == "cat-file" && args[1] == "-e" && args[2] == PublicMainCommit + "^{commit}") ||
                      (args.Length == 5 && args[0] == "rev-list" && args[1] == "--parents" && args[2] == "-n" && args[3] == "1" && args[4] == PublicMainCommit);
        if (!allowed) throw new InvalidOperationException("Unexpected Git command in v0.55.2 convergence evidence verifier.");
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidDataException("Unable to start fixed read-only Git observation.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(20_000)) { try { p.Kill(entireProcessTree: true); } catch { } throw new InvalidDataException("Fixed read-only Git observation timed out."); }
        if (p.ExitCode != 0) throw new InvalidDataException("Fixed read-only Git observation failed: " + stderr.Trim());
        return stdout;
    }

    private static string HashFile(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
    private static string GetString(JsonElement o, string name) => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool GetBool(JsonElement o, string name) => o.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False && p.GetBoolean();
    private static bool SameSha(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string label) { if (!condition) throw new InvalidDataException("V0552_CONVERGENCE_EVIDENCE_REFUSED: " + label); }
}
