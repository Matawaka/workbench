using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;
using Matawaka.V0601DiscoveryDiagnostic;
using Matawaka.V0601PermissionRecheck;

namespace Matawaka.V0601ReconciledPublisher;

internal sealed class SuccessorFixture : IDisposable
{
    internal readonly RecheckFixture Seed = new();
    internal SuccessorPlan Plan = null!;
    internal string Root => Seed.Base.Root;
    internal string Temp => Seed.Temp;
    internal V2Snapshot Snapshot => Seed.Snapshot;
    internal async Task Init()
    {
        await Seed.Init();
        var gitRoot = Path.GetDirectoryName(Path.GetDirectoryName(GitRead.Locate()))!;
        Seed.Plan = Seed.Plan with { Prior = Seed.Plan.Prior with { GitRoot = gitRoot } };
        var p = Seed.Plan; var d = p.Prior;
        Diagnostic.CreateStage(p.Stage);
        var a = Seed.Save("successful-recheck-attempt.json", new {
            Schema = "matawaka.workbench-permission-recheck-attempt/v0.1", State = "PERMISSION_RECHECK_CONSUMED_NO_RETRY",
            ExplicitConfirmation = RecheckPlan.Confirmation, Endpoint = Spec.Remote,
            OperatorDeclaration = "CONTENTS_READ_AND_WRITE_ADDED_AFTER_DENIED_DISCOVERY", DeclarationIndependentlyVerified = false,
            PreflightSha256 = d.Preflight.Sha256, PriorPublicationAttemptSha256 = d.PriorAttempt.Sha256,
            PriorPublicationOutcomeSha256 = d.PriorOutcome.Sha256, DeniedDiscoveryAttemptSha256 = p.DeniedAttempt.Sha256,
            DeniedDiscoveryResultSha256 = p.DeniedResult.Sha256, MaxHelperInvocations = 1, MaxPushInvocations = 0, MaxFetchInvocations = 0,
            PublicationAuthorized = false, PriorAttemptRearmed = false, RetryAuthorized = false
        });
        var r = Seed.Save("successful-recheck.json", new {
            Schema = "matawaka.workbench-permission-recheck/v0.1", Status = "PERMISSION_RECHECK_RECORDED_NO_PUSH",
            AttemptReceiptSha256 = a.Sha256, Endpoint = Spec.Remote, SafeFailureCategory = "NONE",
            PreflightSha256 = d.Preflight.Sha256, PriorPublicationAttemptSha256 = d.PriorAttempt.Sha256,
            PriorPublicationOutcomeSha256 = d.PriorOutcome.Sha256, DeniedDiscoveryAttemptSha256 = p.DeniedAttempt.Sha256,
            DeniedDiscoveryResultSha256 = p.DeniedResult.Sha256,
            OperatorDeclaration = "CONTENTS_READ_AND_WRITE_ADDED_AFTER_DENIED_DISCOVERY", DeclarationIndependentlyVerified = false,
            HelperInvocations = 1, PushInvocations = 0, FetchInvocations = 0,
            AdvertisedBaselineMatches = true, LocalSnapshotAndBothPriorStagesReverified = true, NewStageUnchanged = true,
            Snapshot, PriorPublicationStageDigest = Files.Digest(d.PriorStage), PriorDiscoveryStageDigest = Files.Digest(d.Stage),
            Probe = new { Category = "RECEIVE_DISCOVERY_COMPLETED", ExitCode = 0, ProcessStarted = true, ProcessExited = true,
                TimedOut = false, CleanupIncomplete = false, Stdout = new PipeObservation(100, true, false), Stderr = new PipeObservation(0, true, false),
                Refs = new ListedRefs(d.Proof.Accepted.Second, null, null, null, null, 3) },
            PublicationAuthorized = false, PriorAttemptRearmed = false, RetryAuthorized = false, WritePermissionProven = false,
            HistoricalCauseRecovered = false, ObjectTransferRequested = false, RefUpdateRequested = false, StoredCredentialAccessPerformed = false,
            RawOutputPersisted = false, SecretDerivedDigestsPersisted = false, SourceMutationPerformed = false, GitRefMutationPerformed = false
        });
        Plan = new(p, a, r, Path.Combine(Temp, "successor-stage"), p.OutputRoot, Spec.Remote);
    }
    internal Task<SuccessorObservation> Read() => SuccessorEvidence.Observe(Plan, Seed.Git);
    internal SuccessorPlan Change(string role, Action<JsonNode> change)
    {
        var pin = role == "attempt" ? Plan.RecheckAttempt : Plan.RecheckResult;
        var j = JsonNode.Parse(Incident.Bound(Plan.Prior.Prior, pin))!; change(j);
        var replacement = Seed.Save(Guid.NewGuid().ToString("N") + ".json", j);
        return role == "attempt" ? Plan with { RecheckAttempt = replacement } : Plan with { RecheckResult = replacement };
    }
    public void Dispose() => Seed.Dispose();
}

