using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Matawaka.Workbench.JevLab;

var package = args.Length == 1 ? Path.GetFullPath(args[0]) : FindPackage();
var tests = new List<(string Name, Action Run)>();
var fixtureRoot = Path.Combine(package, "fixtures", "read-only");
var fixtureCandidate = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "candidate.json")))!;
var fixtureReceipt = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "receipt.json")))!;
var fixtureManifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "inputs.json")))!;
var temporaryRoot = Path.Combine(Path.GetTempPath(), "matawaka-jev-lab-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
var fixtureIndex = 0;

void Test(string name, Action run) => tests.Add((name, run));
void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); }
void Reject(Action run, string? code = null)
{
    try { run(); }
    catch (JevInputException error)
    {
        Assert(error.Code.All(c => char.IsAsciiLetterUpper(c) || c is '_' or >= '0' and <= '9'), "Exception exposes input text");
        Assert(code is null || code == error.Code, $"Expected {code}; got {error.Code}");
        return;
    }
    throw new Exception("Invalid fixture was accepted");
}
string Fixture(Action<JsonNode, JsonNode, JsonNode>? mutate = null,
    Func<byte[], byte[]>? candidateBytes = null, Func<byte[], byte[]>? receiptBytes = null,
    Func<byte[], byte[]>? manifestBytes = null)
{
    var c = fixtureCandidate.DeepClone(); var r = fixtureReceipt.DeepClone(); var m = fixtureManifest.DeepClone();
    mutate?.Invoke(c, r, m);
    var cb = Encoding.UTF8.GetBytes(c.ToJsonString()); var rb = Encoding.UTF8.GetBytes(r.ToJsonString());
    if (candidateBytes is not null) cb = candidateBytes(cb);
    if (receiptBytes is not null) rb = receiptBytes(rb);
    if (m["artifacts"]?["candidate"] is JsonNode ca) ca["rawSha256"] = Hash(cb);
    if (m["artifacts"]?["receipt"] is JsonNode ra) ra["rawSha256"] = Hash(rb);
    var mb = Encoding.UTF8.GetBytes(m.ToJsonString());
    if (manifestBytes is not null) mb = manifestBytes(mb);
    var directory = Path.Combine(temporaryRoot, (++fixtureIndex).ToString());
    Directory.CreateDirectory(directory);
    File.WriteAllBytes(Path.Combine(directory, "candidate.json"), cb);
    File.WriteAllBytes(Path.Combine(directory, "receipt.json"), rb);
    File.WriteAllBytes(Path.Combine(directory, "inputs.json"), mb);
    return Path.Combine(directory, "inputs.json");
}
void Mutant(string name, Action<JsonNode, JsonNode, JsonNode> mutate, string? code = null) =>
    Test(name, () => Reject(() => JevObservationReader.Load(Fixture(mutate)), code));
byte[] Bom(byte[] bytes) => [0xef, 0xbb, 0xbf, .. bytes];

