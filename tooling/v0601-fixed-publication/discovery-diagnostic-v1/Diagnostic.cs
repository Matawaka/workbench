using System.Diagnostics;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;

namespace Matawaka.V0601DiscoveryDiagnostic;

internal sealed record DiagnosticPlan(ProofSpec Proof, string GitRoot, string Stage,
    string PriorStage, string OutputRoot, BoundFile Preflight, BoundFile PriorAttempt, BoundFile PriorOutcome)
{
    internal const string Confirmation = "DIAGNOSE-EXACT-V0601-RECEIVE-DISCOVERY-NO-PUSH";
    internal string Attempt => Path.Combine(OutputRoot, "attempt-discovery-diagnostic-v1-" + Proof.Accepted.Head + ".json");
    internal string Result => Path.Combine(OutputRoot, "discovery-diagnostic-v1-" + Proof.Accepted.Head + ".json");
    internal static DiagnosticPlan Fixed() => new(V2.Fixed(), @"K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit",
        @"K:\Matawaka\Tools\WorkbenchV0601ReceiveDiscoveryV1",
        @"K:\Matawaka\Tools\WorkbenchV0601FixedPublicationV1",
        @"K:\Matawaka\Workbench\artifacts\publication-v0601",
        new("artifacts/publication-v0601/preflight-promisor-v2-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "c7a7dae81f93c783629f3fc5686aec5a2fb45533d95a5413628effa5763e1671", 6187),
        new("artifacts/publication-v0601/attempt-publish-fixed-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "5c9334fe696f6eee437b2abbee299ece81985032310fc64301abdca5d03a37e1", 1306),
        new("artifacts/publication-v0601/outcome-publish-fixed-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "d5cd6b21b319803272c25983054ac1fad7e27cad8a654e69c8dfcca01f05d10e", 1282));
}
internal sealed record LocalObservation(V2Snapshot Snapshot, string PriorTransportDigest);

internal static class Incident
{
    internal static byte[] Bound(DiagnosticPlan p, BoundFile pin)
    {
        var b = Safe.Read(Safe.Under(p.Proof.Accepted.Root, pin.RelativePath));
        Safe.Need(b.Length == pin.Bytes && Safe.Hash(b) == pin.Sha256, "ORIGINAL_INCIDENT_BYTES_MISMATCH");
        return b;
    }
    internal static V2Snapshot Read(DiagnosticPlan p)
    {
        var pre = Safe.Parse(Bound(p, p.Preflight)); var attempt = Safe.Parse(Bound(p, p.PriorAttempt));
        var outcome = Safe.Parse(Bound(p, p.PriorOutcome));
        Safe.Eq(pre, "Schema", "matawaka.workbench-v0601-publication-preflight/v0.2");
        Safe.Eq(pre, "Status", V2.Status);
        Safe.False(pre, "PublicationAuthorityCreated", "RetryAuthorityCreated", "NetworkReadPerformed", "RemoteWritePerformed");
        Safe.Eq(attempt, "Schema", "matawaka.workbench-fixed-publication-attempt/v0.1");
        Safe.Eq(attempt, "State", "ATTEMPT_CONSUMED_NO_RETRY");
        Safe.Eq(outcome, "Schema", "matawaka.workbench-fixed-publication-outcome/v0.1");
        Safe.Eq(outcome, "Status", "PUBLICATION_OUTCOME_UNVERIFIED_NO_RETRY");
        Safe.Eq(outcome, "SafeReason", "PUSH_OR_READBACK_NOT_VERIFIED");
        Safe.Eq(outcome, "AttemptReceiptSha256", p.PriorAttempt.Sha256);
        foreach (var x in new[] { attempt, outcome }) { Safe.Eq(x, "PreflightSha256", p.Preflight.Sha256); Safe.False(x, "RetryAuthorized"); }
        Safe.Eq(attempt, "AcceptedHead", p.Proof.Accepted.Head); Safe.Eq(attempt, "AcceptedTagObject", p.Proof.TagObject);
        Safe.False(outcome, "PublicationSuccessClaimed", "RemoteMutationProvenAbsent", "ExactAdvertisedOldMainGuardObserved");
        Safe.True(outcome, "PushMayHaveStarted", "LocalSnapshotReverified");
        Safe.Need(outcome.GetProperty("PushExitCode").GetInt32() == 128 && outcome.GetProperty("PushInvocations").GetInt32() == 1 && outcome.GetProperty("RemoteReadInvocations").GetInt32() == 2, "INCIDENT_SHAPE_MISMATCH");
        return JsonSerializer.Deserialize<V2Snapshot>(pre.GetProperty("Snapshot")) ?? throw new InvalidDataException("INCIDENT_SNAPSHOT_MISSING");
    }
    internal static async Task<LocalObservation> Observe(DiagnosticPlan p, GitRead git)
    {
        var prior = Read(p); var current = await V2.Check(p.Proof, git);
        Safe.Need(current == prior, "CURRENT_LOCAL_STATE_DIFFERS_FROM_ORIGINAL_PREFLIGHT");
        Safe.NoReparse(p.PriorStage); Safe.Need(Directory.Exists(p.PriorStage), "PRIOR_TRANSPORT_EVIDENCE_MISSING");
        return new(current, Files.Digest(p.PriorStage));
    }
}

internal static class Diagnostic
{
    internal static void Unused(DiagnosticPlan p)
    {
        foreach (var path in new[] { p.Attempt, p.Result, p.Stage }) {
            Safe.NoReparse(path); Safe.Need(!File.Exists(path) && !Directory.Exists(path), "DIAGNOSTIC_ALREADY_CONSUMED_NO_OVERWRITE");
        }
    }
    internal static void Confirm(string? value, TimeSpan age)
    {
        Safe.Need(value == DiagnosticPlan.Confirmation, "SEPARATE_READ_ONLY_CONFIRMATION_REQUIRED");
        Safe.Need(age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(5), "DIAGNOSTIC_PREVIEW_EXPIRED");
    }
    internal static void CreateStage(string stage)
    {
        Safe.NoReparse(stage); Safe.Need(!Directory.Exists(stage) && !File.Exists(stage), "DIAGNOSTIC_STAGE_EXISTS");
        Directory.CreateDirectory(stage); Safe.NoReparse(stage);
        Directory.CreateDirectory(Path.Combine(stage, "empty-home"));
        var repo = Path.Combine(stage, "discovery.git");
        Directory.CreateDirectory(Path.Combine(repo, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(repo, "objects"));
        Files.New(Path.Combine(repo, "HEAD"), "ref: refs/heads/unborn-diagnostic\n"u8.ToArray());
        Files.New(Path.Combine(repo, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = true\n\tlogallrefupdates = false\n"u8.ToArray());
    }
    internal static async Task<string> Execute(DiagnosticPlan p, LocalObservation expected,
        Func<Task<LocalObservation>> observe, string confirmation, TimeSpan age,
        Func<Task<Capture>> probe, string selfSha, string toolDigest)
    {
        var timer = Stopwatch.StartNew(); Confirm(confirmation, age); Unused(p);
        Safe.Need(await observe() == expected, "LOCAL_DRIFT_BEFORE_DIAGNOSTIC");
        Confirm(confirmation, age + timer.Elapsed);
        var attemptBytes = Files.Json(new {
            Schema = "matawaka.workbench-receive-discovery-attempt/v0.1", State = "DIAGNOSTIC_ATTEMPT_CONSUMED_NO_RETRY",
            CreatedAt = DateTimeOffset.UtcNow, ExplicitConfirmation = confirmation,
            Endpoint = Spec.Remote, Operation = "RECEIVE_PACK_REFERENCE_DISCOVERY_ONLY",
            PriorPublicationAttemptSha256 = p.PriorAttempt.Sha256, PriorPublicationOutcomeSha256 = p.PriorOutcome.Sha256,
            PreflightSha256 = p.Preflight.Sha256, ToolSha256 = selfSha, MinGitTreeManifestDigest = toolDigest,
            MaxHelperInvocations = 1, MaxPushInvocations = 0, MaxFetchInvocations = 0,
            CredentialStorageAllowed = false, SourceMutationAllowed = false, PublicationAuthorized = false,
            PriorAttemptRearmed = false, RetryAuthorized = false
        });
        Files.New(p.Attempt, attemptBytes);
        ProbeObservation? observation = null; bool stable = false, diagnosticStageStable = false;
        string reason = "NONE"; int calls = 0;
        try {
            CreateStage(p.Stage); var digest = Files.Digest(p.Stage);
            using var locks = new ReadLocks(); locks.Tree(p.Stage);
            Confirm(confirmation, age + timer.Elapsed);
            Safe.Need(await observe() == expected && Safe.Hash(Safe.Read(p.Attempt)) == Safe.Hash(attemptBytes), "DIAGNOSTIC_INPUT_DRIFT");
            calls++; var capture = await probe(); observation = Classification.Observe(capture);
            Array.Clear(capture.Output); Array.Clear(capture.Error);
            stable = await observe() == expected;
            diagnosticStageStable = Files.Digest(p.Stage) == digest;
            if (!stable || !diagnosticStageStable) reason = "POST_DIAGNOSTIC_LOCAL_DRIFT";
        } catch (Exception ex) {
            // Neither Git output nor a transport exception's message can cross this boundary.
            reason = ex is InvalidDataException ? "LOCAL_DIAGNOSTIC_CONTRACT_REFUSED" : "DIAGNOSTIC_INTERNAL_FAILURE";
        }
        var status = reason == "NONE" ? "RECEIVE_DISCOVERY_DIAGNOSTIC_RECORDED_NO_PUSH" : "DIAGNOSTIC_INCOMPLETE_NO_PUSH_NO_RETRY";
        Files.New(p.Result, Files.Json(new {
            Schema = "matawaka.workbench-receive-discovery-diagnostic/v0.1", Status = status, ObservedAt = DateTimeOffset.UtcNow,
            PriorPublicationAttemptSha256 = p.PriorAttempt.Sha256, PriorPublicationOutcomeSha256 = p.PriorOutcome.Sha256,
            PreflightSha256 = p.Preflight.Sha256, DiagnosticAttemptSha256 = Safe.Hash(attemptBytes), ToolSha256 = selfSha,
            MinGitTreeManifestDigest = toolDigest, Endpoint = Spec.Remote, HelperInput = "capabilities; list for-push; end",
            Probe = observation, SafeFailureCategory = reason, HelperInvocations = calls, PushInvocations = 0, FetchInvocations = 0,
            Snapshot = expected.Snapshot, PriorTransportDigest = expected.PriorTransportDigest,
            LocalSnapshotAndPriorTransportReverified = stable, NewDiagnosticStageUnchanged = diagnosticStageStable,
            CredentialSuppliedLocally = true, StoredCredentialAccessPerformed = false, RawOutputPersisted = false,
            SecretDerivedDigestsPersisted = false, SourceMutationPerformed = false, GitRefMutationPerformed = false,
            ObjectTransferRequested = false, RefUpdateRequested = false, PriorAttemptRearmed = false,
            PublicationAuthorized = false, RetryAuthorized = false, HistoricalCauseRecovered = false,
            EntireRuntimeBinaryTreeVerified = false, OsNetworkIsolationProven = false, OsConcurrencyExclusionProven = false,
            Note = "A new bounded observation only, never reconstructed historical stderr. An advertisement is not write permission, hook execution, atomic support qualification or successful publication. No push/fetch commands, update request, object upload, API mutation, old attempt reuse or evidence overwrite. Server access/authentication logs may be produced. Diagnostic completion can record a rejected connection."
        }));
        return status;
    }
}

internal static class DiagnosticEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length == 0, "ARGUMENTS_NOT_ACCEPTED");
            Safe.Need(OperatingSystem.IsWindows(), "WINDOWS_OPERATOR_HOST_REQUIRED");
            var p = DiagnosticPlan.Fixed(); Diagnostic.Unused(p);
            var self = Environment.ProcessPath ?? throw new InvalidDataException("SELF_PATH_MISSING");
            Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(p.Proof.Accepted.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "DIAGNOSTIC_MUST_BE_OUTSIDE_REPOSITORY");
            using var toolLocks = new ReadLocks(); toolLocks.Hold(self);
            var selfSha = Safe.Hash(Safe.Read(self, 128 * 1024 * 1024));
            var toolDigest = ToolTree.Verify(p.GitRoot, ToolTree.Embedded(), toolLocks);
            var git = new GitRead(V2.GitPath, p.Proof.Accepted.Root, V2.GitSha);
            Safe.Need(await git.Text("--version") == "git version 2.55.0.windows.4", "PINNED_GIT_VERSION_MISMATCH");
            var expected = await Incident.Observe(p, git);
            Console.WriteLine("LOCAL DISCOVERY DIAGNOSTIC PREVIEW - NO NETWORK OR CREDENTIAL ACCESS YET");
            Console.WriteLine($"Repository: {p.Proof.Accepted.Root}\nEndpoint: {Spec.Remote}\nAccepted HEAD: {p.Proof.Accepted.Head}\nTag object: {p.Proof.TagObject}\nPrior attempt SHA-256: {p.PriorAttempt.Sha256}\nPrior outcome SHA-256: {p.PriorOutcome.Sha256}");
            Console.WriteLine("After new confirmation: ONE receive-pack reference-discovery helper invocation, hidden local credential input, new diagnostic attempt/result and EMPTY isolated context. ZERO push/fetch/update requests. All old receipts and prior transport remain unchanged. A new observation is NOT recovered historical stderr and grants NO publication/retry authority.");
            Console.WriteLine("Enter exactly: " + DiagnosticPlan.Confirmation);
            var timer = Stopwatch.StartNew(); var confirmation = Console.ReadLine();
            if (confirmation != DiagnosticPlan.Confirmation) { Console.WriteLine("CANCELLED_NO_EFFECTS"); return 2; }
            Diagnostic.Confirm(confirmation, timer.Elapsed); var header = Credential.ReadLocal(); Diagnostic.Confirm(confirmation, timer.Elapsed);
            using var sourceLocks = new ReadLocks(); sourceLocks.Tree(Safe.Under(p.Proof.Accepted.Root, ".git")); sourceLocks.Tree(p.PriorStage);
            foreach (var pin in new[] { p.Preflight, p.PriorAttempt, p.PriorOutcome }.Concat(p.Proof.Accepted.Evidence.Values)) sourceLocks.Hold(Safe.Under(p.Proof.Accepted.Root, pin.RelativePath));
            var status = await Diagnostic.Execute(p, expected, () => Incident.Observe(p, git), confirmation, timer.Elapsed,
                () => BoundedProcess.Run(DiscoveryTransport.Start(p.GitRoot, p.Stage, Spec.Remote, header), DiscoveryTransport.Input), selfSha, toolDigest);
            header = "";
            Console.WriteLine($"COMPLETED: {status}\nAttempt: {p.Attempt}\nResult: {p.Result}\nSTOP. Return both new diagnostic JSON. Do not run the publisher or repeat this probe.");
            return status == "RECEIVE_DISCOVERY_DIAGNOSTIC_RECORDED_NO_PUSH" ? 0 : 1;
        } catch (Exception ex) {
            Console.Error.WriteLine("REFUSED: " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
            Console.Error.WriteLine("STOP. Preserve all old and new evidence. No push is implemented by this diagnostic. No retry or publication authority.");
            return 1;
        }
    }
}