internal static class SuccessorTests
{
    private static readonly List<object> Checks = new();
    private const string Secret = "github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_SUCCESSOR_FIXTURE";
    private static void Pass(bool ok, string id) { Safe.Need(ok, "SUCCESSOR_TEST_FAILED_" + id); Checks.Add(new { Id = id, Passed = true }); Console.WriteLine("PASS " + id); }
    private static bool Refused(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } }
    private static NativeObservation Native(string op, string category = "NATIVE_COMPLETED", bool cleanup = false) =>
        new(op, category, category == "NATIVE_COMPLETED" ? 0 : 128, true, true, category == "PROCESS_DEADLINE", cleanup,
            new(0, true, false), new(0, true, false));
    private sealed class FakeTransfer : IPublicationTransfer
    {
        private readonly SuccessorPlan plan; private readonly string mode;
        internal int Reads, Pushes;
        internal FakeTransfer(SuccessorPlan p, string behavior) { plan = p; mode = behavior; }
        public void Create() { Safe.Need(File.Exists(plan.Attempt), "CREATE_BEFORE_CONSUMPTION"); Directory.CreateDirectory(plan.StageRoot); }
        public IDisposable Lock() => new ReadLocks();
        public Task<ReadObservation> Read()
        {
            Safe.Need(File.Exists(plan.Attempt), "REMOTE_BEFORE_CONSUMPTION"); Reads++;
            var s = plan.Prior.Prior.Proof;
            var baseline = new RemoteState(s.Accepted.Second, null, null, null, null);
            var target = new RemoteState(s.Accepted.Head, s.TagObject, s.Accepted.Head, null, null);
            if (Reads == 1) return Task.FromResult(new ReadObservation(Native("PUBLIC_REF_READ"), mode == "remote-drift" ? target : baseline));
            if (mode == "readback-failed") return Task.FromResult(new ReadObservation(Native("PUBLIC_REF_READ", "CONNECTION_FAILED"), null));
            return Task.FromResult(new ReadObservation(Native("PUBLIC_REF_READ"), mode is "denied" or "deadline" ? baseline : target));
        }
        public Task<NativeObservation> Push(string header)
        {
            Safe.Need(File.Exists(plan.Attempt) && ++Pushes == 1, "REPEATED_OR_UNCONSUMED_PUSH");
            if (mode == "throw-push") throw new IOException(Secret);
            return Task.FromResult(Native("EXACT_ATOMIC_PUSH", mode switch {
                "denied" => "ACCESS_FORBIDDEN", "lost-response" => "REMOTE_CONNECTION_CLOSED",
                "deadline" or "cleanup-incomplete" => "PROCESS_DEADLINE", _ => "NATIVE_COMPLETED"
            }, mode == "cleanup-incomplete"));
        }
        public bool GuardVerified() => mode is not ("missing-guard" or "denied");
        public void Unchanged() { if (mode == "stage-drift") throw new InvalidDataException("FIXTURE_STAGE_DRIFT"); }
    }
    private static async Task Flow(string mode)
    {
        using var f = new SuccessorFixture(); await f.Init(); var p = f.Plan; var expected = await f.Read();
        var originals = p.Inputs().Select(x => Incident.Bound(p.Prior.Prior, x)).ToArray();
        var gitBefore = V2.StoreDigest(f.Root);
        var stages = new[] { p.Prior.Prior.PriorStage, p.Prior.Prior.Stage, p.Prior.Stage };
        var stageBefore = stages.Select(x => Files.Digest(x)).ToArray();
        var fake = new FakeTransfer(p, mode); bool refused = false; int verifies = 0;
        Task<SuccessorObservation> Verify() { verifies++; return Task.FromResult(mode == "local-drift" && verifies >= 3 ? expected with { PermissionStageDigest = "drift" } : expected); }
        try { await ReconciledPublisher.Execute(p, expected, Verify, SuccessorPlan.Confirmation, TimeSpan.Zero, Credential.Header(Secret), "fixture", "fixture", fake); }
        catch (InvalidDataException) { refused = true; }
        Pass(refused == (mode != "normal") && fake.Pushes == (mode == "remote-drift" ? 0 : 1), "flow-exact-outcome-and-push-budget-" + mode);
        Pass(fake.Reads == (mode is "remote-drift" or "throw-push" or "cleanup-incomplete" ? 1 : 2), "flow-read-budget-" + mode);
        var path = mode == "normal" ? p.Result : p.Outcome; var j = Safe.Parse(Safe.Read(path));
        Safe.Eq(j, "AttemptReceiptSha256", Safe.Hash(Safe.Read(p.Attempt)));
        Safe.False(j, "RetryAuthorized", "PriorAttemptRearmed", "HistoricalCauseRecovered", "RawOutputPersisted", "CredentialStored");
        if (mode != "normal") Safe.False(j, "PublicationSuccessClaimed", "RemoteMutationProvenAbsent");
        Pass(!Safe.Utf8.GetString(Safe.Read(path)).Contains(Secret) && !Safe.Utf8.GetString(Safe.Read(path)).Contains(Credential.Header(Secret)), "flow-secret-free-evidence-" + mode);
        Pass(Refused(() => ReconciledPublisher.Unused(p)), "flow-replay-refused-" + mode);
        Pass(p.Inputs().Select((pin, i) => Incident.Bound(p.Prior.Prior, pin).SequenceEqual(originals[i])).All(x => x) &&
            stages.Select(x => Files.Digest(x)).SequenceEqual(stageBefore) && V2.StoreDigest(f.Root) == gitBefore, "flow-originals-stages-source-preserved-" + mode);
    }
    private static async Task Main()
    {
        var fixedPlan = SuccessorPlan.Fixed();
        Pass(fixedPlan.Inputs().Length == 15 && fixedPlan.RecheckResult.Sha256 == "e6c720426ad6b285dc6f078a2fb941f732f54aaf5359b788288173998d40577f", "fifteen-fixed-original-bindings");
        foreach (var value in new[] { "", "PUBLISH-EXACT-V0601-58B9430-C07409C", DiagnosticPlan.Confirmation, RecheckPlan.Confirmation })
            Pass(Refused(() => ReconciledPublisher.Confirm(value, TimeSpan.Zero)), "old-confirmation-not-successor-authority-" + Checks.Count);
        Pass(Refused(() => ReconciledPublisher.Confirm(SuccessorPlan.Confirmation, TimeSpan.FromMinutes(6))), "expired-new-confirmation");
        using (var f = new SuccessorFixture()) {
            await f.Init(); Pass(SuccessorEvidence.Read(f.Plan) == f.Snapshot && (await f.Read()).Prior.Local.Snapshot == f.Snapshot, "full-fifteen-link-reconciliation");
            foreach (var (id, role, change) in new (string, string, Action<JsonNode>)[] {
                ("rearmed-correction", "attempt", j => j["State"] = "READY"),
                ("reused-read-confirmation", "attempt", j => j["ExplicitConfirmation"] = DiagnosticPlan.Confirmation),
                ("forged-prior-publication-authority", "result", j => j["PublicationAuthorized"] = true),
                ("forged-write-proof", "result", j => j["WritePermissionProven"] = true),
                ("forged-historical-cause", "result", j => j["HistoricalCauseRecovered"] = true),
                ("forged-verified-declaration", "result", j => j["DeclarationIndependentlyVerified"] = true),
                ("correction-link-drift", "result", j => j["AttemptReceiptSha256"] = new string('0', 64)),
                ("previous-outcome-link-drift", "result", j => j["PriorPublicationOutcomeSha256"] = new string('0', 64)),
                ("still-denied", "result", j => j["Probe"]!["Category"] = "ACCESS_FORBIDDEN"),
                ("nonzero-exit", "result", j => j["Probe"]!["ExitCode"] = 128),
                ("incomplete-stream", "result", j => j["Probe"]!["Stdout"]!["EndOfStream"] = false),
                ("unexpected-tag", "result", j => j["Probe"]!["Refs"]!["Tag"] = new string('a', 40)),
                ("unexpected-main", "result", j => j["Probe"]!["Refs"]!["Main"] = new string('b', 40)),
                ("extra-helper", "result", j => j["HelperInvocations"] = 2),
                ("prior-push-effect", "result", j => j["PushInvocations"] = 1),
                ("snapshot-drift", "result", j => j["Snapshot"]!["GitStoreByteDigest"] = "drift"),
                ("endpoint-drift", "result", j => j["Endpoint"] = "https://example.org/")
            }) Pass(Refused(() => SuccessorEvidence.Read(f.Change(role, change))), "reject-" + id);
            var expected = await f.Read(); var fake = new FakeTransfer(f.Plan, "normal"); bool cancelled = false;
            try { await ReconciledPublisher.Execute(f.Plan, expected, () => Task.FromResult(expected), RecheckPlan.Confirmation, TimeSpan.Zero, "unused", "f", "f", fake); } catch (InvalidDataException) { cancelled = true; }
            Pass(cancelled && fake.Reads == 0 && fake.Pushes == 0 && !File.Exists(f.Plan.Attempt) && !Directory.Exists(f.Plan.StageRoot), "cancel-before-any-effect");
            bool drift = false;
            try { await ReconciledPublisher.Execute(f.Plan, expected, () => Task.FromResult(expected with { PermissionStageDigest = "changed" }), SuccessorPlan.Confirmation, TimeSpan.Zero, "unused", "f", "f", fake); } catch (InvalidDataException) { drift = true; }
            Pass(drift && fake.Reads == 0 && !File.Exists(f.Plan.Attempt), "fresh-drift-before-consumption");
            Pass(Refused(() => ReconciledPublisher.Unused(f.Plan with { StageRoot = f.Plan.Prior.Stage })), "old-stage-cannot-be-reused");
            var t = new IsolatedTransport(f.Plan.TransportPlan());
            Pass(t.PushArgs().Count(x => x.Contains(":refs/", StringComparison.Ordinal)) == 2 && t.PushArgs().Contains("--atomic") &&
                t.PushArgs().All(x => !x.StartsWith("--force", StringComparison.Ordinal)), "ordinary-atomic-exact-two-ref-vector");
            Pass(Refused(() => t.StartInfo(new[] { "push", "--force" }, Credential.Header(Secret))), "arbitrary-transport-command-refused");
            Pass(Refused(() => t.StartInfo(t.ReadArgs(), Credential.Header(Secret))), "credentials-not-used-for-public-read");
            var raw = Safe.Under(f.Root, f.Plan.RecheckResult.RelativePath); File.AppendAllText(raw, " ");
            Pass(Refused(() => SuccessorEvidence.Read(f.Plan)), "raw-recheck-byte-tamper");
        }
        foreach (var mode in new[] { "normal", "denied", "lost-response", "readback-failed", "missing-guard", "deadline", "cleanup-incomplete", "remote-drift", "throw-push", "local-drift", "stage-drift" }) await Flow(mode);
        File.WriteAllBytes("successor-qualification.json", Files.Json(new { Schema = "matawaka.reconciled-publication-qualification/v0.1", Passed = true, Checks,
            OperatorHostUsed = false, ProductionGitHubContacted = false, LiveTokensUsed = false, RealPublicationPerformed = false,
            Note = "Incident reconciliation and orchestration fixtures, including injected transport outcomes; actual native HTTPS tests are separate." }));
        Console.WriteLine("SUCCESSOR_QUALIFICATION_PASS " + Checks.Count);
    }
}