Test("read-only demo displays six immutable signals with no reference admission", () =>
{
    var observation = JevObservationReader.Load(Path.Combine(fixtureRoot, "inputs.json"));
    Assert(observation.CaseId == "demo-read-only" && observation.Signals.Count == 6, "Missing demo fields");
    Assert(observation.Signals.Single(s => s.Id == "goalAlignment").Probability == 0.94, "Wrong probability");
    Assert(observation.Signals.Count(s => s.ResearchOnly) == 1 && observation.Signals.Single(s => s.ResearchOnly).Id == "operationalSpecificity", "Research boundary lost");
    Assert(observation.ReferenceStatus == "NOT_ASSESSED", "Import created reference admission");
    Assert(observation.IntegritySummary.Contains("NOT_AUTHENTICATED", StringComparison.Ordinal), "Unverified identity omitted");
    Assert(observation.IntegritySummary.Contains("RESPONSE_LEASE_AND_SOURCE_ORIGINALS_NOT_VERIFIED", StringComparison.Ordinal), "Original evidence gap omitted");
    var signals = (IList<JevSignal>)observation.Signals;
    Assert(signals.IsReadOnly, "Mutable signals escaped");
    try { signals[0] = new JevSignal("bad", "bad", 1, false); throw new Exception("Mutable collection"); }
    catch (NotSupportedException) { }
});
Test("scope expansion demo is loadable", () =>
{
    var o = JevObservationReader.Load(Path.Combine(package, "fixtures", "scope-expansion", "inputs.json"));
    Assert(o.Signals.Single(s => s.Id == "scopeExpansion").Probability > 0.9, "Wrong scope demo");
});
Test("load has no mutation of fixture files", () =>
{
    var p = Fixture(); var directory = Path.GetDirectoryName(p)!;
    var before = Directory.GetFiles(directory).ToDictionary(f => f, f => Hash(File.ReadAllBytes(f)));
    JevObservationReader.Load(p);
    Assert(Directory.GetFiles(directory).Length == before.Count && before.All(e => Hash(File.ReadAllBytes(e.Key)) == e.Value), "Input was changed");
});
Test("frozen public historical candidate and actual TypeSafe receipt import", () =>
{
    var historical = Path.GetFullPath(Path.Combine(package, "..", "jev-shadow-evidence-corpus-v001", "evidence", "synthetic-control-001"));
    var c = File.ReadAllBytes(Path.Combine(historical, "candidate.json"));
    var r = File.ReadAllBytes(Path.Combine(historical, "receipt.json"));
    var p = Fixture(candidateBytes: _ => c, receiptBytes: _ => r);
    var o = JevObservationReader.Load(p);
    Assert(o.Provider == "typesafe.jev" && o.RequestedModel == "jev-latest" && o.ObservedModel == "jev-1.13.0", "Alias/version identity wrong");
    Assert(o.CandidateRawSha256 == Hash(c) && o.ReceiptRawSha256 == Hash(r), "Original raw hash wrong");
});
Test("Node canonical cross-runtime Unicode/string escaping vector", () =>
{
    // Hashes computed independently with original Node canonical-json.js. Covers Cyrillic,
    // supplementary Unicode, U+2028/U+2029, controls, quotes, apostrophe, slash and HTML chars.
    var p = Fixture((c, r, _) =>
    {
        c["providerRequest"]!["state"]!["declaredIntent"] = "Проверка \"статуса\" / \\ \b\f\n\r\t \u0001 <>& ' \u2028\u2029 😀";
        c["requestDigest"] = "sha256:34b052625ec8666a3a6bcf05bc85fc769143fc56ec7d63706f6eb1d126e7a660";
        r["candidateRequestDigest"] = c["requestDigest"]!.DeepClone();
        r["candidateDigest"] = "sha256:18be02d45dc450f963a68433d2dd44e1926c1e5c3ab4301e562c2bf083e4ca58";
        r["adapterRequestDigest"] = "sha256:10aa0e8e2226418337d3c49c5d622a0c4bdbccdc6326e27e87dc07c3dd227f47";
        r["consumptionRecord"]!["candidateDigest"] = r["candidateDigest"]!.DeepClone();
        r["consumptionRecord"]!["requestDigest"] = c["requestDigest"]!.DeepClone();
    });
    Assert(JevObservationReader.Load(p).ContextJson.Contains("Проверка", StringComparison.Ordinal), "Unicode context lost");
});
Test("single BOM on manifest/candidate/receipt retains original raw hashes", () =>
{
    var p = Fixture(candidateBytes: Bom, receiptBytes: Bom, manifestBytes: Bom);
    var o = JevObservationReader.Load(p);
    Assert(o.CandidateRawSha256 == Hash(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(p)!, "candidate.json"))), "BOM stripped from raw hash");
});
Test("double BOM is rejected", () => Reject(() => JevObservationReader.Load(Fixture(candidateBytes: b => Bom(Bom(b)))), "INVALID_JSON"));
Test("raw bytes changed without updating manifest are rejected", () =>
{
    var p = Fixture(); var c = Path.Combine(Path.GetDirectoryName(p)!, "candidate.json");
    File.AppendAllText(c, " ");
    Reject(() => JevObservationReader.Load(p), "RAW_SHA256_MISMATCH");
});
Test("duplicate top-level JSON key rejected even with valid raw hash", () =>
    Reject(() => JevObservationReader.Load(Fixture(receiptBytes: b => Encoding.UTF8.GetBytes("{\"schema\":\"duplicate\"," + Encoding.UTF8.GetString(b)[1..]))), "DUPLICATE_JSON_KEY"));
Test("duplicate nested JSON key rejected", () => Reject(() => JevObservationReader.Load(Fixture(receiptBytes: b =>
    Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("\"noul\":0.94", "\"noul\":0.94,\"noul\":0.1", StringComparison.Ordinal)))), "DUPLICATE_JSON_KEY"));
Test("nonfinite exponent rejected", () => Reject(() => JevObservationReader.Load(Fixture(receiptBytes: b =>
    Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("\"noul\":0.94", "\"noul\":1e400", StringComparison.Ordinal)))), "NON_FINITE_NUMBER"));
