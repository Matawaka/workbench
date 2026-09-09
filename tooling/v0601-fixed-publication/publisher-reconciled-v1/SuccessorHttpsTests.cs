using System.Text;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601ReconciledPublisher;
using Matawaka.V0601DiscoveryDiagnostic;

namespace Matawaka.V0601FixedPublisher;

internal static partial class SuccessorHttps
{
    private sealed class ChangedAdvertisement : IPublicationTransfer
    {
        private readonly IPublicationTransfer inner; private readonly string remote;
        internal ChangedAdvertisement(IPublicationTransfer value, string repository) { inner = value; remote = repository; }
        public void Create() => inner.Create();
        public IDisposable Lock() => inner.Lock();
        public Task<ReadObservation> Read() => inner.Read();
        public async Task<NativeObservation> Push(string header)
        {
            // Fixture-only competing ancestor move AFTER public pre-read and BEFORE this push's advertisement.
            var ancestor = Encoding.ASCII.GetString(await Backend(Path.GetDirectoryName(remote)!,
                new[] { "--git-dir=" + remote, "rev-parse", PublishPlan.MainRef + "^" })).Trim();
            await Backend(Path.GetDirectoryName(remote)!, new[] { "--git-dir=" + remote, "update-ref", PublishPlan.MainRef, ancestor });
            return await inner.Push(header);
        }
        public bool GuardVerified() => inner.GuardVerified();
        public void Unchanged() => inner.Unchanged();
    }
    private static async Task RunCase(string mode, bool trust = true)
    {
        using var f = new SuccessorFixture(); await f.Init();
        var remote = Path.Combine(f.Temp, "https-remote.git");
        await Backend(f.Temp, new[] { "init", "--bare", "-q", remote });
        await Backend(f.Root, new[] { "push", "-q", new Uri(remote + Path.DirectorySeparatorChar).AbsoluteUri,
            f.Plan.Prior.Prior.Proof.Accepted.Second + ":" + PublishPlan.MainRef });
        if (mode == "atomic-unsupported") await Backend(f.Temp, new[] { "--git-dir=" + remote, "config", "receive.advertiseAtomic", "false" });
        var serverMode = mode is "bad-credential" or "ancestor-drift" or "atomic-unsupported" ? "normal" : mode;
        using var server = new Server(remote, serverMode);
        var ca = Path.Combine(f.Temp, "fixture-ca.pem"); File.WriteAllText(ca, server.CaPem, new UTF8Encoding(false));
        if (trust) Environment.SetEnvironmentVariable("FIXTURE_TLS_CA", ca);
        try {
            var p = f.Plan with { Endpoint = server.Endpoint };
            var expected = await SuccessorEvidence.Observe(p, f.Seed.Git);
            var beforeGit = V2.StoreDigest(f.Root);
            var pins = p.Inputs(); var original = pins.Select(x => IncidentBytes(p, x)).ToArray();
            var stages = new[] { p.Prior.Prior.PriorStage, p.Prior.Prior.Stage, p.Prior.Stage };
            var oldStages = stages.Select(x => Files.Digest(x)).ToArray();
            using var locks = new ReadLocks(); locks.Tree(Safe.Under(f.Root, ".git"));
            foreach (var stage in stages) locks.Tree(stage);
            foreach (var pin in pins) locks.Hold(Safe.Under(f.Root, pin.RelativePath));
            IPublicationTransfer transfer = new PublicationTransfer(p);
            if (mode == "ancestor-drift") transfer = new ChangedAdvertisement(transfer, remote);
            bool refused = false;
            var token = mode == "bad-credential" ? "github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_WRONG_FIXTURE" : Dummy;
            try { await ReconciledPublisher.Execute(p, expected, () => SuccessorEvidence.Observe(p, f.Seed.Git),
                SuccessorPlan.Confirmation, TimeSpan.Zero, Credential.Header(token), "fixture", "fixture", transfer); }
            catch (InvalidDataException) { refused = true; }
            var happy = mode == "normal" && trust;
            var updated = mode is "normal" or "lost-receive-response" or "post-readback-failure" && trust;
            Safe.Need(refused != happy, "HTTPS_SUCCESSOR_UNEXPECTED_STATUS_" + mode);
            var recordPath = happy ? p.Result : p.Outcome;
            var record = Safe.Parse(Safe.Read(recordPath));
            var main = Encoding.ASCII.GetString(await Backend(f.Temp, new[] { "--git-dir=" + remote, "rev-parse", PublishPlan.MainRef })).Trim();
            Safe.Need((main == p.Prior.Prior.Proof.Accepted.Head) == updated, "HTTPS_SUCCESSOR_TARGET_OBSERVATION_" + mode);
            Safe.Need(server.ReceiveRpcRequests == (updated ? 1 : 0), "HTTPS_SUCCESSOR_ONE_OR_ZERO_UPDATE_REQUESTS");
            Safe.Need(!server.RedirectFollowed && server.PublicCredentialLeaks == 0, "HTTPS_SUCCESSOR_CREDENTIAL_BOUNDARY");
            if (happy) {
                Safe.Eq(record, "Status", ReconciledPublisher.Success); Safe.True(record, "ExactAdvertisedOldMainGuardVerified", "LocalSnapshotAndAllThreePriorStagesReverified");
                var tag = Encoding.ASCII.GetString(await Backend(f.Temp, new[] { "--git-dir=" + remote, "rev-parse", PublishPlan.TagRef })).Trim();
                Safe.Need(tag == p.Prior.Prior.Proof.TagObject, "ANNOTATED_TAG_OBJECT_RECREATED");
            } else {
                Safe.False(record, "PublicationSuccessClaimed", "RemoteMutationProvenAbsent", "RetryAuthorized");
                if (mode is "lost-receive-response" or "post-readback-failure") Safe.True(record, "PushMayHaveStarted");
                Safe.Need(!File.Exists(p.Result), "FALSE_SUCCESS_RECEIPT");
            }
            if (mode == "ancestor-drift") Safe.False(record, "ExactAdvertisedOldMainGuardObserved");
            if (mode == "atomic-unsupported") {
                // Native Git emits the atomic refusal AND a remote-end-hung-up line.
                // Keep the qualified classifier's ambiguity rule; do not infer a single cause or relax atomic.
                Safe.Need(Classification.Error("fatal: the receiving end does not support --atomic push\nfatal: the remote end hung up unexpectedly\n"u8.ToArray()) == "AMBIGUOUS_NATIVE_ERROR", "COMBINED_ATOMIC_ERROR_MUST_REMAIN_AMBIGUOUS");
                Safe.Eq(record.GetProperty("NativePush"), "Category", "AMBIGUOUS_NATIVE_ERROR");
            }
            if (mode == "bad-credential") Safe.Need(server.AuthenticationRefusals == 1, "AUTHENTICATION_NOT_ONE_SHOT");
            var reportText = Encoding.UTF8.GetString(Safe.Read(recordPath));
            Safe.Need(!reportText.Contains(token) && !reportText.Contains(Convert.ToBase64String(Encoding.ASCII.GetBytes("x-access-token:" + token))), "SECRET_IN_SUCCESSOR_REPORT");
            bool replay = false; try { ReconciledPublisher.Unused(p); } catch (InvalidDataException) { replay = true; }
            Safe.Need(replay, "HTTPS_SUCCESSOR_REPLAY_NOT_REFUSED");
            Safe.Need(V2.StoreDigest(f.Root) == beforeGit && stages.Select(x => Files.Digest(x)).SequenceEqual(oldStages) &&
                pins.Select((pin, i) => IncidentBytes(p, pin).SequenceEqual(original[i])).All(x => x), "HTTPS_SUCCESSOR_ORIGINAL_STATE_CHANGED");
            Results.Add(new { Id = "successor-https-" + mode + (trust ? "" : "-untrusted"), Passed = true,
                Requests = server.Requests, ReceiveRequests = server.ReceiveRequests, UpdatePostRequests = server.ReceiveRpcRequests,
                PublicCredentialLeaks = server.PublicCredentialLeaks, AuthenticationRefusals = server.AuthenticationRefusals,
                ExactRemoteTargetObserved = updated, GuardVerified = happy, OriginalInputsAndStagesUnchanged = true,
                NativePushCategory = record.TryGetProperty("NativePush", out var native) && native.ValueKind == System.Text.Json.JsonValueKind.Object ? native.GetProperty("Category").GetString() : null,
                DisposableFixtureOnly = true });
            Console.WriteLine("PASS SUCCESSOR_HTTPS " + mode + " trust=" + trust);
        } finally {
            Environment.SetEnvironmentVariable("FIXTURE_TLS_CA", null);
            // Fixture teardown only, after preservation assertions and read-lock disposal.
            // Native receive-pack creates read-only object files on Windows. No operator path is involved.
            if (Directory.Exists(f.Temp)) foreach (var file in Directory.EnumerateFiles(f.Temp, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }
    private static byte[] IncidentBytes(SuccessorPlan p, BoundFile pin) => Matawaka.V0601DiscoveryDiagnostic.Incident.Bound(p.Prior.Prior, pin);
    private static async Task Main()
    {
        using var locks = new ReadLocks();
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Git))!; var pins = ToolTree.Embedded(); ToolTree.Verify(root, pins, locks);
        foreach (var mode in new[] { "normal", "lost-receive-response", "post-readback-failure", "redirect", "rate-limit", "bad-credential", "ancestor-drift", "atomic-unsupported" }) await RunCase(mode);
        await RunCase("normal", false);
        File.WriteAllBytes("successor-https-qualification.json", Files.Json(new {
            Schema = "matawaka.reconciled-publication-https-qualification/v0.1", Passed = true, Checks = Results,
            ExactMinGitFilesReadLocked = pins.Length, RealNativeGitSmartHttps = true, OnlyLoopbackFixtures = true,
            ProductionRemoteContacted = false, RealCredentialsUsed = false, OperatorHostAccessed = false,
            Note = "The new production orchestration and bounded native driver exercised against a fixture-only Git HTTPS server. Historical implementation is not used as the publisher entry point."
        }));
        Console.WriteLine("SUCCESSOR_HTTPS_QUALIFICATION_PASS " + Results.Count);
    }
}
