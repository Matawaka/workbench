using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

static string ShaFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static async Task ExpectRefusal(Func<Task> action, string expected)
{
    try
    {
        await action();
        throw new Exception($"Expected refusal {expected} was not raised.");
    }
    catch (ProvenanceBoundRuntimeExecutionExceptionV055 ex) when (ex.Classification == expected) { }
}

var offline = ProvenanceBoundRuntimeExecutionV055Service.RunOfflineContractChecks();
if (offline.Any(x => !x.Passed))
    throw new Exception("Offline contract checks failed: " + string.Join(",", offline.Where(x => !x.Passed).Select(x => x.Id)));

var root = Path.Combine(Path.GetTempPath(), "matawaka-v055-probe-" + Guid.NewGuid().ToString("N"));
try
{
    var workspace = Path.Combine(root, "workspace");
    var repository = Path.Combine(workspace, "Workbench");
    var runtime = Path.Combine(root, "runtime");
    var executableDir = Path.Combine(runtime, "bin");
    var evidence = Path.Combine(root, "evidence");
    Directory.CreateDirectory(Path.Combine(repository, ".git"));
    Directory.CreateDirectory(executableDir);
    Directory.CreateDirectory(evidence);

    var sourceExecutable = Environment.ProcessPath ?? throw new Exception("Current process image unavailable.");
    var executable = Path.Combine(executableDir, "safe-probe-runtime.exe");
    File.Copy(sourceExecutable, executable);
    var executableSha = ShaFile(executable);
    var manifestPath = Path.Combine(evidence, "runtime-tree-manifest.json");
    var manifest = new RuntimeTreeManifestV053(
        BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
        "0.53",
        "runtime-tree-v055-probe",
        BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState,
        runtime,
        new[] { new RuntimeTreeFileV053("bin/safe-probe-runtime.exe", new FileInfo(executable).Length, executableSha, "EXECUTABLE") },
        "Synthetic local evidence for no-execution qualification only.");
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    const string bindingId = "kgc-wb-v055-probe-0001";
    var binding = new RuntimeExecutionSourceBindingV055(
        ProvenanceBoundRuntimeExecutionV055Service.SourceBindingSchema,
        bindingId,
        "Matawaka/kontur",
        new string('1', 40),
        new string('2', 64),
        new string('3', 64),
        ProvenanceBoundRuntimeExecutionV055Service.SourceAuthorityEffect,
        ProvenanceBoundRuntimeExecutionV055Service.ProcessEffectCeiling,
        true, 1, 60, true, false, false, false, false);
    var request = new RuntimeExecutionRequestV053(
        BoundedRuntimeExecutionV053Service.RequestSchema,
        bindingId,
        manifestPath,
        ShaFile(manifestPath),
        "bin/safe-probe-runtime.exe",
        executableSha,
        Array.Empty<string>(),
        ".",
        new Dictionary<string, string>(),
        60,
        0,
        true);

    using var service = new ProvenanceBoundRuntimeExecutionV055Service();
    var preview = service.Preview(workspace, binding, request, CancellationToken.None);
    if (!preview.ReadyForExplicitConfirmation || preview.SourceRecordGrantedAuthority || preview.ProcessExecutionPerformed)
        throw new Exception("Preview effect boundary failed.");
    if (Directory.Exists(Path.Combine(repository, "artifacts")))
        throw new Exception("Preview unexpectedly created repository artifacts.");

    await ExpectRefusal(
        () => service.GrantAsync(workspace, binding, request, preview.BindingDigestSha256, false, CancellationToken.None),
        "EXPLICIT_CONFIRMATION_REQUIRED");
    await ExpectRefusal(
        () => Task.FromResult(service.Preview(workspace, binding with { ModelRequestAuthorized = true }, request, CancellationToken.None)),
        "HIGHER_EFFECT_AUTHORITY_REFUSED");
    await ExpectRefusal(
        () => Task.FromResult(service.Preview(workspace, binding with { MaxCalls = 2 }, request, CancellationToken.None)),
        "PROCESS_EFFECT_CEILING_REFUSED");
    await ExpectRefusal(
        () => Task.FromResult(service.Preview(workspace, binding, request with { RequestId = "kgc-wb-v055-probe-other" }, CancellationToken.None)),
        "REQUEST_BINDING_ID_MISMATCH");
    await ExpectRefusal(
        () => Task.FromResult(service.Preview(workspace, binding, request with { TtlSeconds = 61 }, CancellationToken.None)),
        "REQUEST_TTL_MISMATCH");
    await ExpectRefusal(
        () => Task.FromResult(service.Preview(workspace, binding with { SourceArtifactSha256 = new string('0', 64) }, request, CancellationToken.None)),
        "DIGEST_REFUSED");

    var authority = await service.GrantAsync(
        workspace, binding, request, preview.BindingDigestSha256, true, CancellationToken.None);
    if (authority.Grant.InnerLeaseBearerExposed || authority.Receipt.InnerLeaseBearerExposed ||
        authority.Grant.BearerPersistedInPlaintextByWorkbench || authority.Receipt.BearerPlaintextPersisted ||
        authority.Receipt.SourceRecordGrantedAuthority || authority.Receipt.ProcessExecutionPerformed ||
        authority.Receipt.ModelRequestAuthorized)
        throw new Exception("Authority receipt boundary failed.");
    var repositoryArtifacts = Directory.GetFiles(Path.Combine(repository, "artifacts"), "*", SearchOption.AllDirectories);
    if (repositoryArtifacts.Any(path => File.ReadAllText(path).Contains(authority.Grant.Bearer, StringComparison.Ordinal)))
        throw new Exception("Outer bearer plaintext was persisted.");

    await ExpectRefusal(
        async () => { _ = await service.ExecuteAsync(workspace, authority.Grant with { Bearer = new string('4', 64) }, CancellationToken.None); },
        "PROVENANCE_BEARER_REFUSED");
    await ExpectRefusal(
        async () => { _ = await service.ExecuteAsync(workspace, authority.Grant with { BindingDigestSha256 = new string('5', 64) }, CancellationToken.None); },
        "PROVENANCE_GRANT_REFUSED");

    var originalState = await File.ReadAllBytesAsync(authority.Grant.LeaseStatePath);
    await File.AppendAllTextAsync(authority.Grant.LeaseStatePath, " ", Encoding.UTF8);
    await ExpectRefusal(
        async () => { _ = await service.ExecuteAsync(workspace, authority.Grant, CancellationToken.None); },
        "PROVENANCE_STATE_HASH_MISMATCH");
    await File.WriteAllBytesAsync(authority.Grant.LeaseStatePath, originalState);

    var originalAuthorityReceipt = await File.ReadAllBytesAsync(authority.Grant.AuthorityReceiptPath);
    await File.AppendAllTextAsync(authority.Grant.AuthorityReceiptPath, " ", Encoding.UTF8);
    await ExpectRefusal(
        async () => { _ = await service.ExecuteAsync(workspace, authority.Grant, CancellationToken.None); },
        "PROVENANCE_AUTHORITY_RECEIPT_HASH_MISMATCH");
    await File.WriteAllBytesAsync(authority.Grant.AuthorityReceiptPath, originalAuthorityReceipt);

    service.Dispose();
    using var restarted = new ProvenanceBoundRuntimeExecutionV055Service();
    await ExpectRefusal(
        async () => { _ = await restarted.ExecuteAsync(workspace, authority.Grant, CancellationToken.None); },
        "PROCESS_LOCAL_INNER_AUTHORITY_UNAVAILABLE");

    var stateText = File.ReadAllText(authority.Grant.LeaseStatePath);
    if (!stateText.Contains("PROVENANCE_BOUND_EXECUTION_PREPARED", StringComparison.Ordinal) ||
        stateText.Contains("PROVENANCE_AUTHORITY_CONSUMED", StringComparison.Ordinal))
        throw new Exception("Hostile checks consumed or mutated valid authority.");

    Console.WriteLine(
        "WORKBENCH_V055_PROVENANCE_BOUND_RUNTIME_EXECUTION_LEASE_PASS " +
        $"offline={offline.Count} hostile=10 previewEffect=false innerBearerExposed=false " +
        "restartResume=false processStarted=false model=false network=false game=false display=false");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}