Test("malformed JSON rejected", () => Reject(() => JevObservationReader.Load(Fixture(receiptBytes: _ => Encoding.UTF8.GetBytes("{"))), "INVALID_JSON"));
Test("unpaired surrogate rejected", () => Reject(() => JevObservationReader.Load(Fixture(candidateBytes: b =>
    Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("Inspect a staging service health report.", "\\ud800", StringComparison.Ordinal))))));
Test("depth above 16 rejected", () => Reject(() => JevObservationReader.Load(Fixture(receiptBytes: _ =>
    Encoding.UTF8.GetBytes(new string('[', 20) + "0" + new string(']', 20)))), "INVALID_JSON"));
Test("file exceeding 1MiB rejected before parsing", () => Reject(() => JevObservationReader.Load(Fixture(receiptBytes: _ => new byte[1_048_577])), "FILE_TOO_LARGE"));
Mutant("unknown manifest schema", (_, _, m) => m["schema"] = "v-next", "UNSUPPORTED_MANIFEST_SCHEMA");
Mutant("unknown manifest field", (_, _, m) => m["referenceAdmission"] = "ADMITTED", "UNKNOWN_OR_MISSING_FIELD");
Mutant("unknown artifact field", (_, _, m) => m["artifacts"]!["receipt"]!["url"] = "https://example.invalid", "UNKNOWN_OR_MISSING_FIELD");
Mutant("missing artifact field", (_, _, m) => m["artifacts"]!.AsObject().Remove("receipt"), "UNKNOWN_OR_MISSING_FIELD");
Mutant("unknown sample class", (_, _, m) => m["sampleClass"] = "REAL", "INVALID_SAMPLE_CLASS");
Mutant("case identifier cannot contain path or markup", (_, _, m) => m["caseId"] = "../<script>", "INVALID_CASE_ID");
Mutant("unknown candidate field", (c, _, _) => c["extra"] = false, "UNKNOWN_OR_MISSING_FIELD");
Mutant("unknown receipt field", (_, r, _) => r["extra"] = false, "UNKNOWN_OR_MISSING_FIELD");
Mutant("unknown state numeric extension unsupported", (c, _, _) => c["providerRequest"]!["state"]!["n"] = 1e-7, "UNKNOWN_OR_MISSING_FIELD");
Mutant("state number prohibited canonical domain", (c, _, _) => c["providerRequest"]!["state"]!["declaredIntent"] = 1e21, "TEXT_REQUIRED");
Mutant("candidate request tampering detected despite raw rehash", (c, _, _) => c["providerRequest"]!["state"]!["declaredIntent"] = "Other intent", "CANDIDATE_REQUEST_DIGEST_MISMATCH");
Mutant("candidate metadata tampering detected by receipt canonical digest", (c, _, _) => c["sourceShadowDigest"] = "sha256:" + new string('0', 64), "RECEIPT_CANDIDATE_DIGEST_MISMATCH");
Mutant("receipt request digest binding", (_, r, _) => r["candidateRequestDigest"] = "sha256:" + new string('0', 64), "RECEIPT_REQUEST_DIGEST_MISMATCH");
Mutant("adapter digest excludes requested model", (_, r, _) => r["adapterRequestDigest"] = r["candidateRequestDigest"]!.DeepClone(), "ADAPTER_REQUEST_DIGEST_MISMATCH");
Mutant("candidate provider request model binding", (c, _, _) => c["providerRequest"]!["model"] = "jev-other", "REQUEST_MODEL_MISMATCH");
Mutant("receipt requested model binding", (_, r, _) => r["requestedModel"] = "jev-other", "RECEIPT_REQUESTED_MODEL_MISMATCH");
Mutant("unknown provider rejected", (_, r, _) => r["provider"] = "untrusted", "PROVIDER_MODEL_IDENTITY_INVALID");
Mutant("synthetic provider cannot be labeled real", (_, _, m) => m["sampleClass"] = "SANITIZED_REAL_SHADOW", "PROVIDER_MODEL_IDENTITY_INVALID");
Mutant("synthetic observed model cannot be labeled TypeSafe", (_, r, _) => r["provider"] = "typesafe.jev", "PROVIDER_MODEL_IDENTITY_INVALID");
Mutant("empty observed model", (_, r, _) => r["observedModel"] = " ", "NONEMPTY_TEXT_REQUIRED");
foreach (var flag in new[] { "externalizationAuthorized", "providerInvocationAuthorized", "networkSendAuthorized", "decisionReadbackSupported" })
    Mutant("candidate boundary " + flag, (c, _, _) => c[flag] = true, "BOUNDARY_FLAG_INVALID");
