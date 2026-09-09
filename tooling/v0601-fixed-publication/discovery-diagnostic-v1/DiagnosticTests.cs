using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;

namespace Matawaka.V0601DiscoveryDiagnostic;

internal static class DiagnosticTests
{
    private const string Dummy = "github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_DIAGNOSTIC_FIXTURE";
    private const string Old = "ac083598711caa0c399cc0d2c385b980c083024a";
    private static readonly List<object> Results = new();
    private static string GitRoot => Environment.GetEnvironmentVariable("DIAGNOSTIC_TEST_GIT_ROOT") ?? throw new InvalidDataException("TEST_GIT_ROOT_MISSING");
    private static string Git => Path.Combine(GitRoot, "cmd", "git.exe");
    private static void Check(bool ok, string id) { Safe.Need(ok, "TEST_FAILED_" + id); }
    private static void Pass(string id) { Results.Add(new { Id = id, Passed = true }); Console.WriteLine("PASS " + id); }
    private static string Temp() { var p = Path.Combine(Path.GetTempPath(), "v0601-discovery-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static void Remove(string p) { try { Directory.Delete(p, true); } catch (IOException) { } }
    private static bool Refuses(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } }
    private static byte[] Packet(string s) { var b = Encoding.ASCII.GetBytes(s); return Encoding.ASCII.GetBytes((b.Length + 4).ToString("x4")).Concat(b).ToArray(); }
    private static byte[] Advertisement(string service) => Packet("# service=git-" + service + "\n").Concat("0000"u8.ToArray()).Concat(Packet(Old + " refs/heads/main\0report-status delete-refs side-band-64k quiet atomic ofs-delta agent=fixture\n")).Concat("0000"u8.ToArray()).ToArray();
    private sealed class Server : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly RSA key = RSA.Create(2048);
        private readonly X509Certificate2 cert;
        private readonly Task loop;
        private readonly string mode;
        internal readonly List<string> Methods = new();
        internal int Requests, Posts, Leaks, WrongPaths, TlsFailures;
        internal string Endpoint { get; }
        internal string Ca => cert.ExportCertificatePem();
        internal Server(string behavior)
        {
            mode = behavior;
            var req = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); req.CertificateExtensions.Add(san.Build());
            using var generated = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
            listener.Start(); Endpoint = "https://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/Matawaka/workbench.git";
            loop = Loop();
        }
        private async Task<string> Line(Stream stream)
        {
            var bytes = new List<byte>(); var b = new byte[1];
            while (bytes.Count < 16384) {
                var n = await stream.ReadAsync(b, stop.Token); if (n == 0) throw new IOException(); bytes.Add(b[0]);
                if (bytes.Count > 1 && bytes[^2] == 13 && bytes[^1] == 10) return Encoding.ASCII.GetString(bytes.Take(bytes.Count - 2).ToArray());
            }
            throw new IOException();
        }
        private async Task Loop()
        {
            while (!stop.IsCancellationRequested) {
                try {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    using var ssl = new SslStream(client.GetStream(), false);
                    try { await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, ClientCertificateRequired = false }, stop.Token); }
                    catch (System.Security.Authentication.AuthenticationException) { TlsFailures++; continue; }
                    var line = await Line(ssl); var words = line.Split(' '); var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string h; while ((h = await Line(ssl)).Length > 0) { var at = h.IndexOf(':'); if (at < 1 || headers.Count > 100) throw new IOException(); headers[h[..at]] = h[(at + 1)..].Trim(); }
                    Requests++; Methods.Add(words[0]); if (words[0] != "GET") Posts++;
                    if (words[1] != "/Matawaka/workbench.git/info/refs?service=git-receive-pack") WrongPaths++;
                    if (headers.GetValueOrDefault("Authorization") != Credential.Header(Dummy)["Authorization: ".Length..]) Leaks++;
                    if (mode == "slow") { await Task.Delay(10000, stop.Token); continue; }
                    if (mode == "close") continue;
                    string status = "200 OK", type = "application/x-git-receive-pack-advertisement", extra = "";
                    byte[] body = Advertisement("receive-pack");
                    if (mode == "401") { status = "401 Unauthorized"; extra = "WWW-Authenticate: Basic realm=fixture\r\n"; body = Array.Empty<byte>(); }
                    if (mode == "403") { status = "403 Forbidden"; body = Encoding.ASCII.GetBytes("arbitrary secret echo " + Dummy); }
                    if (mode == "404") { status = "404 Not Found"; body = Array.Empty<byte>(); }
                    if (mode == "429") { status = "429 Too Many Requests"; extra = "Retry-After: 0\r\n"; body = Array.Empty<byte>(); }
                    if (mode == "redirect") { status = "302 Found"; extra = "Location: " + Endpoint + "/leak\r\n"; body = Array.Empty<byte>(); }
                    if (mode == "malformed") body = "0123not-a-valid-ref"u8.ToArray();
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n" + extra + "\r\n");
                    await ssl.WriteAsync(head, stop.Token); await ssl.WriteAsync(body, stop.Token); await ssl.FlushAsync(stop.Token);
                } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException) { }
            }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); try { loop.Wait(2000); } catch (AggregateException) { } cert.Dispose(); key.Dispose(); stop.Dispose(); }
    }
    private static async Task Https(string mode, string expected, bool trusted = true)
    {
        var temp = Temp();
        try {
            using var s = new Server(mode); var stage = Path.Combine(temp, "stage"); Diagnostic.CreateStage(stage);
            var ca = Path.Combine(temp, "ca.pem"); File.WriteAllText(ca, s.Ca, new UTF8Encoding(false));
            using var locks = new ReadLocks(); ToolTree.Verify(GitRoot, ToolTree.Embedded(), locks); locks.Tree(stage);
            var before = Files.Digest(stage);
            var psi = DiscoveryTransport.Fixture(GitRoot, stage, s.Endpoint, Credential.Header(Dummy), trusted ? ca : null);
            var capture = await BoundedProcess.Run(psi, DiscoveryTransport.Input, deadlineMs: mode == "slow" ? 700 : 10000);
            var result = Classification.Observe(capture);
            // Dummy fixture output only; production never prints raw output.
            if (result.Category != expected) Console.WriteLine("FIXTURE_MISMATCH " + mode + " category=" + result.Category + " stderr=" + Encoding.UTF8.GetString(capture.Error));
            Check(result.Category == expected, "https-category-" + mode);
            Check(s.Posts == 0 && s.WrongPaths == 0 && s.Leaks == 0, "no-update-post-or-credential-redirection-" + mode);
            Check(s.Requests == (trusted ? 1 : 0), "one-discovery-get-no-http-retry-" + mode);
            Check(Files.Digest(stage) == before, "empty-stage-byte-preservation-" + mode);
            var json = Encoding.UTF8.GetString(Files.Json(result));
            Check(!json.Contains(Dummy) && !json.Contains(Convert.ToBase64String(Encoding.ASCII.GetBytes("x-access-token:" + Dummy))), "secret-free-json-" + mode);
            if (mode == "normal" && trusted) Check(result.Refs?.Main == Old && result.Refs.ObservedRefCount == 1, "exact-native-helper-ref-parsing");
            Results.Add(new { Id = "native-https-" + mode + (trusted ? "" : "-untrusted"), Passed = true, Category = result.Category, Requests = s.Requests, UpdatePostRequests = s.Posts, CredentialLeaks = s.Leaks, OnlyLoopback = true });
            Console.WriteLine("PASS native-https-" + mode + " " + trusted);
        } finally { Remove(temp); }
    }
    private static async Task PipeTest(string mode, string expected)
    {
        var p = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        p.ArgumentList.Add("--child=" + mode); var clock = Stopwatch.StartNew();
        var r = await BoundedProcess.Run(p, Array.Empty<byte>(), maximum: 65536, deadlineMs: 500);
        Check(r.Failure == expected && clock.Elapsed < TimeSpan.FromSeconds(7), "pipe-bound-" + mode);
        if (mode is "flood-out" or "flood-error") Check(r.Stdout.LimitExceeded || r.Stderr.LimitExceeded, "pipe-limit-observed");
        if (mode == "dual") Check(r.Output.Length == 32768 && r.Error.Length == 32768 && r.Stdout.EndOfStream && r.Stderr.EndOfStream, "dual-drain-complete");
        Pass("bounded-process-" + mode);
    }
    private static async Task<int> Child(string mode)
    {
        if (mode == "sleep") { await Task.Delay(10000); return 0; }
        if (mode == "inherit") {
            var p = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; p.ArgumentList.Add("--child=hold"); Process.Start(p)!.Dispose(); return 0;
        }
        if (mode == "hold") { await Task.Delay(1600); return 0; }
        var b = Enumerable.Repeat((byte)'x', mode == "dual" ? 32768 : 200000).ToArray();
        if (mode is "dual" or "flood-out") await Console.OpenStandardOutput().WriteAsync(b);
        if (mode is "dual" or "flood-error") await Console.OpenStandardError().WriteAsync(b);
        return 0;
    }
    private static void Pure()
    {
        foreach (var (message, expected) in new[] {
            ("fatal: Authentication failed for exact", "AUTHENTICATION_REJECTED"),
            ("fatal: unable to get password from user", "AUTHENTICATION_CHALLENGE_UNSATISFIED"),
            ("fatal: requested URL returned error: 403", "ACCESS_FORBIDDEN"),
            ("fatal: repository not found", "REPOSITORY_NOT_FOUND_OR_NOT_VISIBLE"),
            ("fatal: requested URL returned error: 429", "RATE_LIMITED"),
            ("TLS connect error: details", "TLS_FAILED"), ("could not resolve host: x", "DNS_FAILED"),
            ("failed to connect to x", "CONNECTION_FAILED"), ("operation timed out", "NETWORK_TIMEOUT"),
            ("server does not support --atomic push", "ATOMIC_UNSUPPORTED"), ("cannot spawn hook", "HOOK_START_FAILED"),
            ("SSL certificate problem / Authentication failed", "AMBIGUOUS_NATIVE_ERROR"), (Dummy, "UNRECOGNIZED_NATIVE_ERROR") }) {
            Check(Classification.Error(Encoding.UTF8.GetBytes(message)) == expected, "allowlisted-classifier"); Pass("classifier-" + expected);
        }
        string valid = "fetch\noption\npush\ncheck-connectivity\nobject-format\nstateless-connect\n\n" + Old + " refs/heads/main\n\n";
        Check(Classification.Parse(Encoding.ASCII.GetBytes(valid)).Main == Old, "parser-happy"); Pass("helper-output-parsed");
        Check(Refuses(() => Classification.Parse(Encoding.ASCII.GetBytes(valid.TrimEnd('\n')))), "truncated"); Pass("helper-truncation-refused");
        Check(Refuses(() => Classification.Parse(Encoding.ASCII.GetBytes(valid.Replace(Old + " refs/heads/main\n", Old + " refs/heads/main\n" + Old + " refs/heads/main\n")))), "duplicate"); Pass("duplicate-ref-refused");
        Check(Refuses(() => Diagnostic.Confirm("PUBLISH-EXACT-V0601-58B9430-C07409C", TimeSpan.Zero)), "old-confirmation"); Pass("old-publication-confirmation-not-read-authority");
        Check(Refuses(() => Diagnostic.Confirm(DiagnosticPlan.Confirmation, TimeSpan.FromMinutes(6))), "expiry"); Pass("expired-confirmation-refused");
        Check(Encoding.ASCII.GetString(DiscoveryTransport.Input) == "capabilities\nlist for-push\n\n", "fixed-input"); Pass("closed-helper-input-no-update-command");
        Check(Refuses(() => DiscoveryTransport.Start(GitRoot, "unused", "https://example.org/", Credential.Header(Dummy))), "endpoint"); Pass("arbitrary-endpoint-refused");
        var info = DiscoveryTransport.Start(GitRoot, "unused", Spec.Remote, Credential.Header(Dummy));
        Check(info.ArgumentList.Count == 2 && info.ArgumentList.All(x => x == Spec.Remote) && !info.Arguments.Contains(Dummy), "arg-closure");
        Check(!info.Environment.ContainsKey("GITHUB_TOKEN") && !info.Environment.ContainsKey("GIT_TRACE") && !info.Environment.ContainsKey("HTTPS_PROXY"), "ambient-env");
        var cfg = Enumerable.Range(0, int.Parse(info.Environment["GIT_CONFIG_COUNT"]!)).ToDictionary(i => info.Environment["GIT_CONFIG_KEY_" + i]!, i => info.Environment["GIT_CONFIG_VALUE_" + i]!);
        Check(cfg["http.sslVerify"] == "true" && cfg["http.followRedirects"] == "false" && cfg["credential.helper"] == "" && cfg["http.maxRetries"] == "0", "guard-config");
        Pass("credential-env-only-no-helper-no-redirect-no-retry");
    }
    private static async Task Orchestration()
    {
        using var f = new V2Tests.F(); var temp = Temp();
        try {
            var snap = await f.Read();
            BoundFile Save(string n, object x) { var b = Files.Json(x); var rel = "artifacts/publication-v0601/" + n; Files.New(Safe.Under(f.Root, rel), b); return new(rel, Safe.Hash(b), b.Length); }
            var pre = Save("pre.json", new { Schema = "matawaka.workbench-v0601-publication-preflight/v0.2", Status = V2.Status, Snapshot = snap, PublicationAuthorityCreated = false, RetryAuthorityCreated = false, NetworkReadPerformed = false, RemoteWritePerformed = false });
            var a = Save("a.json", new { Schema = "matawaka.workbench-fixed-publication-attempt/v0.1", State = "ATTEMPT_CONSUMED_NO_RETRY", PreflightSha256 = pre.Sha256, AcceptedHead = f.Proof.Accepted.Head, AcceptedTagObject = f.Proof.TagObject, RetryAuthorized = false });
            var o = Save("o.json", new { Schema = "matawaka.workbench-fixed-publication-outcome/v0.1", Status = "PUBLICATION_OUTCOME_UNVERIFIED_NO_RETRY", SafeReason = "PUSH_OR_READBACK_NOT_VERIFIED", AttemptReceiptSha256 = a.Sha256, PreflightSha256 = pre.Sha256, RetryAuthorized = false, PublicationSuccessClaimed = false, RemoteMutationProvenAbsent = false, ExactAdvertisedOldMainGuardObserved = false, PushMayHaveStarted = true, LocalSnapshotReverified = true, PushExitCode = 128, PushInvocations = 1, RemoteReadInvocations = 2 });
            var oldStage = Path.Combine(temp, "old"); Directory.CreateDirectory(oldStage); File.WriteAllText(Path.Combine(oldStage, "session.json"), "{}");
            var p = new DiagnosticPlan(f.Proof, GitRoot, Path.Combine(temp, "new"), oldStage, Path.Combine(f.Root, "artifacts/publication-v0601"), pre, a, o);
            var g = new GitRead(Git, f.Root); var baseline = await Incident.Observe(p, g); int calls = 0;
            var priorDigest = Files.Digest(oldStage); var store = V2.StoreDigest(f.Root);
            var error = Encoding.ASCII.GetBytes("fatal: Authentication failed " + Dummy);
            Task<Capture> Probe() { calls++; return Task.FromResult(new Capture(128, true, true, false, false, "NONE", Array.Empty<byte>(), error, new(0, true, false), new(error.Length, true, false))); }
            bool rejected = false;
            try { await Diagnostic.Execute(p, baseline, () => Incident.Observe(p, g), "WRONG", TimeSpan.Zero, Probe, "fixture", "fixture"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && calls == 0 && !File.Exists(p.Attempt) && !Directory.Exists(p.Stage), "cancel-zero-effects"); Pass("cancel-before-attempt-or-network");
            var status = await Diagnostic.Execute(p, baseline, () => Incident.Observe(p, g), DiagnosticPlan.Confirmation, TimeSpan.Zero, Probe, "fixture", "fixture");
            var result = Safe.Parse(Safe.Read(p.Result));
            Check(status == "RECEIVE_DISCOVERY_DIAGNOSTIC_RECORDED_NO_PUSH" && calls == 1, "one-observation");
            Safe.Eq(result.GetProperty("Probe"), "Category", "AUTHENTICATION_REJECTED");
            Safe.False(result, "PublicationAuthorized", "RetryAuthorized", "PriorAttemptRearmed", "RawOutputPersisted", "SecretDerivedDigestsPersisted");
            Safe.True(result, "LocalSnapshotAndPriorTransportReverified", "NewDiagnosticStageUnchanged");
            Check(Incident.Read(p) == snap && V2.StoreDigest(f.Root) == store && Files.Digest(oldStage) == priorDigest, "old-evidence-preserved");
            Check(!Encoding.UTF8.GetString(Safe.Read(p.Result)).Contains(Dummy), "secret-omitted");
            Pass("incident-linked-result-keeps-old-attempt-and-source-unchanged");
            Check(Refuses(() => Diagnostic.Unused(p)), "no-replay"); Pass("diagnostic-attempt-not-reusable");
            File.AppendAllText(Safe.Under(f.Root, o.RelativePath), " ");
            Check(Refuses(() => Incident.Read(p)), "incident-tamper"); Pass("original-outcome-byte-tamper-refused");
        } finally { Remove(temp); }
    }
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0].StartsWith("--child=", StringComparison.Ordinal)) return await Child(args[0][8..]);
        Check(args.Length == 0, "tests-zero-args");
        Pure();
        await PipeTest("dual", "NONE"); await PipeTest("flood-out", "OUTPUT_LIMIT"); await PipeTest("flood-error", "OUTPUT_LIMIT");
        await PipeTest("sleep", "PROCESS_DEADLINE"); await PipeTest("inherit", "PROCESS_DEADLINE");
        await Https("normal", "RECEIVE_DISCOVERY_COMPLETED");
        await Https("401", "AUTHENTICATION_CHALLENGE_UNSATISFIED"); await Https("403", "ACCESS_FORBIDDEN");
        await Https("404", "REPOSITORY_NOT_FOUND_OR_NOT_VISIBLE"); await Https("429", "RATE_LIMITED");
        await Https("redirect", "REDIRECT_REFUSED"); await Https("normal", "TLS_FAILED", false);
        await Https("malformed", "HELPER_PROTOCOL_FAILED"); await Https("slow", "PROCESS_DEADLINE");
        await Orchestration();
        File.WriteAllBytes("discovery-diagnostic-qualification.json", Files.Json(new { Schema = "matawaka.receive-discovery-diagnostic-qualification/v0.1", Passed = true, Checks = Results, ProductionGitHubContacted = false, OperatorHostUsed = false, LiveTokensUsed = false, ProductionPushPerformed = false, NativeGitRemoteHttpsUsed = true, HttpsFixturesLoopbackOnly = true }));
        Console.WriteLine("DISCOVERY_DIAGNOSTIC_QUALIFICATION_PASS " + Results.Count); return 0;
    }
}
