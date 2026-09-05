using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

if (args.Length != 1)
    throw new InvalidOperationException("Pass exact fixture build directory.");

var fixtureBuild = Path.GetFullPath(args[0]);
var fixtureExe = Path.Combine(fixtureBuild, BoundedLocalModelInvocationV055Service.FixtureExecutableName);
if (!File.Exists(fixtureExe))
    throw new FileNotFoundException("Fixture executable missing.", fixtureExe);

var failures = new List<string>();
var passes = new List<string>();

void Pass(string id) { passes.Add(id); Console.WriteLine($"PASS {id}"); }
void Fail(string id, string detail) { failures.Add(id + ": " + detail); Console.WriteLine($"FAIL {id}: {detail}"); }
void Check(string id, bool condition, string detail)
{
    if (condition) Pass(id); else Fail(id, detail);
}

async Task ExpectRefusalAsync(string id, Func<Task> action, params string[] allowed)
{
    try
    {
        await action();
        Fail(id, "unexpected success");
    }
    catch (LocalModelInvocationExceptionV055 ex)
    {
        if (allowed.Length == 0 || allowed.Contains(ex.Classification, StringComparer.Ordinal))
            Pass(id);
        else
            Fail(id, $"classification={ex.Classification}; expected one of {string.Join(",", allowed)}");
    }
}

void ExpectPreviewRefusal(string id, Func<object> action, params string[] allowed)
{
    try
    {
        _ = action();
        Fail(id, "unexpected success");
    }
    catch (LocalModelInvocationExceptionV055 ex)
    {
        if (allowed.Length == 0 || allowed.Contains(ex.Classification, StringComparer.Ordinal))
            Pass(id);
        else
            Fail(id, $"classification={ex.Classification}; expected one of {string.Join(",", allowed)}");
    }
}

var staticChecks = BoundedLocalModelInvocationV055Service.RunOfflineContractChecks();
foreach (var c in staticChecks)
    Check("contract-" + c.Id, c.Passed, $"observed={c.Observed}; expected={c.Expected}");

Check("request-schema-has-no-arguments-property",
    typeof(LocalModelInvocationRequestV055).GetProperty("Arguments") is null,
    "caller-defined arbitrary process argument vector surfaced");

var positive = CreateContext("NORMAL");
var service = new BoundedLocalModelInvocationV055Service();
var preview = service.Preview(positive.WorkspaceRoot, positive.Request, CancellationToken.None);
Check("preview-ready", preview.ReadyForExplicitModelInvocationAuthority && !preview.ProcessExecutionPerformed && !preview.ModelRequestPerformed,
    "preview authority/effect boundary");
Check("preview-model-bound", preview.ModelSha256 == positive.ModelSha && preview.ModelBytes == positive.ModelBytes, "model identity mismatch");
Check("preview-runtime-bound", preview.ExecutableSha256 == positive.ExeSha, "runtime identity mismatch");
var grant = await service.GrantAsync(positive.WorkspaceRoot, preview, positive.Request.RequestUtf8, CancellationToken.None);
Check("grant-no-effects", !grant.Receipt.ProcessExecutionPerformed && !grant.Receipt.ModelRequestPerformed &&
    !grant.Receipt.BearerPlaintextPersisted && !grant.Receipt.RequestTextPersistedInLeaseState, "grant widened effects/persistence");

var badBearer = grant.Grant with { Bearer = new string('0', grant.Grant.Bearer.Length) };
await ExpectRefusalAsync("wrong-bearer-refused",
    async () => { await service.InvokeAsync(positive.WorkspaceRoot, badBearer, CancellationToken.None); },
    "AUTHORITY_BEARER_MISMATCH");

var executed = await service.InvokeAsync(positive.WorkspaceRoot, grant.Grant, CancellationToken.None);
Check("positive-status", executed.Receipt.Status == "UNTRUSTED_LOCAL_MODEL_OUTPUT" && executed.Result?.Status == "UNTRUSTED_LOCAL_MODEL_OUTPUT",
    "unexpected positive terminal status");
Check("one-request", executed.Receipt.OneRequestAttempted && executed.Receipt.ModelInvocationAuthorityConsumed, "one-shot evidence missing");
Check("positive-output", executed.Result is not null && executed.Result.OutputText.Contains(positive.Request.RequestUtf8.Trim(), StringComparison.Ordinal),
    "fixture output missing request");
