using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;
using Matawaka.V0601DiscoveryDiagnostic;

namespace Matawaka.V0601PermissionRecheck;

internal static class PermissionRecheckTests
{
    private static readonly List<object> Checks = new();
    private static void Pass(bool ok, string id) { Safe.Need(ok, "RECHECK_TEST_FAILED_" + id); Checks.Add(new { Id = id, Passed = true }); Console.WriteLine("PASS " + id); }
    private static bool Refused(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } }
    private sealed class F : IDisposable
    {
        internal readonly V2Tests.F Base = new();
        internal readonly string Temp = Path.Combine(Path.GetTempPath(), "wb-recheck-tests-" + Guid.NewGuid().ToString("N"));
        internal RecheckPlan Plan = null!;
        internal V2Snapshot Snapshot = null!;
        internal GitRead Git => new(GitRead.Locate(), Base.Root);
        internal BoundFile Save(string name, object value) {
            var relative = "artifacts/publication-v0601/" + name; var bytes = Files.Json(value);
            Files.New(Safe.Under(Base.Root, relative), bytes); return new(relative, Safe.Hash(bytes), bytes.Length);
        }
        internal async Task Init() {
            Snapshot = await Base.Read(); Directory.CreateDirectory(Temp);
            var pre = Save("pre.json", new { Schema = "matawaka.workbench-v0601-publication-preflight/v0.2", Status = V2.Status, Snapshot,
                PublicationAuthorityCreated = false, RetryAuthorityCreated = false, NetworkReadPerformed = false, RemoteWritePerformed = false });
            var a = Save("pub-attempt.json", new { Schema = "matawaka.workbench-fixed-publication-attempt/v0.1", State = "ATTEMPT_CONSUMED_NO_RETRY",
                PreflightSha256 = pre.Sha256, AcceptedHead = Base.Proof.Accepted.Head, AcceptedTagObject = Base.Proof.TagObject, RetryAuthorized = false });
            var o = Save("pub-outcome.json", new { Schema = "matawaka.workbench-fixed-publication-outcome/v0.1", Status = "PUBLICATION_OUTCOME_UNVERIFIED_NO_RETRY",
                SafeReason = "PUSH_OR_READBACK_NOT_VERIFIED", AttemptReceiptSha256 = a.Sha256, PreflightSha256 = pre.Sha256,
                RetryAuthorized = false, PublicationSuccessClaimed = false, RemoteMutationProvenAbsent = false, ExactAdvertisedOldMainGuardObserved = false,
                PushMayHaveStarted = true, LocalSnapshotReverified = true, PushExitCode = 128, PushInvocations = 1, RemoteReadInvocations = 2 });
            var old = Path.Combine(Temp, "old-publisher"); Directory.CreateDirectory(old); File.WriteAllText(Path.Combine(old, "preserve.json"), "{}");
            var deniedStage = Path.Combine(Temp, "old-discovery"); Diagnostic.CreateStage(deniedStage);
            var prior = new DiagnosticPlan(Base.Proof, "fixture", deniedStage, old, Path.Combine(Base.Root, "artifacts/publication-v0601"), pre, a, o);
            var da = Save("denied-attempt.json", new { Schema = "matawaka.workbench-receive-discovery-attempt/v0.1", State = "DIAGNOSTIC_ATTEMPT_CONSUMED_NO_RETRY",
                Operation = "RECEIVE_PACK_REFERENCE_DISCOVERY_ONLY", Endpoint = Spec.Remote, PreflightSha256 = pre.Sha256,
                PriorPublicationAttemptSha256 = a.Sha256, PriorPublicationOutcomeSha256 = o.Sha256,
                MaxHelperInvocations = 1, MaxPushInvocations = 0, MaxFetchInvocations = 0, PublicationAuthorized = false, PriorAttemptRearmed = false, RetryAuthorized = false });
            var dr = Save("denied-result.json", new { Schema = "matawaka.workbench-receive-discovery-diagnostic/v0.1", Status = "RECEIVE_DISCOVERY_DIAGNOSTIC_RECORDED_NO_PUSH",
                Endpoint = Spec.Remote, SafeFailureCategory = "NONE", DiagnosticAttemptSha256 = da.Sha256, PreflightSha256 = pre.Sha256,
                PriorPublicationAttemptSha256 = a.Sha256, PriorPublicationOutcomeSha256 = o.Sha256,
                HelperInvocations = 1, PushInvocations = 0, FetchInvocations = 0, PublicationAuthorized = false, PriorAttemptRearmed = false, RetryAuthorized = false,
                LocalSnapshotAndPriorTransportReverified = true, NewDiagnosticStageUnchanged = true, HistoricalCauseRecovered = false, RefUpdateRequested = false, ObjectTransferRequested = false,
                Probe = new { Category = "ACCESS_FORBIDDEN", ExitCode = 128, Refs = (object?)null, ProcessStarted = true, ProcessExited = true, TimedOut = false, CleanupIncomplete = false,
                    Stdout = new { EndOfStream = true, LimitExceeded = false }, Stderr = new { EndOfStream = true, LimitExceeded = false } }, Snapshot, PriorTransportDigest = Files.Digest(old) });
            Plan = new(prior, da, dr, Path.Combine(Temp, "recheck"), prior.OutputRoot);
        }
        internal RecheckPlan Change(string role, Action<JsonNode> change) {
            var pin = role == "attempt" ? Plan.DeniedAttempt : Plan.DeniedResult; var j = JsonNode.Parse(Incident.Bound(Plan.Prior, pin))!; change(j);
            var replacement = Save(Guid.NewGuid().ToString("N") + ".json", j);
            return role == "attempt" ? Plan with { DeniedAttempt = replacement } : Plan with { DeniedResult = replacement };
        }
        public void Dispose() { Base.Dispose(); try { Directory.Delete(Temp, true); } catch (IOException) { } }
    }
    private static Capture Capture(bool denied, bool timeout = false) {
        var error = denied ? "fatal: requested URL returned error: 403\n"u8.ToArray() : Array.Empty<byte>();
        var data = denied ? Array.Empty<byte>() : Encoding.ASCII.GetBytes("fetch\noption\npush\n\nac083598711caa0c399cc0d2c385b980c083024a refs/heads/main\n\n");
        return new(denied ? 128 : 0, true, true, timeout, false, timeout ? "PROCESS_DEADLINE" : "NONE", data, error,
            new(data.Length, true, false), new(error.Length, true, false));
    }
    private static async Task Flow(bool denied, bool timeout = false, bool throwProbe = false) {
        using var f = new F(); await f.Init(); var p = f.Plan; var expected = await Reconciliation.Observe(p, f.Git); int calls = 0;
        var oldDigests = new[] { Files.Digest(p.Prior.PriorStage), Files.Digest(p.Prior.Stage), V2.StoreDigest(f.Base.Root) };
        var pins = new[] { p.Prior.Preflight, p.Prior.PriorAttempt, p.Prior.PriorOutcome, p.DeniedAttempt, p.DeniedResult };
        var originals = pins.Select(x => Incident.Bound(p.Prior, x)).ToArray();
        Task<Capture> Probe() { calls++; Safe.Need(File.Exists(p.Attempt), "NETWORK_BEFORE_DURABLE_ATTEMPT"); if (throwProbe) throw new IOException("dummy secret must not escape"); return Task.FromResult(Capture(denied, timeout)); }
        var status = await PermissionRecheck.Execute(p, expected, () => Reconciliation.Observe(p, f.Git), RecheckPlan.Confirmation, TimeSpan.Zero, Probe, "fixture", "fixture");
        var result = Safe.Parse(Safe.Read(p.Result)); var a = Safe.Parse(Safe.Read(p.Attempt));
        Pass(calls == 1 && result.GetProperty("HelperInvocations").GetInt32() == 1, "one-helper-" + denied + timeout + throwProbe);
        Safe.Eq(result, "AttemptReceiptSha256", Safe.Hash(Safe.Read(p.Attempt))); Safe.Eq(result, "DeniedDiscoveryResultSha256", p.DeniedResult.Sha256);
        Safe.Eq(a, "State", "PERMISSION_RECHECK_CONSUMED_NO_RETRY");
        Safe.False(result, "DeclarationIndependentlyVerified", "PublicationAuthorized", "RetryAuthorized", "PriorAttemptRearmed", "HistoricalCauseRecovered", "WritePermissionProven", "RawOutputPersisted", "RefUpdateRequested", "ObjectTransferRequested");
        Pass(status == (throwProbe ? "PERMISSION_RECHECK_INCOMPLETE_NO_PUSH_NO_RETRY" : PermissionRecheck.Recorded), "status-not-access-or-push-success-" + denied + timeout + throwProbe);
        if (!throwProbe) Safe.Eq(result.GetProperty("Probe"), "Category", timeout ? "PROCESS_DEADLINE" : denied ? "ACCESS_FORBIDDEN" : "RECEIVE_DISCOVERY_COMPLETED");
        Pass(pins.Select((pin, i) => Incident.Bound(p.Prior, pin).SequenceEqual(originals[i])).All(x => x) &&
            oldDigests.SequenceEqual(new[] { Files.Digest(p.Prior.PriorStage), Files.Digest(p.Prior.Stage), V2.StoreDigest(f.Base.Root) }), "old-evidence-and-stages-unchanged-" + denied + timeout + throwProbe);
        Pass(Refused(() => PermissionRecheck.Unused(p)) && !Encoding.UTF8.GetString(Safe.Read(p.Result)).Contains("dummy secret"), "consumed-and-secret-free-" + denied + timeout + throwProbe);
    }
    private static async Task Main() {
        Pass(RecheckPlan.Fixed().DeniedResult.Sha256 == "d755106034cfc470a10d6d859c138f308bc0334f77453f159e3a61a9cc014158" &&
            RecheckPlan.Fixed().DeniedAttempt.Sha256 == "7eed29fac84985b6d846759250027fd3f8cb30e055f75ced3931d97a023c79ea", "fixed-real-denied-incident-pins");
        foreach (var s in new[] { "PUBLISH-EXACT-V0601-58B9430-C07409C", DiagnosticPlan.Confirmation, "" }) Pass(Refused(() => PermissionRecheck.Confirm(s, TimeSpan.Zero)), "old-or-empty-confirmation-refused-" + Checks.Count);
        Pass(Refused(() => PermissionRecheck.Confirm(RecheckPlan.Confirmation, TimeSpan.FromMinutes(6))), "expired-recheck-refused");
        using (var f = new F()) {
            await f.Init(); Pass(Reconciliation.Read(f.Plan) == f.Snapshot, "full-linked-reconciliation");
            foreach (var (id, role, mutation) in new (string, string, Action<JsonNode>)[] {
                ("old-attempt-rearmed", "attempt", j => j["State"] = "READY"),
                ("denial-reclassified", "result", j => j["Probe"]!["Category"] = "RECEIVE_DISCOVERY_COMPLETED"),
                ("extra-prior-helper", "result", j => j["HelperInvocations"] = 2),
                ("prior-push-claimed", "result", j => j["PushInvocations"] = 1),
                ("retry-authority-forged", "result", j => j["RetryAuthorized"] = true),
                ("snapshot-substituted", "result", j => j["Snapshot"]!["GitStoreByteDigest"] = "changed"),
                ("attempt-link-drift", "result", j => j["DiagnosticAttemptSha256"] = new string('0', 64)),
                ("endpoint-changed", "result", j => j["Endpoint"] = "https://example.org/"),
                ("incomplete-prior-pipe", "result", j => j["Probe"]!["Stderr"]!["EndOfStream"] = false)
            }) { var p = f.Change(role, mutation); Pass(Refused(() => Reconciliation.Read(p)), id); }
            var expected = await Reconciliation.Observe(f.Plan, f.Git); int calls = 0;
            Task<Capture> Probe() { calls++; return Task.FromResult(Capture(false)); }
            foreach (var bad in new[] { "WRONG", DiagnosticPlan.Confirmation }) {
                bool denied = false; try { await PermissionRecheck.Execute(f.Plan, expected, () => Task.FromResult(expected), bad, TimeSpan.Zero, Probe, "f", "f"); } catch (InvalidDataException) { denied = true; }
                Pass(denied && calls == 0 && !File.Exists(f.Plan.Attempt) && !Directory.Exists(f.Plan.Stage), "cancel-no-attempt-stage-network-" + bad);
            }
            bool drift = false; try { await PermissionRecheck.Execute(f.Plan, expected, () => Task.FromResult(expected with { PriorDiscoveryStageDigest = "drift" }), RecheckPlan.Confirmation, TimeSpan.Zero, Probe, "f", "f"); } catch (InvalidDataException) { drift = true; }
            Pass(drift && calls == 0 && !File.Exists(f.Plan.Attempt), "pre-confirmed-state-drift-zero-network");
            Directory.CreateDirectory(f.Plan.Stage); Pass(Refused(() => PermissionRecheck.Unused(f.Plan)), "new-stage-collision-no-reuse");
            var old = Safe.Under(f.Base.Root, f.Plan.DeniedResult.RelativePath); File.AppendAllText(old, " ");
            Pass(Refused(() => Reconciliation.Read(f.Plan)), "old-receipt-raw-byte-tamper");
        }
        await Flow(false); await Flow(true); await Flow(true, timeout: true); await Flow(false, throwProbe: true);
        File.WriteAllBytes("permission-recheck-qualification.json", Files.Json(new { Schema = "matawaka.permission-recheck-qualification/v0.1", Passed = true, Checks,
            OperatorHostUsed = false, ProductionGitHubContacted = false, LiveTokensUsed = false, PushPerformed = false,
            Note = "Reconciliation and orchestration fixtures; unchanged native HTTPS/stream tests run separately." }));
        Console.WriteLine("PERMISSION_RECHECK_QUALIFICATION_PASS " + Checks.Count);
    }
}
