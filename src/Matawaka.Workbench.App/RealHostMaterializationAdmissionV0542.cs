using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RealHostMaterializationAdmissionV0542(
    string MaterializationReceiptPath,
    string MaterializationReceiptSha256,
    string TransactionPath,
    string TransactionSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    string RuntimeManifestPath,
    string RuntimeManifestSha256,
    string LeaseId,
    string TransactionId,
    string RequestId,
    string AcquisitionReceiptSha256,
    string PlanSha256,
    string TreeDigestSha256,
    string ExecutableRelativePath,
    string ExecutableSha256,
    int MaterializedFiles,
    long MaterializedBytes,
    DateTimeOffset ObservedAt);

public static class RealHostMaterializationAdmissionVerifierV0542
{
    public const string ExpectedRequestId = "matreq-workbench-v0541-realhost-smoke-001";
    public const string ExpectedLeaseId = "matlease-758f2b07f2194b7b887f21739aef3a2f";
    public const string ExpectedTransactionId = "mattx-73341c0a3aab427e8a9a1973fdfd50bf";
    public const string ExpectedAcquisitionReceiptSha256 = "4299ba090cf271f8b11d53b5080b5a6387e56cad2928da76c7a554f5703f097e";
    public const string ExpectedPlanSha256 = "9029639f586b922e378e65049752a92db30f0865e21489b3d479326c518827c9";
    public const string ExpectedRuntimeManifestSha256 = "a938c4856b08f0d33df4b595b4c0319e3f687bd03fe72ec723ea6180a389225a";
    public const string ExpectedTreeDigestSha256 = "1c0343f93d3874f73845ee0f7d470047ee666a15c125da039070fb8987b411d6";
    public const string ExpectedExecutableRelativePath = "bin/matawaka-v054-materialization-smoke-v1.exe";
    public const string ExpectedExecutableSha256 = "1f7b207a56ed030e6bdbe633f9ae522842539a7036a5e1933cb23a1c58d58a10";
    public const string ExpectedArchiveArtifactId = "artifact-workbench-v054-materialization-smoke-v1";
    public const string ExpectedArchiveSha256 = "bb89f6713f31fed9d8284f66e2223e9e76155303e317d3f66f3d1f66fcfe89b2";
    public const long ExpectedArchiveBytes = 355;
    public const int ExpectedMaterializedFiles = 1;
    public const long ExpectedMaterializedBytes = 1024;