Check("portable-no-authority", executed.Result is not null && !executed.Result.ResponseAuthorityCreated &&
    !executed.Result.DisplayPermitCreated && !executed.Result.GameAuthorityCreated &&
    !executed.Result.ActionPermitCreated && !executed.Result.SuccessorPermitCreated, "portable result widened authority");
Check("network-claim-bounded", !executed.Receipt.WorkbenchNetworkTransportPerformed &&
    !executed.Receipt.ServerOrPortRequestedByInvocationProfile && !executed.Receipt.ProcessNetworkIsolationProven,
    "unsupported process network isolation claim");
Check("output-file-bound", executed.Receipt.OutputArtifactPath is not null && File.Exists(executed.Receipt.OutputArtifactPath) &&
    HashFile(executed.Receipt.OutputArtifactPath) == executed.Receipt.OutputArtifactSha256, "output artifact binding failed");

await ExpectRefusalAsync("lease-reuse-refused",
    async () => { await service.InvokeAsync(positive.WorkspaceRoot, grant.Grant, CancellationToken.None); },
    "AUTHORITY_STATE_MISMATCH", "AUTHORITY_ALREADY_COMPLETED", "AUTHORITY_CALL_BUDGET_EXHAUSTED");

var wrongReceipt = CreateContext("NORMAL");
var wrongReceiptReq = wrongReceipt.Request with { ModelAcquisitionReceiptSha256 = new string('a', 64) };
ExpectPreviewRefusal("wrong-model-receipt-hash-refused",
    () => service.Preview(wrongReceipt.WorkspaceRoot, wrongReceiptReq, CancellationToken.None),
    "MODEL_ACQUISITION_RECEIPT_HASH_MISMATCH");

var nonVerified = CreateContext("NORMAL", acquisitionVerified: false);
ExpectPreviewRefusal("nonverified-acquisition-refused",
    () => service.Preview(nonVerified.WorkspaceRoot, nonVerified.Request, CancellationToken.None),
    "MODEL_ACQUISITION_RECEIPT_NOT_VERIFIED");

var unsupported = CreateContext("NORMAL");
var unsupportedReq = unsupported.Request with { InvocationProfileId = "CALLER_ARBITRARY_ARGS_V1" };
ExpectPreviewRefusal("unsupported-profile-refused",
    () => service.Preview(unsupported.WorkspaceRoot, unsupportedReq, CancellationToken.None),
    "INVOCATION_PROFILE_UNSUPPORTED");

var tooLarge = CreateContext("NORMAL", requestText: new string('x', 2048), maxRequestBytes: 32);
ExpectPreviewRefusal("request-byte-ceiling-refused",
    () => service.Preview(tooLarge.WorkspaceRoot, tooLarge.Request, CancellationToken.None),
    "REQUEST_BYTE_CEILING_REFUSED");

var modelDrift = CreateContext("NORMAL");
var driftPreview = service.Preview(modelDrift.WorkspaceRoot, modelDrift.Request, CancellationToken.None);
await File.AppendAllTextAsync(modelDrift.ModelPath, "DRIFT");
await ExpectRefusalAsync("model-drift-before-grant-refused",
    async () => { await service.GrantAsync(modelDrift.WorkspaceRoot, driftPreview, modelDrift.Request.RequestUtf8, CancellationToken.None); },
    "MODEL_HASH_DRIFT");

var exeDrift = CreateContext("NORMAL");
var exePreview = service.Preview(exeDrift.WorkspaceRoot, exeDrift.Request, CancellationToken.None);
await File.AppendAllTextAsync(exeDrift.ExePath, "DRIFT");
await ExpectRefusalAsync("executable-drift-before-grant-refused",
    async () => { await service.GrantAsync(exeDrift.WorkspaceRoot, exePreview, exeDrift.Request.RequestUtf8, CancellationToken.None); },
    "EXECUTABLE_HASH_DRIFT");

await ExecuteTerminalCase("stdout-overrun", "STDOUT_OVER", maxStdout: 1024, maxStderr: 4096, timeoutSeconds: 5,
    expected: "STDOUT_BYTE_CEILING_EXCEEDED");
