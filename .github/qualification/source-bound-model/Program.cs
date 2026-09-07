using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
int rejected = 0;
async Task Refuse(Func<Task> action, string classification)
{
    try { await action(); }
    catch (InvalidDataException ex) when (ex.Message == classification) { rejected++; return; }
    throw new Exception("Expected refusal: " + classification);
}
Task Check(Action action) { action(); return Task.CompletedTask; }
var root = Path.Combine(Path.GetTempPath(), "workbench-model-binding-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var repo = Path.Combine(root, "Workbench");
    Directory.CreateDirectory(Path.Combine(repo, ".git"));
    var runtime = Path.Combine(root, "runtime");
    Directory.CreateDirectory(runtime);
    // Deliberately not executable: qualification cannot accidentally invoke a real model.
    var exeBytes = Encoding.UTF8.GetBytes("NONEXECUTABLE_SYNTHETIC_RUNTIME");
    var modelBytes = Encoding.UTF8.GetBytes("SYNTHETIC_MODEL");
    var exe = Path.Combine(runtime, BoundedLocalModelInvocationV055Service.FixtureExecutableName);
    var model = Path.Combine(root, "model.bin");
    File.WriteAllBytes(exe, exeBytes);
    File.WriteAllBytes(model, modelBytes);
    var manifest = Path.Combine(root, "manifest.json");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new RuntimeTreeManifestV053(
        BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema, "0.53", "synthetic-tree", "MATERIALIZED_VERIFIED",
        runtime, new[] { new RuntimeTreeFileV053(Path.GetFileName(exe), exeBytes.Length, Hash(exeBytes), "EXECUTABLE") }, "synthetic")));
    var acquisitionRoot = Path.Combine(repo, "artifacts", "artifact-acquisition-v052");
    Directory.CreateDirectory(acquisitionRoot);
    var acquisition = Path.Combine(acquisitionRoot, "synthetic-receipt.json");
    File.WriteAllText(acquisition, JsonSerializer.Serialize(new {
        Schema = BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema,
        State = "ACQUISITION_VERIFIED", Status = "ACQUISITION_VERIFIED", AllArtifactsSha256Verified = true,
        Items = new[] { new { ArtifactId = "fixture-model", ExpectedSizeMatched = true, ExpectedSha256Matched = true,
            FinalPathPromoted = true, ExistingVerifiedReused = false, ObservedFileBytes = modelBytes.Length,
            ObservedSha256 = Hash(modelBytes), FinalPath = model } }
    }));
    var request = new LocalModelInvocationRequestV055(BoundedLocalModelInvocationV055Service.RequestSchema,
        "synthetic-model-request", manifest, Hash(File.ReadAllBytes(manifest)), Path.GetFileName(exe), Hash(exeBytes),
        acquisition, Hash(File.ReadAllBytes(acquisition)), "fixture-model", Hash(modelBytes),
        BoundedLocalModelInvocationV055Service.FixtureProfileId, "synthetic request", 1024, 4096, 1024, 600, 160, 60, 60);
    var source = new ModelInvocationSourceBinding(SourceBoundModelInvocation.SourceSchema, request.RequestId,
        "example/fixture", new string('a', 40), new string('b', 64), new string('c', 64), "NONE_BY_SOURCE_RECORD",
        request.InvocationProfileId, request.ExpectedExecutableSha256, request.ExpectedModelSha256,
        1024, 4096, 1024, 600, 160, 60, 60, 1, false);
    var service = new SourceBoundModelInvocation();
    var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
    var preview = service.Preview(root, source, request, default);
    if (before != Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length) throw new Exception("Preview wrote files");
    await Refuse(async () => { await service.GrantAsync(root, source, request, preview.ReviewDigestSha256, false, default); },
        "SEPARATE_MODEL_CONFIRMATION_REQUIRED");
    await Refuse(async () => { await service.GrantAsync(root, source, request with { RequestUtf8 = "changed" }, preview.ReviewDigestSha256, true, default); }, "REVIEW_DRIFT");
    foreach (var bad in new[] {
        source with { MaxCalls = 2 }, source with { SourceAuthorityEffect = "PROCESS_LEASE" },
        source with { SourceAuthorityEffect = "RECEIPT_AUTHORITY" } })
        await Refuse(() => Check(() => SourceBoundModelInvocation.ValidateBinding(bad, request)), "SOURCE_NOT_AUTHORITY");
    foreach (var bad in new[] { source with { MaxOutputTokens = 161 }, source with { MaxOutputChars = 601 },
        source with { TimeoutSeconds = 61 }, source with { MaxStdoutBytes = 5000 } })
        await Refuse(() => Check(() => SourceBoundModelInvocation.ValidateBinding(bad, request)), "SOURCE_BOUND_MISMATCH");
    await Refuse(() => Check(() => SourceBoundModelInvocation.ValidateBinding(source with { RequireProcessNetworkIsolation = true }, request)),
        "PROCESS_NETWORK_ISOLATION_UNPROVEN");

    using var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "translation-fixture.json")));
    var sourceJson = golden.RootElement.GetProperty("model_source_binding").GetRawText();
    var kontur = SourceBoundModelInvocation.ParseSource(sourceJson);
    using var proposalSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "source-binding.schema.json")));
    var required = proposalSchema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToHashSet();
    if (!required.SetEquals(typeof(ModelInvocationSourceBinding).GetProperties().Select(x => x.Name)))
        throw new Exception("Proposal schema/record keys diverged");
    var properties = proposalSchema.RootElement.GetProperty("properties");
    if (proposalSchema.RootElement.GetProperty("additionalProperties").GetBoolean() ||
        properties.GetProperty("Schema").GetProperty("const").GetString() != SourceBoundModelInvocation.SourceSchema ||
        properties.GetProperty("MaxCalls").GetProperty("const").GetInt32() != 1)
        throw new Exception("Proposal schema authority boundary diverged");
    foreach (var limit in new Dictionary<string, int> {
        ["MaxRequestBytes"] = BoundedLocalModelInvocationV055Service.HardMaxRequestBytes,
        ["MaxStdoutBytes"] = BoundedLocalModelInvocationV055Service.HardMaxStdoutBytes,
        ["MaxStderrBytes"] = BoundedLocalModelInvocationV055Service.HardMaxStderrBytes,
        ["MaxOutputChars"] = BoundedLocalModelInvocationV055Service.HardMaxOutputChars,
        ["MaxOutputTokens"] = BoundedLocalModelInvocationV055Service.HardMaxOutputTokens,
        ["TimeoutSeconds"] = BoundedLocalModelInvocationV055Service.MaxTimeoutSeconds,
        ["TtlSeconds"] = BoundedLocalModelInvocationV055Service.MaxTtlSeconds })
        if (properties.GetProperty(limit.Key).GetProperty("maximum").GetInt32() != limit.Value)
            throw new Exception("Proposal schema ceiling diverged: " + limit.Key);
    var sourceNode = System.Text.Json.Nodes.JsonNode.Parse(sourceJson)!.AsObject();
    sourceNode.Remove("RequireProcessNetworkIsolation");
    await Refuse(() => Check(() => SourceBoundModelInvocation.ParseSource(sourceNode.ToJsonString())), "SOURCE_KEYS_INVALID");
    await Refuse(() => Check(() => SourceBoundModelInvocation.ParseSource(sourceJson.TrimEnd().TrimEnd('}') + ",\"MaxCalls\":1}")), "SOURCE_KEYS_INVALID");
    var processBinding = golden.RootElement.GetProperty("runtime_source_binding").Deserialize<RuntimeExecutionSourceBindingV055>()!;
    if (processBinding.ModelRequestAuthorized || processBinding.SourceFrontier != kontur.SourceFrontier)
        throw new Exception("Process/model boundary or origin mismatch");
    var konturRequest = request with { RequestId = kontur.BindingId, ExpectedExecutableSha256 = kontur.ExpectedExecutableSha256,
        ExpectedModelSha256 = kontur.ExpectedModelSha256, InvocationProfileId = kontur.InvocationProfileId };
    await Refuse(() => Check(() => SourceBoundModelInvocation.ValidateBinding(kontur, konturRequest)), "INVOCATION_PROFILE_UNSUPPORTED");
    await Refuse(() => Check(() => SourceBoundModelInvocation.ValidateBinding(kontur, konturRequest with { InvocationProfileId = request.InvocationProfileId })), "PROFILE_SUBSTITUTION");

    var permit = await service.GrantAsync(root, source, request, preview.ReviewDigestSha256, true, default);
    await Refuse(async () => { await service.GrantAsync(root, source, request, preview.ReviewDigestSha256, true, default); }, "BINDING_ALREADY_ISSUED");
    await Refuse(async () => { await new SourceBoundModelInvocation().InvokeAsync(permit, default); }, "MODEL_PERMIT_UNAVAILABLE");
    foreach (var path in Directory.GetFiles(Path.Combine(repo, "artifacts"), "*.json", SearchOption.AllDirectories))
        if (File.ReadAllText(path).Contains(request.RequestUtf8, StringComparison.Ordinal)) throw new Exception("Request text persisted");
    if (JsonSerializer.Serialize(permit).Contains("Bearer", StringComparison.Ordinal)) throw new Exception("Bearer exposed");

    var outputText = "Synthetic output";
    var outputHash = Hash(Encoding.UTF8.GetBytes(outputText));
    var requestHash = Hash(Encoding.UTF8.GetBytes(request.RequestUtf8));
    T Object<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    var receipt = Object<LocalModelInvocationExecutionReceiptV055>(new {
        Schema = BoundedLocalModelInvocationV055Service.ExecutionReceiptSchema, Version = "0.55.0", LeaseId = "synthetic-lease",
        RequestId = request.RequestId, RequestDigestSha256 = requestHash, RequestBytes = Encoding.UTF8.GetByteCount(request.RequestUtf8),
        InvocationProfileId = request.InvocationProfileId, request.RuntimeTreeManifestSha256, request.ModelAcquisitionReceiptSha256,
        request.ModelArtifactId, ExecutableSha256BeforeStart = request.ExpectedExecutableSha256,
        ObservedProcessImageSha256 = request.ExpectedExecutableSha256, ModelSha256BeforeStart = request.ExpectedModelSha256,
        Status = "UNTRUSTED_LOCAL_MODEL_OUTPUT", State = "MODEL_INVOCATION_COMPLETED", ModelInvocationAuthorityConsumed = true,
        OneRequestAttempted = true, ExactProcessImageVerified = true, StdoutBytesObserved = outputText.Length,
        StdoutSha256 = outputHash, OutputArtifactSha256 = outputHash, OutputChars = outputText.Length
    });
    var result = Object<LocalModelInvocationPortableResultV055>(new {
        Schema = BoundedLocalModelInvocationV055Service.PortableResultSchema, Version = "0.55.0", request.RequestId,
        request.InvocationProfileId, request.ModelArtifactId, ModelSha256 = request.ExpectedModelSha256,
        ExecutableSha256 = request.ExpectedExecutableSha256, RequestDigestSha256 = requestHash,
        RequestBytes = Encoding.UTF8.GetByteCount(request.RequestUtf8), StdoutSha256 = outputHash,
        OutputTextSha256 = outputHash, OutputText = outputText, OutputChars = outputText.Length, Status = "UNTRUSTED_LOCAL_MODEL_OUTPUT"
    });
    var output = SourceBoundModelInvocation.BindOutput(source, request, preview, permit, receipt, result, "synthetic-lease");
    if (output.SourceClass != "FIXTURE_OUTPUT_NOT_LIVE_MODEL" || output.DisplayPermitCreated || output.ContentReviewComplete)
        throw new Exception("Synthetic output promoted");
    foreach (var bad in new[] { receipt with { DisplayPerformed = true }, receipt with { ProcessNetworkIsolationProven = true },
        receipt with { ActionPermitCreated = true }, receipt with { ResponseAuthorityCreated = true } })
        await Refuse(() => Check(() => SourceBoundModelInvocation.BindOutput(source, request, preview, permit, bad, result, "synthetic-lease")), "OUTPUT_AUTHORITY_WIDENING");
    await Refuse(() => Check(() => SourceBoundModelInvocation.BindOutput(source, request, preview, permit,
        receipt with { LeaseId = "other" }, result, "synthetic-lease")), "OUTPUT_REQUEST_BINDING");
    await Refuse(() => Check(() => SourceBoundModelInvocation.BindOutput(source, request, preview, permit,
        receipt with { FailureClassification = "TIMEOUT" }, result, "synthetic-lease")), "OUTPUT_TERMINAL_STATE");
    await Refuse(() => Check(() => SourceBoundModelInvocation.BindOutput(source, request, preview, permit,
        receipt, result with { OutputText = "altered" }, "synthetic-lease")), "OUTPUT_TEXT_BINDING");
    await Refuse(() => Check(() => SourceBoundModelInvocation.BindOutput(source, request, preview, permit,
        receipt, result with { ModelSha256 = new string('d', 64) }, "synthetic-lease")), "OUTPUT_ARTIFACT_BINDING");

    // Corrupt state before Invoke: refusal occurs before the underlying process boundary.
    var statePath = Directory.GetFiles(Path.Combine(repo, "artifacts", "local-model-invocation-v055"), "state.json", SearchOption.AllDirectories).Single();
    File.AppendAllText(statePath, " ");
    await Refuse(async () => { await service.InvokeAsync(permit, default); }, "MODEL_STATE_DRIFT");
    await Refuse(async () => { await service.InvokeAsync(permit, default); }, "MODEL_PERMIT_UNAVAILABLE");
    Console.WriteLine($"SOURCE_BOUND_MODEL_PASS hostile={rejected} translation_roundtrip=true synthetic_grant=true live_model=false process_start=false");
}
finally { Directory.Delete(root, recursive: true); }