foreach (var flag in new[] { "workbenchReadbackAuthorized", "authorityCreated", "displayPermitCreated", "actionPermitCreated" })
    Mutant("receipt boundary " + flag, (_, r, _) => r[flag] = true, "BOUNDARY_FLAG_INVALID");
Mutant("human review cannot be skipped", (c, _, _) => c["humanReviewRequired"] = false, "BOUNDARY_FLAG_INVALID");
Mutant("unconsumed receipt rejected", (_, r, _) => r["leaseConsumed"] = false, "BOUNDARY_FLAG_INVALID");
Mutant("authority effect rejected", (_, r, _) => r["normativeEffect"] = "ALLOW", "AUTHORITY_BOUNDARY_INVALID");
Mutant("operationalSpecificity cannot leave research only", (c, _, _) => c["questionUse"]!["operationalSpecificity"] = "SHADOW_OBSERVATION", "QUESTION_USE_MISMATCH");
Mutant("question instruction semantic substitution rejected", (c, _, _) => c["providerRequest"]!["questions"]!["goalAlignment"]!["instructions"] = "Should this action be permitted?", "UNSUPPORTED_QUESTION_RUBRIC");
Mutant("unknown question ID", (c, _, _) => c["providerRequest"]!["questions"]!["permit"] = new JsonObject(), "UNKNOWN_OR_MISSING_FIELD");
Mutant("missing judgment", (_, r, _) => r["judgments"]!.AsObject().Remove("externalCommunication"), "UNKNOWN_OR_MISSING_FIELD");
Mutant("additional judgment", (_, r, _) => r["judgments"]!["permit"] = new JsonObject(), "UNKNOWN_OR_MISSING_FIELD");
Mutant("judgment rubric binding", (_, r, _) => r["judgments"]!["goalAlignment"]!["instructions"] = "Other rubric", "JUDGMENT_RUBRIC_MISMATCH");
Mutant("noul outside unit interval", (_, r, _) => r["judgments"]!["goalAlignment"]!["answer"]!["noul"] = 1.01, "NUMBER_OUT_OF_RANGE");
Mutant("null noul is never coerced to false", (_, r, _) => r["judgments"]!["goalAlignment"]!["answer"]!["noul"] = null, "NUMBER_REQUIRED");
Mutant("boolean noul is never coerced to probability", (_, r, _) => r["judgments"]!["goalAlignment"]!["answer"]!["noul"] = false, "NUMBER_REQUIRED");
Mutant("unknown answer field", (_, r, _) => r["judgments"]!["goalAlignment"]!["answer"]!["truth"] = true, "UNKNOWN_OR_MISSING_FIELD");
Mutant("choice confidence outside unit interval", (_, r, _) => r["judgments"]!["targetSurface"]!["answer"]!["confidence"] = -0.1, "NUMBER_OUT_OF_RANGE");
Mutant("score confidence outside unit interval", (_, r, _) => r["judgments"]!["ambiguity"]!["answer"]!["confidence"] = 1.01, "NUMBER_OUT_OF_RANGE");
Mutant("score outside rubric", (_, r, _) => r["judgments"]!["ambiguity"]!["answer"]!["score"] = 4, "NUMBER_OUT_OF_RANGE");
Mutant("score legend substitution", (_, r, _) => r["judgments"]!["ambiguity"]!["answer"]!["legend"]!["0"] = "Allow", "SCORE_LEGEND_MISMATCH");
Mutant("distribution negative member", (_, r, _) => r["judgments"]!["targetSurface"]!["answer"]!["probabilities"]!["ci"] = -0.01, "NUMBER_OUT_OF_RANGE");
Mutant("distribution nonunit total", (_, r, _) => r["judgments"]!["targetSurface"]!["answer"]!["probabilities"]!["ci"] = 1, "INVALID_PROBABILITY_DISTRIBUTION");
Mutant("distribution unknown outcome", (_, r, _) => r["judgments"]!["targetSurface"]!["answer"]!["probabilities"]!["extra"] = 0, "UNKNOWN_OR_MISSING_FIELD");
Mutant("undeclared target choice", (_, r, _) => r["judgments"]!["targetSurface"]!["answer"]!["choice"] = "elsewhere", "UNDECLARED_CHOICE");
Mutant("negative token usage", (_, r, _) => r["usage"]!["input_tokens"] = -1, "NUMBER_OUT_OF_RANGE");
Mutant("fractional token usage", (_, r, _) => r["usage"]!["output_tokens"] = 0.5, "USAGE_INTEGER_REQUIRED");
Mutant("unknown token usage field", (_, r, _) => r["usage"]!["extra"] = 0, "UNKNOWN_OR_MISSING_FIELD");
foreach (var (field, code) in new[] { ("leaseDigest", "CONSUMPTION_LEASE_MISMATCH"), ("candidateDigest", "CONSUMPTION_CANDIDATE_MISMATCH"), ("requestDigest", "CONSUMPTION_REQUEST_MISMATCH") })
    Mutant("consumption binding " + field, (_, r, _) => r["consumptionRecord"]![field] = "sha256:" + new string('0', 64), code);