await ExecuteTerminalCase("stderr-overrun", "STDERR_OVER", maxStdout: 4096, maxStderr: 1024, timeoutSeconds: 5,
    expected: "STDERR_BYTE_CEILING_EXCEEDED");
await ExecuteTerminalCase("timeout", "SLEEP", maxStdout: 4096, maxStderr: 4096, timeoutSeconds: 1,
    expected: "TIMEOUT");
await ExecuteTerminalCase("nonzero-exit", "NONZERO", maxStdout: 4096, maxStderr: 4096, timeoutSeconds: 5,
    expected: "NONZERO_EXIT");
await ExecuteTerminalCase("invalid-utf8", "INVALID_UTF8", maxStdout: 4096, maxStderr: 4096, timeoutSeconds: 5,
    expected: "OUTPUT_INVALID_UTF8");

var expired = CreateContext("NORMAL", ttlSeconds: 1);
var expiredPreview = service.Preview(expired.WorkspaceRoot, expired.Request, CancellationToken.None);
var expiredGrant = await service.GrantAsync(expired.WorkspaceRoot, expiredPreview, expired.Request.RequestUtf8, CancellationToken.None);
await Task.Delay(1300);
await ExpectRefusalAsync("expired-lease-refused",
    async () => { await service.InvokeAsync(expired.WorkspaceRoot, expiredGrant.Grant, CancellationToken.None); },
    "AUTHORITY_EXPIRED");

Console.WriteLine();
Console.WriteLine($"V055 hostile qualification: {passes.Count} PASS / {failures.Count} FAIL");
if (failures.Count > 0)
{
    foreach (var failure in failures) Console.WriteLine("  " + failure);
    return 1;
}
return 0;

async Task ExecuteTerminalCase(string id, string mode, int maxStdout, int maxStderr, int timeoutSeconds, string expected)
{
    var ctx = CreateContext(mode, maxStdout: maxStdout, maxStderr: maxStderr, timeoutSeconds: timeoutSeconds);
    var p = service.Preview(ctx.WorkspaceRoot, ctx.Request, CancellationToken.None);
    var g = await service.GrantAsync(ctx.WorkspaceRoot, p, ctx.Request.RequestUtf8, CancellationToken.None);
    await ExpectRefusalAsync(id + "-refused",
        async () => { await service.InvokeAsync(ctx.WorkspaceRoot, g.Grant, CancellationToken.None); },
        expected);

    var stateJson = await File.ReadAllTextAsync(g.Grant.LeaseStatePath);
    using var doc = JsonDocument.Parse(stateJson);
    var root = doc.RootElement;
    Check(id + "-terminal-state",
        root.GetProperty("State").GetString() == "MODEL_INVOCATION_FAILED_CLOSED" &&
        root.GetProperty("RemainingCalls").GetInt32() == 0 &&
        root.GetProperty("Failed").GetBoolean(),
        "terminal state did not consume authority/fail closed");
}

