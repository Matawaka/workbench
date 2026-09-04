using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

static string Sha256File(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static async Task ExpectRefusedAsync(string expected, Func<Task> action)
{
    try
    {
        await action();
        throw new Exception($"Expected refusal {expected}, but operation succeeded.");
    }
    catch (RuntimeExecutionExceptionV053 ex) when (ex.Classification == expected)
    {
        Console.WriteLine($"EXPECTED_REFUSAL {expected}");
    }
}

var repo = Path.GetFullPath(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory());
var workspaceRoot = Directory.GetParent(repo)?.FullName ?? throw new Exception("Repository parent missing.");
var temp = Path.Combine(Path.GetTempPath(), "matawaka-v053-runtime-probe-" + Guid.NewGuid().ToString("N"));
var runtimeRoot = Path.Combine(temp, "runtime");
Directory.CreateDirectory(runtimeRoot);
var sourceExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
if (!File.Exists(sourceExe)) throw new Exception("Windows where.exe fixture missing.");
var exe = Path.Combine(runtimeRoot, "probe.exe");
File.Copy(sourceExe, exe, overwrite: false);
var exeSha = Sha256File(exe);
var exeBytes = new FileInfo(exe).Length;
var manifestPath = Path.Combine(temp, "runtime-tree.json");
var manifest = new RuntimeTreeManifestV053(
    BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
    "0.53",
    "ci-runtime-tree",
    BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState,
    runtimeRoot,
    new[] { new RuntimeTreeFileV053("probe.exe", exeBytes, exeSha, "executable") },
    "CI-only no-effect runtime-tree evidence fixture.");
await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
var manifestSha = Sha256File(manifestPath);

var request = new RuntimeExecutionRequestV053(
    BoundedRuntimeExecutionV053Service.RequestSchema,
    "ci-v053-preview",
    manifestPath,
    manifestSha,
    "probe.exe",
    exeSha,
    Array.Empty<string>(),
    ".",
    new Dictionary<string, string>(),
    120,
    0,
    true);

using var service = new BoundedRuntimeExecutionV053Service();
var preview = service.Preview(workspaceRoot, request, CancellationToken.None);
if (!preview.ReadyForExplicitExecutionAuthority || preview.ProcessExecutionPerformed || preview.RuntimeTreeMaterializationPerformed)
    throw new Exception("No-effect preview contract failed.");
if (!preview.ExecutableSha256.Equals(exeSha, StringComparison.OrdinalIgnoreCase) || preview.ExecutableBytes != exeBytes)
    throw new Exception("Executable evidence binding mismatch in preview.");
Console.WriteLine("V053_NO_EFFECT_PREVIEW_PASS");

await ExpectRefusedAsync("SHELL_INDIRECTION_REFUSED", () => Task.Run(() => service.Preview(workspaceRoot, request with { ExecutableRelativePath = "cmd.exe" }, CancellationToken.None)));
await ExpectRefusedAsync("RELATIVE_PATH_REFUSED", () => Task.Run(() => service.Preview(workspaceRoot, request with { ExecutableRelativePath = "../probe.exe" }, CancellationToken.None)));
await ExpectRefusedAsync("ENVIRONMENT_NAME_REFUSED", () => Task.Run(() => service.Preview(workspaceRoot, request with { Environment = new Dictionary<string, string> { ["OPENAI_API_TOKEN"] = "sentinel-secret" } }, CancellationToken.None)));
await ExpectRefusedAsync("RUNTIME_MANIFEST_HASH_MISMATCH", () => Task.Run(() => service.Preview(workspaceRoot, request with { RuntimeTreeManifestSha256 = new string('0', 64) }, CancellationToken.None)));
await ExpectRefusedAsync("EXECUTABLE_REQUEST_MANIFEST_MISMATCH", () => Task.Run(() => service.Preview(workspaceRoot, request with { ExpectedExecutableSha256 = new string('1', 64) }, CancellationToken.None)));
await ExpectRefusedAsync("TTL_REFUSED", () => Task.Run(() => service.Preview(workspaceRoot, request with { TtlSeconds = 1 }, CancellationToken.None)));
await ExpectRefusedAsync("READINESS_BOUND_REFUSED", () => Task.Run(() => service.Preview(workspaceRoot, request with { ReadinessDelayMilliseconds = 5001 }, CancellationToken.None)));

var authority = await service.GrantAsync(workspaceRoot, preview, CancellationToken.None);
var persisted = await File.ReadAllTextAsync(authority.Grant.LeaseStatePath);
if (persisted.Contains(authority.Grant.Bearer, StringComparison.Ordinal))
    throw new Exception("Bearer plaintext leaked into persisted execution lease state.");
if (!persisted.Contains(authority.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
    throw new Exception("Expected bearer SHA-256 evidence missing from persisted lease state.");
if (!persisted.Contains("\"RemainingCalls\": 1", StringComparison.Ordinal))
    throw new Exception("Execution lease did not persist exact one-shot call budget.");
Console.WriteLine("V053_ONE_SHOT_AUTHORITY_PREPARED_PASS");

foreach (var check in BoundedRuntimeExecutionV053Service.RunOfflineContractChecks())
    if (!check.Passed) throw new Exception($"Offline contract check failed: {check.Id} observed={check.Observed} expected={check.Expected}");

var runtimeArtifacts = Path.Combine(repo, "artifacts", "runtime-execution");
if (Directory.Exists(runtimeArtifacts)) Directory.Delete(runtimeArtifacts, recursive: true);
Directory.Delete(temp, recursive: true);
Console.WriteLine("V053_HOSTILE_OFFLINE_PASS");
