using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RealHostModelInvocationAdmissionV0551(
    string ExecutionReceiptPath,
    string ExecutionReceiptSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    string OutputArtifactPath,
    string OutputArtifactSha256,
    string RuntimeManifestPath,
    string RuntimeManifestSha256,
    string ModelAcquisitionReceiptPath,
    string ModelAcquisitionReceiptSha256,
    string LeaseId,
    string TransactionId,
    string RequestId,
    string RequestDigestSha256,
    string ExecutableSha256,
    string ModelSha256,
    long OutputBytes,
    DateTimeOffset ObservedAt);

public static class RealHostModelInvocationAdmissionVerifierV0551
{
    public const string ExpectedRequestId = "modreq-v055-realhost-fixture-mattx-6d05f6223217406685c2945fa71f72b6-v1";
    public const string ExpectedTransactionId = "modtx-5340d725c7434590935227f21071884e";
    public const string ExpectedLeaseId = "modlease-efc700088fc145a5984410197cdede12";
    public const string ExpectedExecutionReceiptSha256 = "b0e8128c62b66afbf5bd45aa9d699464610b930d341634d089eca96a9764b14c";
    public const string ExpectedLeaseStateSha256 = "69fc5ef4901fe546028a14cd169ced4cd56d2226748b6194c8c75bab861e2091";
    public const string ExpectedRequestDigestSha256 = "758d61f26a44448384e5c4468a0dcb7a2abe456067b0f7b505bc28b9411fe931";
    public const int ExpectedRequestBytes = 4;
    public const string ExpectedInvocationProfileId = "FIXTURE_STDIO_V1";
    public const string ExpectedRuntimeManifestSha256 = "b33da53c351395d649641ad9f1d55d138a98c54bd6c4812fafe88111b8059b58";
    public const string ExpectedExecutableRelativePath = "Matawaka.Workbench.V055.ModelFixture.exe";
    public const string ExpectedExecutableSha256 = "bb7f1fe17be0757bc5404378601a1b1ef37af884a929cf7e80b748b6ce379343";
    public const long ExpectedExecutableBytes = 122880;
    public const string ExpectedModelAcquisitionReceiptSha256 = "86f8f6880cde0eed9bd169245d336619a8b68c078e6749fbc9e4c9f90672ecdc";
    public const string ExpectedModelArtifactId = "artifact-v055-fixture-model-v1";
    public const string ExpectedModelSha256 = "db2cb3fe28e2c54fce50bd9c03fd2b131091229c0ceda79289527aaba47226ad";
    public const long ExpectedModelBytes = 6;
    public const string ExpectedOutputSha256 = "4d5273f68e133cdf9d8241be01f0d05d19a597d685a0efde752d53f2b6584702";
    public const long ExpectedOutputBytes = 12;
    public const int ExpectedOutputChars = 12;