TestContext CreateContext(
    string mode,
    bool acquisitionVerified = true,
    string requestText = "hello fixture",
    int maxRequestBytes = 4096,
    int maxStdout = 8192,
    int maxStderr = 8192,
    int timeoutSeconds = 5,
    int ttlSeconds = 30)
{
    var root = Path.Combine(Path.GetTempPath(), "matawaka-v055-" + Guid.NewGuid().ToString("N"));
    var workbench = Path.Combine(root, "Workbench");
    Directory.CreateDirectory(workbench);

    var runtimeRoot = Path.Combine(root, "runtime");
    Directory.CreateDirectory(runtimeRoot);
    foreach (var source in Directory.GetFiles(fixtureBuild))
        File.Copy(source, Path.Combine(runtimeRoot, Path.GetFileName(source)), overwrite: true);

    var exePath = Path.Combine(runtimeRoot, BoundedLocalModelInvocationV055Service.FixtureExecutableName);
    var exeSha = HashFile(exePath);
    var exeBytes = new FileInfo(exePath).Length;

    var manifestPath = Path.Combine(root, "runtime-manifest.json");
    WriteJson(manifestPath, new
    {
        Schema = BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
        Version = "0.53",
        ManifestId = "v055-fixture-" + Guid.NewGuid().ToString("N"),
        State = BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState,
        RuntimeRoot = runtimeRoot,
        Files = new[] { new { RelativePath = Path.GetFileName(exePath), Bytes = exeBytes, Sha256 = exeSha, Role = "executable" } },
        Note = "offline v0.55 qualification fixture"
    });
    var manifestSha = HashFile(manifestPath);

    var models = Path.Combine(root, "models");
    Directory.CreateDirectory(models);
    var modelPath = Path.Combine(models, "fixture-model.bin");
    File.WriteAllText(modelPath, mode, new UTF8Encoding(false));
    var modelSha = HashFile(modelPath);
    var modelBytes = new FileInfo(modelPath).Length;

    var receiptDir = Path.Combine(workbench, "artifacts", "artifact-acquisition-v052", "receipts");
    Directory.CreateDirectory(receiptDir);
    var receiptPath = Path.Combine(receiptDir, "fixture-acquisition.json");
    var transactionPath = Path.Combine(workbench, "artifacts", "artifact-acquisition-v052", "transactions", "fixture.json");
    var leaseStatePath = Path.Combine(workbench, "artifacts", "artifact-acquisition-v052", "leases", "fixture", "state.json");
    WriteJson(receiptPath, new
    {
        Schema = BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema,
        Version = "0.52.0",
        ObservedAt = DateTimeOffset.UtcNow,
        TransactionId = "acqtx-fixture",
        LeaseId = "acqlease-fixture",
        RequestId = "acqreq-fixture",
        State = acquisitionVerified ? "ACQUISITION_VERIFIED" : "ACQUISITION_FAILED",
        NetworkBytesObserved = modelBytes,
        Items = new[]
        {
            new
            {
                ArtifactId = "model-fixture",
                SourceUri = "https://example.invalid/immutable/model",
                FileName = "fixture-model.bin",
                FinalPath = modelPath,
                PartialPath = (string?)null,
                State = acquisitionVerified ? "SHA256_VERIFIED" : "FAILED",
                RedirectsObserved = 0,
                ObservedNetworkBytes = modelBytes,
                ObservedFileBytes = (long?)modelBytes,
                ObservedSha256 = modelSha,
                ExpectedSizeMatched = acquisitionVerified,
                ExpectedSha256Matched = acquisitionVerified,
                ExistingVerifiedReused = false,
                FinalPathPromoted = acquisitionVerified,
                NetworkAccessPerformed = false,
                FailureClassification = acquisitionVerified ? null : "TEST_FAILURE"
            }
        },
        TransactionPath = transactionPath,
        TransactionSha256 = new string('1', 64),
        LeaseStatePath = leaseStatePath,
        LeaseStateSha256 = new string('2', 64),
        AllArtifactsSha256Verified = acquisitionVerified,
        NetworkAccessPerformed = false,
        FilesystemMutationPerformed = acquisitionVerified,
        ExtractionPerformed = false,
        ProcessExecutionPerformed = false,
        RuntimeStartPerformed = false,
        BenchmarkPerformed = false,
        ModelRequestPerformed = false,
        GameAccessPerformed = false,
        NonEffects = Array.Empty<string>(),
        Status = acquisitionVerified ? "ACQUISITION_VERIFIED" : "ACQUISITION_FAILED",
        Note = "offline v0.55 qualification fixture"
    });
    var receiptSha = HashFile(receiptPath);

    var request = new LocalModelInvocationRequestV055(
        BoundedLocalModelInvocationV055Service.RequestSchema,
        "v055-" + Guid.NewGuid().ToString("N"),
        manifestPath,
        manifestSha,
        Path.GetFileName(exePath),
        exeSha,
        receiptPath,
        receiptSha,
        "model-fixture",
        modelSha,
        BoundedLocalModelInvocationV055Service.FixtureProfileId,
        requestText,
        maxRequestBytes,
        maxStdout,
        maxStderr,
        4096,
        160,
        timeoutSeconds,
        ttlSeconds);

    return new TestContext(root, workbench, runtimeRoot, exePath, exeSha, modelPath, modelSha, modelBytes, receiptPath, request);
}

void WriteJson(string path, object value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
}

string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

sealed record TestContext(
    string WorkspaceRoot,
    string WorkbenchRoot,
    string RuntimeRoot,
    string ExePath,
    string ExeSha,
    string ModelPath,
    string ModelSha,
    long ModelBytes,
    string ReceiptPath,
    LocalModelInvocationRequestV055 Request);
