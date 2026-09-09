using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Matawaka.V0601PublicationPreflight;

namespace Matawaka.V0601DiscoveryDiagnostic;

internal sealed record PipeObservation(int CapturedBytes, bool EndOfStream, bool LimitExceeded);
internal sealed record Capture(int? ExitCode, bool Started, bool Exited, bool TimedOut,
    bool CleanupIncomplete, string Failure, byte[] Output, byte[] Error,
    PipeObservation Stdout, PipeObservation Stderr);
internal sealed record ListedRefs(string? Main, string? Tag, string? Peeled, string? Historical,
    string? HistoricalPeeled, int ObservedRefCount);
internal sealed record ProbeObservation(string Category, int? ExitCode, bool ProcessStarted,
    bool ProcessExited, bool TimedOut, bool CleanupIncomplete, PipeObservation Stdout,
    PipeObservation Stderr, ListedRefs? Refs, bool RawOutputPersisted = false,
    bool PublicationAuthorized = false, bool HistoricalCauseRecovered = false);

// This runner has no knowledge of Git write commands. Its only production caller supplies
// a fixed absolute remote-https image, two fixed endpoint arguments, and a fixed list request.
internal static class BoundedProcess
{
    private sealed class Pipe
    {
        private readonly object gate = new();
        private readonly MemoryStream data = new();
        private bool eof, limit;
        internal (byte[], PipeObservation) Snapshot()
        {
            lock (gate) return (data.ToArray(), new((int)data.Length, eof, limit));
        }
        internal async Task Drain(Stream stream, int max, CancellationToken stop,
            TaskCompletionSource<string> fault)
        {
            var buffer = new byte[8192];
            try {
                while (true) {
                    var n = await stream.ReadAsync(buffer, stop);
                    if (n == 0) { lock (gate) eof = true; return; }
                    lock (gate) {
                        var keep = Math.Min(n, max - (int)data.Length);
                        data.Write(buffer, 0, keep);
                        if (keep != n) { limit = true; fault.TrySetResult("OUTPUT_LIMIT"); return; }
                    }
                }
            } catch (OperationCanceledException) { }
              catch (Exception e) when (e is IOException or ObjectDisposedException) {
                  fault.TrySetResult("PIPE_READ_FAILED");
              }
        }
    }
    internal static async Task<Capture> Run(ProcessStartInfo psi, byte[] input,
        int maximum = 512 * 1024, int deadlineMs = 30000)
    {
        Safe.Need(maximum > 0 && maximum <= 512 * 1024 && deadlineMs >= 100 && deadlineMs <= 30000,
            "PROBE_BOUNDS_REFUSED");
        Safe.Need(!psi.UseShellExecute && psi.RedirectStandardInput && psi.RedirectStandardOutput &&
            psi.RedirectStandardError, "PROBE_PIPE_CONTRACT_REFUSED");
        using var process = new Process { StartInfo = psi };
        using var cancellation = new CancellationTokenSource();
        var fault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = new Pipe(); var stderr = new Pipe();
        bool started = false, exited = false, timedOut = false, cleanupIncomplete = false;
        int? exit = null; string failure = "NONE";
        Task[] tasks = Array.Empty<Task>();
        try {
            try { started = process.Start(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) {
                failure = "PROCESS_START_FAILED";
            }
            if (!started) {
                failure = "PROCESS_START_FAILED";
            } else {
                async Task WriteInput()
                {
                    try { await process.StandardInput.BaseStream.WriteAsync(input, cancellation.Token); }
                    catch (OperationCanceledException) { }
                    catch (IOException) { fault.TrySetResult("PIPE_WRITE_FAILED"); }
                    finally { try { process.StandardInput.Close(); } catch (IOException) { } }
                }
                tasks = new[] {
                    stdout.Drain(process.StandardOutput.BaseStream, maximum, cancellation.Token, fault),
                    stderr.Drain(process.StandardError.BaseStream, maximum, cancellation.Token, fault),
                    WriteInput(), process.WaitForExitAsync(cancellation.Token)
                };
                var all = Task.WhenAll(tasks);
                var deadline = Task.Delay(deadlineMs);
                var first = await Task.WhenAny(all, fault.Task, deadline);
                if (fault.Task.IsCompleted) failure = await fault.Task;
                else if (first == deadline) { failure = "PROCESS_DEADLINE"; timedOut = true; }
                else {
                    try { await all; }
                    catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException) {
                        failure = "PROCESS_OR_PIPE_FAILED";
                    }
                }
                if (failure != "NONE") {
                    cancellation.Cancel();
                    // No unbounded wait in cleanup. A failure to terminate remains explicit.
                    var cleanup = Task.Run(async () => {
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                        catch (Exception e) when (e is TimeoutException or InvalidOperationException) { }
                    });
                    try { await cleanup.WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { cleanupIncomplete = true; }
                    try { process.StandardOutput.Close(); process.StandardError.Close(); }
                    catch (IOException) { }
                    try { await all.WaitAsync(TimeSpan.FromSeconds(2)); }
                    catch (Exception e) when (e is TimeoutException or OperationCanceledException or IOException or InvalidOperationException) {
                        if (!all.IsCompleted) cleanupIncomplete = true;
                    }
                }
                try { exited = process.HasExited; if (exited) exit = process.ExitCode; }
                catch (InvalidOperationException) { }
                if (!exited) cleanupIncomplete = true;
            }
        } finally { psi.Environment.Clear(); }
        var o = stdout.Snapshot(); var eout = stderr.Snapshot();
        return new(exit, started, exited, timedOut, cleanupIncomplete, failure,
            o.Item1, eout.Item1, o.Item2, eout.Item2);
    }
}

internal static class Classification
{
    // Allowlisted categories only: never copy a server line or exception message to a receipt.
    internal static string Error(byte[] bytes)
    {
        string s;
        try { s = Safe.Utf8.GetString(bytes).ToLowerInvariant(); }
        catch (DecoderFallbackException) { return "UNRECOGNIZED_NATIVE_ERROR"; }
        var categories = new HashSet<string>(StringComparer.Ordinal);
        void Match(string category, params string[] fragments) {
            if (fragments.Any(s.Contains)) categories.Add(category);
        }
        Match("AUTHENTICATION_REJECTED", "authentication failed", "returned error: 401");
        Match("AUTHENTICATION_CHALLENGE_UNSATISFIED", "unable to get password from user", "could not read username", "could not read password");
        Match("ACCESS_FORBIDDEN", "returned error: 403", "write access to repository not granted");
        Match("REPOSITORY_NOT_FOUND_OR_NOT_VISIBLE", "returned error: 404", "repository not found");
        Match("RATE_LIMITED", "returned error: 429");
        Match("REDIRECT_REFUSED", "returned error: 301", "returned error: 302", "returned error: 307", "returned error: 308", "unable to update url base from redirection");
        Match("TLS_FAILED", "ssl certificate problem", "tls connect error", "ssl connect error", "certificate verify failed", "error setting certificate file");
        Match("DNS_FAILED", "could not resolve host");
        Match("CONNECTION_FAILED", "failed to connect to", "connection refused");
        Match("NETWORK_TIMEOUT", "operation timed out", "connection timed out");
        Match("REMOTE_CONNECTION_CLOSED", "empty reply from server", "connection reset by peer");
        Match("ATOMIC_UNSUPPORTED", "does not support --atomic push", "does not support atomic push");
        Match("HOOK_START_FAILED", "cannot spawn", "cannot run", "cannot exec");
        Match("HELPER_PROTOCOL_FAILED", "invalid server response", "invalid ref advertisement", "bad line length character", "protocol error:");
        return categories.Count == 1 ? categories.Single() : categories.Count > 1 ? "AMBIGUOUS_NATIVE_ERROR" : "UNRECOGNIZED_NATIVE_ERROR";
    }
    internal static ListedRefs Parse(byte[] bytes)
    {
        var text = Safe.Utf8.GetString(bytes);
        Safe.Need(!text.Contains('\r') && text.EndsWith("\n\n", StringComparison.Ordinal), "HELPER_OUTPUT_INCOMPLETE");
        var split = text.IndexOf("\n\n", StringComparison.Ordinal);
        Safe.Need(split > 0, "HELPER_CAPABILITIES_MISSING");
        var caps = text[..split].Split('\n');
        Safe.Need(caps.Contains("push") && caps.Length <= 32 && caps.Distinct().Count() == caps.Length &&
            caps.All(c => Regex.IsMatch(c, @"\A\*?[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant)), "HELPER_CAPABILITIES_REFUSED");
        var body = text[(split + 2)..];
        Safe.Need(body.EndsWith('\n'), "HELPER_LIST_INCOMPLETE");
        var rows = body.Split('\n'); var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Length - 2; i++) {
            var f = rows[i].Split(' ');
            Safe.Need(f.Length == 2 && (Safe.Oid(f[0]) || f[0].StartsWith("@refs/", StringComparison.Ordinal)) &&
                (f[1] == "HEAD" || f[1].StartsWith("refs/", StringComparison.Ordinal)) &&
                !f[1].Any(char.IsControl) && map.TryAdd(f[1], f[0]), "HELPER_REF_OUTPUT_REFUSED");
            Safe.Need(map.Count <= 4096, "HELPER_REF_COUNT_LIMIT");
        }
        Safe.Need(rows.Length >= 2 && rows[^1] == "" && rows[^2] == "", "HELPER_LIST_TERMINATOR_REFUSED");
        string? Get(string key) { var v = map.GetValueOrDefault(key); Safe.Need(v is null || Safe.Oid(v), "TARGET_REF_NOT_OID"); return v; }
        return new(Get("refs/heads/main"), Get("refs/tags/workbench-v0.60.1-accepted"),
            Get("refs/tags/workbench-v0.60.1-accepted^{}"), Get("refs/tags/workbench-v0.60-accepted"),
            Get("refs/tags/workbench-v0.60-accepted^{}"), map.Count);
    }
    internal static ProbeObservation Observe(Capture r)
    {
        string category; ListedRefs? refs = null;
        if (r.Failure != "NONE") category = r.Failure;
        else if (!r.Exited || !r.Stdout.EndOfStream || !r.Stderr.EndOfStream) category = "CAPTURE_INCOMPLETE";
        else if (r.ExitCode != 0) category = Error(r.Error);
        else {
            try { refs = Parse(r.Output); category = "RECEIVE_DISCOVERY_COMPLETED"; }
            catch (Exception e) when (e is InvalidDataException or DecoderFallbackException) { category = "HELPER_PROTOCOL_FAILED"; }
        }
        return new(category, r.ExitCode, r.Started, r.Exited, r.TimedOut, r.CleanupIncomplete, r.Stdout, r.Stderr, refs);
    }
}

internal static class DiscoveryTransport
{
    internal static readonly byte[] Input = Encoding.ASCII.GetBytes("capabilities\nlist for-push\n\n");
    internal static ProcessStartInfo Start(string gitRoot, string stage, string endpoint, string header)
    {
        Safe.Need(endpoint == Spec.Remote, "FIXED_DISCOVERY_ENDPOINT_REQUIRED");
        return Create(gitRoot, stage, endpoint, header, null);
    }
    private static ProcessStartInfo Create(string gitRoot, string stage, string endpoint, string header, string? testCa)
    {
        Safe.Need(header.StartsWith("Authorization: Basic ", StringComparison.Ordinal) && !header.Any(char.IsControl), "CREDENTIAL_HEADER_REFUSED");
        var helper = Path.Combine(gitRoot, "mingw64", "bin", "git-remote-https.exe");
        var home = Path.Combine(stage, "empty-home"); var repo = Path.Combine(stage, "discovery.git");
        var p = new ProcessStartInfo(helper) { WorkingDirectory = repo, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        p.Environment.Clear();
        foreach (var k in new[] { "SystemRoot", "WINDIR" }) if (Environment.GetEnvironmentVariable(k) is { } value) p.Environment[k] = value;
        p.Environment["PATH"] = string.Join(Path.PathSeparator, Path.Combine(gitRoot, "cmd"), Path.Combine(gitRoot, "mingw64", "bin"), Path.Combine(gitRoot, "usr", "bin"), Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "System32"));
        foreach (var (k, v) in new[] { ("HOME", home), ("USERPROFILE", home), ("XDG_CONFIG_HOME", home), ("TEMP", home), ("TMP", home), ("GIT_DIR", repo), ("GIT_CONFIG_NOSYSTEM", "1"), ("GIT_CONFIG_GLOBAL", "NUL"), ("GIT_TERMINAL_PROMPT", "0"), ("GIT_NO_LAZY_FETCH", "1"), ("GIT_NO_REPLACE_OBJECTS", "1"), ("GIT_OPTIONAL_LOCKS", "0"), ("GIT_PROTOCOL_FROM_USER", "0"), ("GIT_ALLOW_PROTOCOL", "https"), ("LC_ALL", "C") }) p.Environment[k] = v;
        p.Environment["GIT_EXEC_PATH"] = Path.Combine(gitRoot, "mingw64", "bin");
        var config = new List<(string, string)> { ("credential.helper", ""), ("credential.interactive", "false"), ("core.hooksPath", home.Replace('\\', '/')), ("core.fsmonitor", "false"), ("core.commitGraph", "false"), ("gc.auto", "0"), ("maintenance.auto", "false"), ("protocol.allow", "never"), ("protocol.https.allow", "always"), ("protocol.version", "0"), ("http.followRedirects", "false"), ("http.sslVerify", "true"), ("http.sslBackend", "openssl"), ("http.sslCAInfo", testCa ?? Path.Combine(gitRoot, "mingw64", "etc", "ssl", "certs", "ca-bundle.crt")), ("http.proxy", ""), ("http.maxRetries", "0"), ("http.retryAfter", "0"), ("http.maxRetryTime", "0"), ("http.lowSpeedLimit", "1"), ("http.lowSpeedTime", "20"), ("http." + endpoint + ".extraHeader", header) };
        p.Environment["GIT_CONFIG_COUNT"] = config.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < config.Count; i++) { p.Environment["GIT_CONFIG_KEY_" + i] = config[i].Item1; p.Environment["GIT_CONFIG_VALUE_" + i] = config[i].Item2; }
        p.ArgumentList.Add(endpoint); p.ArgumentList.Add(endpoint);
        return p;
    }
#if QUALIFICATION
    internal static ProcessStartInfo Fixture(string gitRoot, string stage, string endpoint, string header, string? ca)
    {
        Safe.Need(Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "127.0.0.1", "LOOPBACK_FIXTURE_ONLY");
        return Create(gitRoot, stage, endpoint, header, ca);
    }
#endif
}