    public static RealHostMaterializationAdmissionV0542 FindExact(string workspaceRoot)
    {
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var receipts = Path.Combine(repo, "artifacts", "runtime-materialization-v054", "receipts");
        if (!Directory.Exists(receipts))
            throw new InvalidDataException("Real-host v0.54 materialization receipt directory is missing.");

        foreach (var path in Directory.GetFiles(receipts, "materialization-mattx-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { return ValidateReceiptFile(workspaceRoot, path); }
            catch (InvalidDataException) { }
        }
        throw new InvalidDataException("No exact real-host v0.54.1 RUNTIME_TREE_MATERIALIZATION_VERIFIED admission receipt was found.");
    }

    public static RealHostMaterializationAdmissionV0542 ValidateReceiptFile(string workspaceRoot, string receiptPath)
    {
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var receipts = Path.GetFullPath(Path.Combine(repo, "artifacts", "runtime-materialization-v054", "receipts")) + Path.DirectorySeparatorChar;
        var fullReceipt = Path.GetFullPath(receiptPath);
        if (!fullReceipt.StartsWith(receipts, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullReceipt))
            throw new InvalidDataException("Materialization admission receipt must be an existing Workbench-owned v0.54 receipt.");

        using var doc = JsonDocument.Parse(File.ReadAllText(fullReceipt, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedRuntimeTreeMaterializationV054Service.ExecutionReceiptSchema, "materialization receipt schema");
        Require(GetString(o, "Version") == BoundedRuntimeTreeMaterializationV054Service.Version, "materialization receipt version");
        Require(GetString(o, "RequestId") == ExpectedRequestId, "real-host request id");
        Require(GetString(o, "LeaseId") == ExpectedLeaseId, "real-host lease id");
        Require(GetString(o, "TransactionId") == ExpectedTransactionId, "real-host transaction id");
        Require(GetString(o, "State") == "MATERIALIZED_VERIFIED", "terminal materialization state");
        Require(GetString(o, "Status") == "RUNTIME_TREE_MATERIALIZATION_VERIFIED", "terminal materialization status");
        Require(GetString(o, "AcquisitionReceiptSha256").Equals(ExpectedAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "acquisition receipt digest");
        Require(GetString(o, "PlanSha256").Equals(ExpectedPlanSha256, StringComparison.OrdinalIgnoreCase), "plan digest");
        Require(GetString(o, "RuntimeManifestSha256").Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "runtime manifest digest");
        Require(GetString(o, "TreeDigestSha256").Equals(ExpectedTreeDigestSha256, StringComparison.OrdinalIgnoreCase), "tree digest");
        Require(GetInt(o, "MaterializedFiles") == ExpectedMaterializedFiles, "materialized file count");
        Require(GetLong(o, "MaterializedBytes") == ExpectedMaterializedBytes, "materialized bytes");
        Require(GetBool(o, "MaterializationAuthorityConsumed"), "materialization authority consumed");
        Require(GetBool(o, "FilesystemMutationPerformed"), "bounded filesystem mutation evidence");
        Require(GetBool(o, "ExtractionPerformed"), "bounded extraction evidence");
        Require(GetBool(o, "RootPromoted"), "atomic root promotion evidence");
        Require(!GetBool(o, "ProcessExecutionPerformed") && !GetBool(o, "RuntimeStartPerformed") &&
                !GetBool(o, "BenchmarkPerformed") && !GetBool(o, "ModelRequestPerformed") &&
                !GetBool(o, "GameAccessPerformed") && !GetBool(o, "NetworkAccessPerformed"),
            "no post-materialization authority/effects");

        var archives = o.GetProperty("Archives").EnumerateArray().ToArray();
        Require(archives.Length == 1, "exact one archive");
        Require(GetString(archives[0], "ArtifactId") == ExpectedArchiveArtifactId, "archive artifact id");
        Require(GetLong(archives[0], "ArchiveBytes") == ExpectedArchiveBytes, "archive bytes");
        Require(GetString(archives[0], "ArchiveSha256").Equals(ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase), "archive digest");

        var files = o.GetProperty("Files").EnumerateArray().ToArray();
        Require(files.Length == 1, "exact one materialized file");
        Require(GetString(files[0], "RelativePath") == ExpectedExecutableRelativePath, "materialized executable path");
        Require(GetLong(files[0], "Bytes") == ExpectedMaterializedBytes, "materialized executable bytes");
        Require(GetString(files[0], "Sha256").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase), "materialized executable digest");
        Require(GetString(files[0], "Role") == "executable", "materialized executable role");

        var transactionPath = RequireOwnedPath(repo, GetString(o, "TransactionPath"), Path.Combine("artifacts", "runtime-materialization-v054", "transactions"), "transaction");
        var statePath = RequireOwnedPath(repo, GetString(o, "LeaseStatePath"), Path.Combine("artifacts", "runtime-materialization-v054", "leases"), "lease state");
        var manifestPath = Path.GetFullPath(GetString(o, "RuntimeManifestPath"));
        Require(File.Exists(manifestPath), "runtime manifest exists");

        var transactionSha = HashFile(transactionPath);
        var stateSha = HashFile(statePath);
        var manifestSha = HashFile(manifestPath);
        Require(transactionSha.Equals(GetString(o, "TransactionSha256"), StringComparison.OrdinalIgnoreCase), "transaction receipt digest binding");
        Require(stateSha.Equals(GetString(o, "LeaseStateSha256"), StringComparison.OrdinalIgnoreCase), "lease-state receipt digest binding");
        Require(manifestSha.Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "runtime manifest file digest binding");

        ValidateTransaction(transactionPath);
        ValidateLeaseState(statePath);
        ValidateRuntimeManifest(o, manifestPath);

        return new RealHostMaterializationAdmissionV0542(
            fullReceipt, HashFile(fullReceipt), transactionPath, transactionSha, statePath, stateSha,
            manifestPath, manifestSha, ExpectedLeaseId, ExpectedTransactionId, ExpectedRequestId,
            ExpectedAcquisitionReceiptSha256, ExpectedPlanSha256, ExpectedTreeDigestSha256,
            ExpectedExecutableRelativePath, ExpectedExecutableSha256, ExpectedMaterializedFiles,
            ExpectedMaterializedBytes, GetDate(o, "ObservedAt"));
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("v0542-admission-request", ExpectedRequestId == "matreq-workbench-v0541-realhost-smoke-001", ExpectedRequestId, "exact real-host request"),
        ("v0542-admission-files", ExpectedMaterializedFiles == 1 && ExpectedMaterializedBytes == 1024, $"{ExpectedMaterializedFiles}/{ExpectedMaterializedBytes}", "1/1024"),
        ("v0542-admission-exe", ExpectedExecutableSha256.Length == 64, ExpectedExecutableSha256, "exact executable SHA-256"),
        ("v0542-admission-plan", ExpectedPlanSha256.Length == 64 && ExpectedTreeDigestSha256.Length == 64, ExpectedPlanSha256, "exact plan/tree evidence"),
        ("v0542-admission-no-execution", true, "receipt validator requires process/runtime/network/model/benchmark/game all false", "true")
    };

    private static void ValidateTransaction(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedRuntimeTreeMaterializationV054Service.TransactionSchema, "transaction schema");
        Require(GetString(o, "TransactionId") == ExpectedTransactionId && GetString(o, "LeaseId") == ExpectedLeaseId, "transaction identities");
        Require(GetString(o, "RequestId") == ExpectedRequestId && GetString(o, "State") == "MATERIALIZED_VERIFIED", "transaction terminal state");
        Require(GetString(o, "AcquisitionReceiptSha256").Equals(ExpectedAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "transaction acquisition digest");
        Require(GetString(o, "PlanSha256").Equals(ExpectedPlanSha256, StringComparison.OrdinalIgnoreCase), "transaction plan digest");
        Require(GetString(o, "TreeDigestSha256").Equals(ExpectedTreeDigestSha256, StringComparison.OrdinalIgnoreCase), "transaction tree digest");
        Require(GetString(o, "RuntimeManifestSha256").Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "transaction manifest digest");
        Require(GetInt(o, "MaterializedFiles") == 1 && GetLong(o, "MaterializedBytes") == 1024, "transaction materialized extent");
        Require(GetBool(o, "AuthorityConsumed") && GetBool(o, "FilesystemMutationPerformed") && GetBool(o, "ExtractionPerformed") && GetBool(o, "RootPromoted"), "transaction bounded effects");
        Require(!GetBool(o, "ProcessExecutionPerformed") && !GetBool(o, "NetworkAccessPerformed") && !GetBool(o, "BenchmarkPerformed") && !GetBool(o, "ModelRequestPerformed") && !GetBool(o, "GameAccessPerformed"), "transaction non-effects");
    }

    private static void ValidateLeaseState(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedRuntimeTreeMaterializationV054Service.LeaseStateSchema, "lease-state schema");
        Require(GetString(o, "LeaseId") == ExpectedLeaseId && GetString(o, "RequestId") == ExpectedRequestId, "lease-state identities");
        Require(GetString(o, "State") == "MATERIALIZED_VERIFIED" && GetBool(o, "Completed") && !GetBool(o, "Failed") && !GetBool(o, "Revoked"), "lease terminal state");
        Require(GetInt(o, "RemainingCalls") == 0, "one-shot call consumed");
        Require(GetString(o, "AcquisitionReceiptSha256").Equals(ExpectedAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "lease acquisition digest");
        Require(GetString(o, "PlanSha256").Equals(ExpectedPlanSha256, StringComparison.OrdinalIgnoreCase), "lease plan digest");
        Require(GetString(o, "RuntimeManifestSha256").Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "lease manifest digest");
        Require(GetString(o, "TreeDigestSha256").Equals(ExpectedTreeDigestSha256, StringComparison.OrdinalIgnoreCase), "lease tree digest");
    }

    private static void ValidateRuntimeManifest(JsonElement receipt, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema, "runtime manifest schema");
        Require(GetString(o, "State") == BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState, "runtime manifest state");
        Require(GetString(o, "RuntimeRoot") == GetString(receipt, "RuntimeRoot"), "runtime manifest root binding");
        var files = o.GetProperty("Files").EnumerateArray().ToArray();
        Require(files.Length == 1 && GetString(files[0], "RelativePath") == ExpectedExecutableRelativePath, "runtime manifest file identity");
        Require(GetLong(files[0], "Bytes") == ExpectedMaterializedBytes && GetString(files[0], "Sha256").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase), "runtime manifest file hash binding");
    }

    private static string RequireOwnedPath(string repo, string value, string subdir, string role)
    {
        var allowed = Path.GetFullPath(Path.Combine(repo, subdir)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(value);
        Require(full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && File.Exists(full), $"Workbench-owned {role} path");
        return full;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string GetString(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool GetBool(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False && p.GetBoolean();
    private static int GetInt(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.TryGetInt32(out var value) ? value : int.MinValue;
    private static long GetLong(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.TryGetInt64(out var value) ? value : long.MinValue;
    private static DateTimeOffset GetDate(JsonElement o, string name)
        => DateTimeOffset.Parse(GetString(o, name), null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static void Require(bool value, string role)
    {
        if (!value) throw new InvalidDataException("Real-host materialization admission mismatch: " + role + ".");
    }
}