    public static RealHostModelInvocationAdmissionV0551 FindExact(string workspaceRoot)
    {
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var receipts = Path.Combine(repo, "artifacts", "local-model-invocation-v055", "receipts");
        if (!Directory.Exists(receipts))
            throw new InvalidDataException("Real-host v0.55 invocation receipt directory is missing.");

        foreach (var path in Directory.GetFiles(receipts, $"execution-{ExpectedTransactionId}-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { return ValidateReceiptFile(workspaceRoot, path); }
            catch (InvalidDataException) { }
        }
        throw new InvalidDataException("No exact real-host v0.55 UNTRUSTED_LOCAL_MODEL_OUTPUT admission receipt was found.");
    }

    public static RealHostModelInvocationAdmissionV0551 ValidateReceiptFile(string workspaceRoot, string receiptPath)
    {
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var receipts = Path.GetFullPath(Path.Combine(repo, "artifacts", "local-model-invocation-v055", "receipts")) + Path.DirectorySeparatorChar;
        var fullReceipt = Path.GetFullPath(receiptPath);
        Require(fullReceipt.StartsWith(receipts, StringComparison.OrdinalIgnoreCase) && File.Exists(fullReceipt),
            "Workbench-owned execution receipt path");
        var receiptSha = HashFile(fullReceipt);
        Require(receiptSha.Equals(ExpectedExecutionReceiptSha256, StringComparison.OrdinalIgnoreCase), "execution receipt SHA-256");

        using var doc = JsonDocument.Parse(File.ReadAllText(fullReceipt, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedLocalModelInvocationV055Service.ExecutionReceiptSchema, "execution receipt schema");
        Require(GetString(o, "Version") == BoundedLocalModelInvocationV055Service.Version, "execution receipt version");
        Require(GetString(o, "RequestId") == ExpectedRequestId, "request id");
        Require(GetString(o, "TransactionId") == ExpectedTransactionId, "transaction id");
        Require(GetString(o, "LeaseId") == ExpectedLeaseId, "lease id");
        Require(GetString(o, "State") == "MODEL_INVOCATION_COMPLETED", "terminal invocation state");
        Require(GetString(o, "Status") == "UNTRUSTED_LOCAL_MODEL_OUTPUT", "terminal untrusted-output status");
        Require(GetString(o, "InvocationProfileId") == ExpectedInvocationProfileId, "fixture profile id");
        Require(GetString(o, "RequestDigestSha256").Equals(ExpectedRequestDigestSha256, StringComparison.OrdinalIgnoreCase), "request digest");
        Require(GetInt(o, "RequestBytes") == ExpectedRequestBytes, "request bytes");
        Require(GetString(o, "RuntimeTreeManifestSha256").Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "runtime manifest digest");
        Require(GetString(o, "ExecutableSha256BeforeStart").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase), "executable digest before start");
        Require(GetString(o, "ModelAcquisitionReceiptSha256").Equals(ExpectedModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "model acquisition receipt digest");
        Require(GetString(o, "ModelArtifactId") == ExpectedModelArtifactId, "model artifact id");
        Require(GetString(o, "ModelSha256BeforeStart").Equals(ExpectedModelSha256, StringComparison.OrdinalIgnoreCase), "model digest before start");
        Require(GetLong(o, "ModelBytes") == ExpectedModelBytes, "model bytes");
        Require(GetBool(o, "ExactProcessImageVerified"), "exact process image verification");
        Require(GetBool(o, "OneRequestAttempted"), "one request attempted");
        Require(GetString(o, "ObservedProcessImageSha256").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase), "observed process image digest");
        Require(GetLong(o, "StdoutBytesObserved") == ExpectedOutputBytes, "stdout bytes");
        Require(GetString(o, "StdoutSha256").Equals(ExpectedOutputSha256, StringComparison.OrdinalIgnoreCase), "stdout digest");
        Require(GetLong(o, "StderrBytesObserved") == 0, "zero stderr bytes");
        Require(GetString(o, "OutputArtifactSha256").Equals(ExpectedOutputSha256, StringComparison.OrdinalIgnoreCase), "output artifact digest");
        Require(GetInt(o, "OutputChars") == ExpectedOutputChars, "output chars");
        Require(GetBool(o, "ModelInvocationAuthorityConsumed"), "model invocation authority consumed");
        Require(!GetBool(o, "WorkbenchNetworkTransportPerformed"), "no Workbench network transport");
        Require(!GetBool(o, "ServerOrPortRequestedByInvocationProfile"), "no server or port requested");
        Require(!GetBool(o, "ProcessNetworkIsolationProven"), "no unsupported process network isolation claim");
        Require(!GetBool(o, "AutomaticRetryPerformed") && !GetBool(o, "AutomaticResumePerformed"), "no retry or resume");
        Require(!GetBool(o, "BenchmarkPerformed") && !GetBool(o, "GameAccessPerformed") && !GetBool(o, "DisplayPerformed"),
            "no benchmark game or display effect");
        Require(!GetBool(o, "ResponseAuthorityCreated") && !GetBool(o, "ActionPermitCreated") && !GetBool(o, "SuccessorPermitCreated"),
            "no response action or successor authority");
        Require(IsNull(o, "FailureClassification"), "no failure classification");

        var runtimeManifestPath = RequireOutsideExistingFile(repo, GetString(o, "RuntimeTreeManifestPath"), "runtime manifest");
        Require(HashFile(runtimeManifestPath).Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "runtime manifest file digest");
        var executablePath = RequireOutsideExistingFile(repo, GetString(o, "ExecutablePath"), "fixture executable");
        Require(new FileInfo(executablePath).Length == ExpectedExecutableBytes, "fixture executable bytes");
        Require(HashFile(executablePath).Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase), "fixture executable file digest");
        Require(Path.GetFileName(executablePath).Equals(ExpectedExecutableRelativePath, StringComparison.OrdinalIgnoreCase), "fixture executable name");
        var observedImagePath = RequireOutsideExistingFile(repo, GetString(o, "ObservedProcessImagePath"), "observed process image");
        Require(PathsEqual(observedImagePath, executablePath), "observed process image path binding");

        var modelReceiptPath = RequireOwnedPath(repo, GetString(o, "ModelAcquisitionReceiptPath"),
            Path.Combine("artifacts", "artifact-acquisition-v052"), "model acquisition receipt");
        Require(HashFile(modelReceiptPath).Equals(ExpectedModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "model acquisition receipt file digest");
        var modelPath = RequireOutsideExistingFile(repo, GetString(o, "ModelPath"), "model fixture");
        Require(new FileInfo(modelPath).Length == ExpectedModelBytes, "model fixture bytes");
        Require(HashFile(modelPath).Equals(ExpectedModelSha256, StringComparison.OrdinalIgnoreCase), "model fixture file digest");

        var outputPath = RequireOutsideExistingFile(repo, GetString(o, "OutputArtifactPath"), "untrusted output artifact");
        Require(new FileInfo(outputPath).Length == ExpectedOutputBytes, "output artifact bytes");
        Require(HashFile(outputPath).Equals(ExpectedOutputSha256, StringComparison.OrdinalIgnoreCase), "output artifact file digest");

        var statePath = RequireOwnedPath(repo,
            Path.Combine(repo, "artifacts", "local-model-invocation-v055", "leases", ExpectedLeaseId, "state.json"),
            Path.Combine("artifacts", "local-model-invocation-v055", "leases"), "model invocation lease state");
        var stateSha = HashFile(statePath);
        Require(stateSha.Equals(ExpectedLeaseStateSha256, StringComparison.OrdinalIgnoreCase), "terminal lease-state digest");

        ValidateLeaseState(statePath);
        ValidateRuntimeManifest(runtimeManifestPath, executablePath);
        ValidateModelAcquisitionReceipt(modelReceiptPath, modelPath);

        return new RealHostModelInvocationAdmissionV0551(
            fullReceipt, receiptSha, statePath, stateSha, outputPath, ExpectedOutputSha256,
            runtimeManifestPath, ExpectedRuntimeManifestSha256, modelReceiptPath, ExpectedModelAcquisitionReceiptSha256,
            ExpectedLeaseId, ExpectedTransactionId, ExpectedRequestId, ExpectedRequestDigestSha256,
            ExpectedExecutableSha256, ExpectedModelSha256, ExpectedOutputBytes, GetDate(o, "ObservedAt"));
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("v0551-admission-request", ExpectedRequestId.StartsWith("modreq-v055-realhost-fixture-", StringComparison.Ordinal), ExpectedRequestId, "exact v0.55 real-host fixture request"),
        ("v0551-admission-receipt", ExpectedExecutionReceiptSha256.Length == 64, ExpectedExecutionReceiptSha256, "exact execution receipt SHA-256"),
        ("v0551-admission-state", ExpectedLeaseStateSha256.Length == 64, ExpectedLeaseStateSha256, "exact terminal lease-state SHA-256"),
        ("v0551-admission-output", ExpectedOutputBytes == 12 && ExpectedOutputSha256.Length == 64, $"{ExpectedOutputBytes}/{ExpectedOutputSha256}", "12 exact untrusted output bytes"),
        ("v0551-admission-profile", ExpectedInvocationProfileId == BoundedLocalModelInvocationV055Service.FixtureProfileId, ExpectedInvocationProfileId, BoundedLocalModelInvocationV055Service.FixtureProfileId),
        ("v0551-admission-no-replay", true, "validator requires Completed=true and RemainingCalls=0", "terminal one-shot authority")
    };

    private static void ValidateLeaseState(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedLocalModelInvocationV055Service.LeaseStateSchema, "lease-state schema");
        Require(GetString(o, "Version") == BoundedLocalModelInvocationV055Service.Version, "lease-state version");
        Require(GetString(o, "LeaseId") == ExpectedLeaseId && GetString(o, "RequestId") == ExpectedRequestId, "lease-state identities");
        Require(GetString(o, "RequestDigestSha256").Equals(ExpectedRequestDigestSha256, StringComparison.OrdinalIgnoreCase) && GetInt(o, "RequestBytes") == ExpectedRequestBytes,
            "lease request binding");
        Require(GetString(o, "State") == "MODEL_INVOCATION_COMPLETED", "lease terminal state");
        Require(GetInt(o, "MaxCalls") == 1 && GetInt(o, "RemainingCalls") == 0, "lease one-shot budget consumed");
        Require(GetBool(o, "Completed") && !GetBool(o, "Failed") && !GetBool(o, "Revoked"), "lease terminal completion flags");
        Require(IsNull(o, "FailureClassification"), "lease has no failure classification");
        Require(GetString(o, "RuntimeTreeManifestSha256").Equals(ExpectedRuntimeManifestSha256, StringComparison.OrdinalIgnoreCase), "lease runtime manifest digest");
        Require(GetString(o, "ExecutableSha256").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase) && GetLong(o, "ExecutableBytes") == ExpectedExecutableBytes,
            "lease executable binding");
        Require(GetString(o, "ModelAcquisitionReceiptSha256").Equals(ExpectedModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase), "lease model receipt digest");
        Require(GetString(o, "ModelArtifactId") == ExpectedModelArtifactId && GetString(o, "ModelSha256").Equals(ExpectedModelSha256, StringComparison.OrdinalIgnoreCase) && GetLong(o, "ModelBytes") == ExpectedModelBytes,
            "lease model binding");
        Require(GetString(o, "InvocationProfileId") == ExpectedInvocationProfileId, "lease profile binding");
    }

    private static void ValidateRuntimeManifest(string path, string executablePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema, "runtime manifest schema");
        Require(GetString(o, "Version") == "0.53" && GetString(o, "State") == BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState, "runtime manifest terminal state");
        var runtimeRoot = Path.GetFullPath(GetString(o, "RuntimeRoot"));
        Require(PathsEqual(Path.Combine(runtimeRoot, ExpectedExecutableRelativePath), executablePath), "runtime manifest executable path");
        var files = o.GetProperty("Files").EnumerateArray().ToArray();
        Require(files.Length == 1, "exact one runtime manifest file");
        Require(GetString(files[0], "RelativePath") == ExpectedExecutableRelativePath, "runtime manifest file identity");
        Require(GetLong(files[0], "Bytes") == ExpectedExecutableBytes && GetString(files[0], "Sha256").Equals(ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase),
            "runtime manifest executable digest binding");
        Require(GetString(files[0], "Role") == "executable", "runtime manifest executable role");
    }

    private static void ValidateModelAcquisitionReceipt(string path, string modelPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var o = doc.RootElement;
        Require(GetString(o, "Schema") == BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema, "model acquisition receipt schema");
        Require(GetString(o, "State") == "ACQUISITION_VERIFIED" && GetString(o, "Status") == "ACQUISITION_VERIFIED", "model acquisition terminal state");
        Require(GetBool(o, "AllArtifactsSha256Verified"), "model acquisition all artifacts verified");
        Require(!GetBool(o, "ExtractionPerformed") && !GetBool(o, "ProcessExecutionPerformed") && !GetBool(o, "RuntimeStartPerformed") &&
                !GetBool(o, "BenchmarkPerformed") && !GetBool(o, "ModelRequestPerformed") && !GetBool(o, "GameAccessPerformed"),
            "model acquisition non-effects");
        var item = o.GetProperty("Items").EnumerateArray()
            .SingleOrDefault(x => GetString(x, "ArtifactId") == ExpectedModelArtifactId);
        Require(item.ValueKind == JsonValueKind.Object, "model acquisition item exists");
        Require(GetLong(item, "ObservedFileBytes") == ExpectedModelBytes, "model acquisition item bytes");
        Require(GetString(item, "ObservedSha256").Equals(ExpectedModelSha256, StringComparison.OrdinalIgnoreCase), "model acquisition item digest");
        Require(GetBool(item, "ExpectedSizeMatched") && GetBool(item, "ExpectedSha256Matched"), "model acquisition expected identity matched");
        Require(GetBool(item, "FinalPathPromoted") || GetBool(item, "ExistingVerifiedReused"), "model acquisition final verified path");
        Require(PathsEqual(GetString(item, "FinalPath"), modelPath), "model acquisition final path binding");
    }

    private static string RequireOwnedPath(string repo, string value, string subdir, string role)
    {
        var allowed = Path.GetFullPath(Path.Combine(repo, subdir)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(value);
        Require(full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && File.Exists(full), $"Workbench-owned {role} path");
        return full;
    }

    private static string RequireOutsideExistingFile(string repo, string value, string role)
    {
        Require(!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value), $"absolute {role} path");
        var full = Path.GetFullPath(value);
        var repoRoot = Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Require(!full.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full), $"existing external {role} path");
        return full;
    }

    private static bool PathsEqual(string left, string right)
        => Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

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
    private static bool IsNull(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Null;
    private static void Require(bool value, string role)
    {
        if (!value) throw new InvalidDataException("Real-host v0.55 model-invocation admission mismatch: " + role + ".");
    }
}
