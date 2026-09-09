using System.Diagnostics;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;
using Matawaka.V0601DiscoveryDiagnostic;

namespace Matawaka.V0601PermissionRecheck;

// A new narrowly confirmed observation after an operator-reported external permission change.
// Requires the consumed publication AND consumed denied discovery evidence. Neither is rearmed.
internal sealed record RecheckPlan(DiagnosticPlan Prior, BoundFile DeniedAttempt, BoundFile DeniedResult,
    string Stage, string OutputRoot)
{
    internal const string Confirmation = "RECHECK-CONTENTS-WRITE-V0601-NO-PUSH";
    internal string Attempt => Path.Combine(OutputRoot, "attempt-permission-recheck-v1-" + Prior.Proof.Accepted.Head + ".json");
    internal string Result => Path.Combine(OutputRoot, "permission-recheck-v1-" + Prior.Proof.Accepted.Head + ".json");
    internal static RecheckPlan Fixed() => new(DiagnosticPlan.Fixed(),
        new("artifacts/publication-v0601/attempt-discovery-diagnostic-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "7eed29fac84985b6d846759250027fd3f8cb30e055f75ced3931d97a023c79ea", 1107),
        new("artifacts/publication-v0601/discovery-diagnostic-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "d755106034cfc470a10d6d859c138f308bc0334f77453f159e3a61a9cc014158", 5004),
        @"K:\Matawaka\Tools\WorkbenchV0601PermissionRecheckV1",
        @"K:\Matawaka\Workbench\artifacts\publication-v0601");
}
internal sealed record RecheckObservation(LocalObservation Local, string PriorDiscoveryStageDigest);

internal static class Reconciliation
{
    internal static V2Snapshot Read(RecheckPlan p)
    {
        var original = Incident.Read(p.Prior);
        var attempt = Safe.Parse(Incident.Bound(p.Prior, p.DeniedAttempt));
        var result = Safe.Parse(Incident.Bound(p.Prior, p.DeniedResult));
        Safe.Eq(attempt, "Schema", "matawaka.workbench-receive-discovery-attempt/v0.1");
        Safe.Eq(attempt, "State", "DIAGNOSTIC_ATTEMPT_CONSUMED_NO_RETRY");
        Safe.Eq(attempt, "Operation", "RECEIVE_PACK_REFERENCE_DISCOVERY_ONLY");
        Safe.Eq(result, "Schema", "matawaka.workbench-receive-discovery-diagnostic/v0.1");
        Safe.Eq(result, "Status", "RECEIVE_DISCOVERY_DIAGNOSTIC_RECORDED_NO_PUSH");
        Safe.Eq(result, "SafeFailureCategory", "NONE");
        Safe.Eq(result, "DiagnosticAttemptSha256", p.DeniedAttempt.Sha256);
        foreach (var j in new[] { attempt, result }) {
            Safe.Eq(j, "Endpoint", Spec.Remote);
            Safe.Eq(j, "PriorPublicationAttemptSha256", p.Prior.PriorAttempt.Sha256);
            Safe.Eq(j, "PriorPublicationOutcomeSha256", p.Prior.PriorOutcome.Sha256);
            Safe.Eq(j, "PreflightSha256", p.Prior.Preflight.Sha256);
            Safe.False(j, "PublicationAuthorized", "PriorAttemptRearmed", "RetryAuthorized");
        }
        Safe.Need(attempt.GetProperty("MaxHelperInvocations").GetInt32() == 1 &&
            attempt.GetProperty("MaxPushInvocations").GetInt32() == 0 && attempt.GetProperty("MaxFetchInvocations").GetInt32() == 0,
            "PRIOR_DISCOVERY_BUDGET_MISMATCH");
        Safe.Need(result.GetProperty("HelperInvocations").GetInt32() == 1 &&
            result.GetProperty("PushInvocations").GetInt32() == 0 && result.GetProperty("FetchInvocations").GetInt32() == 0,
            "PRIOR_DISCOVERY_EFFECT_MISMATCH");
        Safe.True(result, "LocalSnapshotAndPriorTransportReverified", "NewDiagnosticStageUnchanged");
        Safe.False(result, "HistoricalCauseRecovered", "RefUpdateRequested", "ObjectTransferRequested");
        var probe = result.GetProperty("Probe");
        Safe.Eq(probe, "Category", "ACCESS_FORBIDDEN");
        Safe.Need(probe.GetProperty("ExitCode").GetInt32() == 128 && probe.GetProperty("Refs").ValueKind == JsonValueKind.Null,
            "PRIOR_DENIAL_SHAPE_MISMATCH");
        Safe.True(probe, "ProcessStarted", "ProcessExited"); Safe.False(probe, "TimedOut", "CleanupIncomplete");
        foreach (var key in new[] { "Stdout", "Stderr" }) {
            Safe.True(probe.GetProperty(key), "EndOfStream"); Safe.False(probe.GetProperty(key), "LimitExceeded");
        }
        var previous = JsonSerializer.Deserialize<V2Snapshot>(result.GetProperty("Snapshot"));
        Safe.Need(previous == original, "PRIOR_DISCOVERY_SNAPSHOT_MISMATCH");
        return original;
    }
    internal static async Task<RecheckObservation> Observe(RecheckPlan p, GitRead git)
    {
        var expected = Read(p); var local = await Incident.Observe(p.Prior, git);
        Safe.Need(local.Snapshot == expected, "RECHECK_SNAPSHOT_DRIFT");
        var denied = Safe.Parse(Incident.Bound(p.Prior, p.DeniedResult));
        Safe.Need(local.PriorTransportDigest == denied.GetProperty("PriorTransportDigest").GetString(), "PRIOR_PUBLICATION_STAGE_DRIFT");
        Safe.NoReparse(p.Prior.Stage); Safe.Need(Directory.Exists(p.Prior.Stage), "PRIOR_DISCOVERY_STAGE_MISSING");
        return new(local, Files.Digest(p.Prior.Stage));
    }
}

internal static class PermissionRecheck
{
    internal const string Recorded = "PERMISSION_RECHECK_RECORDED_NO_PUSH";
    internal static void Confirm(string? value, TimeSpan age)
    {
        Safe.Need(value == RecheckPlan.Confirmation, "NEW_PERMISSION_RECHECK_CONFIRMATION_REQUIRED");
        Safe.Need(age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(5), "PERMISSION_RECHECK_PREVIEW_EXPIRED");
    }
    internal static void Unused(RecheckPlan p)
    {
        foreach (var file in new[] { p.Attempt, p.Result, p.Stage }) {
            Safe.NoReparse(file); Safe.Need(!File.Exists(file) && !Directory.Exists(file), "PERMISSION_RECHECK_CONSUMED_NO_OVERWRITE");
        }
    }
    internal static async Task<string> Execute(RecheckPlan p, RecheckObservation expected,
        Func<Task<RecheckObservation>> observe, string confirmation, TimeSpan age,
        Func<Task<Capture>> probe, string selfSha, string toolDigest)
    {
        var clock = Stopwatch.StartNew(); Confirm(confirmation, age); Unused(p);
        Safe.Need(ReadMatches(p, expected) && await observe() == expected, "RECHECK_INPUT_DRIFT");
        Confirm(confirmation, age + clock.Elapsed);
        var attempt = Files.Json(new {
            Schema = "matawaka.workbench-permission-recheck-attempt/v0.1", State = "PERMISSION_RECHECK_CONSUMED_NO_RETRY",
            CreatedAt = DateTimeOffset.UtcNow, ExplicitConfirmation = confirmation, Endpoint = Spec.Remote,
            OperatorDeclaration = "CONTENTS_READ_AND_WRITE_ADDED_AFTER_DENIED_DISCOVERY", DeclarationIndependentlyVerified = false,
            PreflightSha256 = p.Prior.Preflight.Sha256, PriorPublicationAttemptSha256 = p.Prior.PriorAttempt.Sha256,
            PriorPublicationOutcomeSha256 = p.Prior.PriorOutcome.Sha256, DeniedDiscoveryAttemptSha256 = p.DeniedAttempt.Sha256,
            DeniedDiscoveryResultSha256 = p.DeniedResult.Sha256, ToolSha256 = selfSha, MinGitTreeManifestDigest = toolDigest,
            MaxHelperInvocations = 1, MaxPushInvocations = 0, MaxFetchInvocations = 0,
            CredentialStorageAllowed = false, PriorAttemptRearmed = false, PublicationAuthorized = false, RetryAuthorized = false
        });
        Files.New(p.Attempt, attempt);
        int calls = 0; string reason = "NONE"; ProbeObservation? observation = null;
        bool stable = false, stageStable = false;
        try {
            Diagnostic.CreateStage(p.Stage); var digest = Files.Digest(p.Stage);
            using var locks = new ReadLocks(); locks.Tree(p.Stage);
            Safe.Need(ReadMatches(p, expected) && await observe() == expected && Safe.Hash(Safe.Read(p.Attempt)) == Safe.Hash(attempt), "RECHECK_INPUT_DRIFT");
            Confirm(confirmation, age + clock.Elapsed);
            calls++; var capture = await probe();
            try { observation = Classification.Observe(capture); }
            finally { Array.Clear(capture.Output); Array.Clear(capture.Error); }
            stable = ReadMatches(p, expected) && await observe() == expected;
            stageStable = Files.Digest(p.Stage) == digest;
            if (!stable || !stageStable) reason = "RECHECK_POST_OBSERVATION_DRIFT";
        } catch (Exception ex) {
            reason = ex is InvalidDataException ? "RECHECK_LOCAL_CONTRACT_REFUSED" : "RECHECK_INTERNAL_FAILURE";
        }
        var status = reason == "NONE" ? Recorded : "PERMISSION_RECHECK_INCOMPLETE_NO_PUSH_NO_RETRY";
        var baseline = observation?.Refs is { } r && r.Main == p.Prior.Proof.Accepted.Second &&
            r.Tag is null && r.Peeled is null && r.Historical is null && r.HistoricalPeeled is null;
        Files.New(p.Result, Files.Json(new {
            Schema = "matawaka.workbench-permission-recheck/v0.1", Status = status, ObservedAt = DateTimeOffset.UtcNow,
            Endpoint = Spec.Remote, AttemptReceiptSha256 = Safe.Hash(attempt), ToolSha256 = selfSha, MinGitTreeManifestDigest = toolDigest,
            PreflightSha256 = p.Prior.Preflight.Sha256, PriorPublicationAttemptSha256 = p.Prior.PriorAttempt.Sha256,
            PriorPublicationOutcomeSha256 = p.Prior.PriorOutcome.Sha256, DeniedDiscoveryAttemptSha256 = p.DeniedAttempt.Sha256,
            DeniedDiscoveryResultSha256 = p.DeniedResult.Sha256, OperatorDeclaration = "CONTENTS_READ_AND_WRITE_ADDED_AFTER_DENIED_DISCOVERY",
            DeclarationIndependentlyVerified = false, Probe = observation, SafeFailureCategory = reason,
            HelperInvocations = calls, PushInvocations = 0, FetchInvocations = 0,
            AdvertisedBaselineMatches = baseline, Snapshot = expected.Local.Snapshot,
            PriorPublicationStageDigest = expected.Local.PriorTransportDigest, PriorDiscoveryStageDigest = expected.PriorDiscoveryStageDigest,
            LocalSnapshotAndBothPriorStagesReverified = stable, NewStageUnchanged = stageStable,
            StoredCredentialAccessPerformed = false, RawOutputPersisted = false, SecretDerivedDigestsPersisted = false,
            SourceMutationPerformed = false, GitRefMutationPerformed = false, ObjectTransferRequested = false, RefUpdateRequested = false,
            PriorAttemptRearmed = false, PublicationAuthorized = false, RetryAuthorized = false, HistoricalCauseRecovered = false,
            WritePermissionProven = false, OsNetworkIsolationProven = false, OsConcurrencyExclusionProven = false,
            Note = "New observation after operator-reported permission correction. Original denied discovery and unverified publication stay immutable and consumed. A matching advertisement does not prove write/atomic/hook/branch-rule admission. No push/fetch/update commands or object uploads are implemented; no automatic continuation."
        }));
        return status;
    }
    private static bool ReadMatches(RecheckPlan p, RecheckObservation expected) => Reconciliation.Read(p) == expected.Local.Snapshot;
}

internal static class PermissionRecheckEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length == 0, "ARGUMENTS_NOT_ACCEPTED");
            Safe.Need(OperatingSystem.IsWindows(), "WINDOWS_OPERATOR_HOST_REQUIRED");
            var p = RecheckPlan.Fixed(); PermissionRecheck.Unused(p);
            var self = Environment.ProcessPath ?? throw new InvalidDataException("SELF_PATH_MISSING"); Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(p.Prior.Proof.Accepted.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "RECHECK_MUST_BE_OUTSIDE_REPOSITORY");
            using var toolLocks = new ReadLocks(); toolLocks.Hold(self);
            var selfSha = Safe.Hash(Safe.Read(self, 128 * 1024 * 1024));
            var toolDigest = ToolTree.Verify(p.Prior.GitRoot, ToolTree.Embedded(), toolLocks);
            var git = new GitRead(V2.GitPath, p.Prior.Proof.Accepted.Root, V2.GitSha);
            Safe.Need(await git.Text("--version") == "git version 2.55.0.windows.4", "PINNED_GIT_VERSION_MISMATCH");
            var expected = await Reconciliation.Observe(p, git);
            Console.WriteLine("LOCAL PERMISSION RECHECK PREVIEW - NO NETWORK OR CREDENTIAL ACCESS YET");
            Console.WriteLine($"Repository: {p.Prior.Proof.Accepted.Root}\nEndpoint: {Spec.Remote}\nAccepted HEAD: {p.Prior.Proof.Accepted.Head}\nTag object: {p.Prior.Proof.TagObject}\nPrior denied discovery SHA-256: {p.DeniedResult.Sha256}");
            Console.WriteLine("Confirm only if Contents is now Read and write for this repository. This declaration is recorded as operator-reported, not independently verified. ONE new no-push receive discovery; new attempt/result and EMPTY context. No old attempt reuse, no write-permission or publication-success claim.");
            Console.WriteLine("Enter exactly: " + RecheckPlan.Confirmation);
            var timer = Stopwatch.StartNew(); var value = Console.ReadLine();
            if (value != RecheckPlan.Confirmation) { Console.WriteLine("CANCELLED_NO_EFFECTS"); return 2; }
            PermissionRecheck.Confirm(value, timer.Elapsed); var header = Credential.ReadLocal(); PermissionRecheck.Confirm(value, timer.Elapsed);
            using var locks = new ReadLocks(); locks.Tree(Safe.Under(p.Prior.Proof.Accepted.Root, ".git"));
            locks.Tree(p.Prior.PriorStage); locks.Tree(p.Prior.Stage);
            foreach (var pin in new[] { p.Prior.Preflight, p.Prior.PriorAttempt, p.Prior.PriorOutcome, p.DeniedAttempt, p.DeniedResult }.Concat(p.Prior.Proof.Accepted.Evidence.Values))
                locks.Hold(Safe.Under(p.Prior.Proof.Accepted.Root, pin.RelativePath));
            var status = await PermissionRecheck.Execute(p, expected, () => Reconciliation.Observe(p, git), value, timer.Elapsed,
                () => BoundedProcess.Run(DiscoveryTransport.Start(p.Prior.GitRoot, p.Stage, Spec.Remote, header), DiscoveryTransport.Input), selfSha, toolDigest);
            header = "";
            Console.WriteLine($"COMPLETED: {status}\nAttempt: {p.Attempt}\nResult: {p.Result}\nSTOP. Return both new JSON. Do not repeat any tool or run the publisher.");
            return status == PermissionRecheck.Recorded ? 0 : 1;
        } catch (Exception ex) {
            Console.Error.WriteLine("REFUSED: " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
            Console.Error.WriteLine("STOP. Preserve every old/new record and stage. No push implemented; no retry authority."); return 1;
        }
    }
}