Mutant("consumption authority", (_, r, _) => r["consumptionRecord"]!["authorityEffect"] = "ALLOW", "CONSUMPTION_AUTHORITY_INVALID");
Mutant("consumption readback", (_, r, _) => r["consumptionRecord"]!["workbenchReadbackAuthorized"] = true, "BOUNDARY_FLAG_INVALID");
Mutant("consumption malformed chronology", (_, r, _) => r["consumptionRecord"]!["consumedAt"] = "yesterday", "INVALID_CONSUMPTION_TIME");
Mutant("unknown consumption field", (_, r, _) => r["consumptionRecord"]!["leaseActivated"] = true, "UNKNOWN_OR_MISSING_FIELD");
Test("relative manifest rejected", () => Reject(() => JevObservationReader.Load("inputs.json"), "ABSOLUTE_MANIFEST_PATH_REQUIRED"));
Test("UNC manifest rejected before read", () => Reject(() => JevObservationReader.Load(@"\\unreachable.invalid\share\inputs.json"), "NETWORK_OR_DEVICE_PATH_REJECTED"));
Test("slash UNC manifest rejected", () => Reject(() => JevObservationReader.Load("//unreachable.invalid/share/inputs.json"), "NETWORK_OR_DEVICE_PATH_REJECTED"));
Test("device path manifest rejected", () => Reject(() => JevObservationReader.Load(@"\\?\C:\inputs.json"), "NETWORK_OR_DEVICE_PATH_REJECTED"));
Mutant("UNC artifact rejected before read", (_, _, m) => m["artifacts"]!["candidate"]!["path"] = @"\\unreachable.invalid\share\candidate.json", "NETWORK_OR_DEVICE_PATH_REJECTED");
Mutant("URL artifact rejected", (_, _, m) => m["artifacts"]!["candidate"]!["path"] = "https://example.invalid/candidate.json", "NETWORK_OR_DEVICE_PATH_REJECTED");
Test("missing file produces safe code", () => Reject(() => JevObservationReader.Load(Path.Combine(temporaryRoot, "missing.json")), "INPUT_READ_FAILED"));
if (OperatingSystem.IsWindows())
    Mutant("alternate data stream rejected", (_, _, m) => m["artifacts"]!["candidate"]!["path"] = "candidate.json:stream", "DEVICE_OR_ALTERNATE_STREAM_REJECTED");
Test("symlink file and ancestor rejected", () =>
{
    var p = Fixture(); var directory = Path.GetDirectoryName(p)!;
    var fileLink = Path.Combine(temporaryRoot, "linked-inputs.json");
    var dirLink = Path.Combine(temporaryRoot, "linked-directory");
    try
    {
        File.CreateSymbolicLink(fileLink, p);
        Directory.CreateSymbolicLink(dirLink, directory);
        Reject(() => JevObservationReader.Load(fileLink), "LINK_OR_REPARSE_PATH_REJECTED");
        Reject(() => JevObservationReader.Load(Path.Combine(dirLink, "inputs.json")), "LINK_OR_REPARSE_PATH_REJECTED");
    }
    finally
    {
        if (File.Exists(fileLink)) File.Delete(fileLink);
        if (Directory.Exists(dirLink)) Directory.Delete(dirLink);
    }
});

var failures = 0;
try
{
    foreach (var test in tests)
    {
        try { test.Run(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
    }
    Console.WriteLine($"RESULT {tests.Count - failures}/{tests.Count} passed; {failures} failed");
}
finally
{
    // Only delete this exact independently created test root after its parent/name validation.
    var resolved = Path.GetFullPath(temporaryRoot);
    if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) &&
        Path.GetFileName(resolved).StartsWith("matawaka-jev-lab-tests-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
}
return failures == 0 ? 0 : 1;

static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
static string FindPackage()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "fixtures", "read-only", "inputs.json"))) return directory.FullName;
    throw new Exception("Pass the absolute jev-workbench-experiment-v001 package directory");
}
