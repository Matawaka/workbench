using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Matawaka.V0601PublicationPreflight;
using Matawaka.V0601FixedPublisher;
using Matawaka.V0601DiscoveryDiagnostic;
using Matawaka.V0601PermissionRecheck;

namespace Matawaka.V0601ReconciledPublisher;

internal sealed record SuccessorPlan(RecheckPlan Prior, BoundFile RecheckAttempt, BoundFile RecheckResult,
    string StageRoot, string OutputRoot, string Endpoint)
{
    internal const string Confirmation = "PUBLISH-RECONCILED-V0601-58B9430-C07409C";
    internal string Attempt => Path.Combine(OutputRoot, "attempt-publish-reconciled-v1-" + Prior.Prior.Proof.Accepted.Head + ".json");
    internal string Result => Path.Combine(OutputRoot, "publication-reconciled-v1-" + Prior.Prior.Proof.Accepted.Head + ".json");
    internal string Outcome => Path.Combine(OutputRoot, "outcome-publish-reconciled-v1-" + Prior.Prior.Proof.Accepted.Head + ".json");
    internal static SuccessorPlan Fixed() => new(RecheckPlan.Fixed(),
        new("artifacts/publication-v0601/attempt-permission-recheck-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "30bce27780fb0b3acc26ffdbcb4a674adfe8c2b4114bb74e161b7f158e69329e", 1339),
        new("artifacts/publication-v0601/permission-recheck-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json", "e6c720426ad6b285dc6f078a2fb941f732f54aaf5359b788288173998d40577f", 5471),
        @"K:\Matawaka\Tools\WorkbenchV0601ReconciledPublicationV1",
        @"K:\Matawaka\Workbench\artifacts\publication-v0601", Spec.Remote);
    internal PublishPlan TransportPlan() => new(Prior.Prior.Proof, Path.Combine(Prior.Prior.GitRoot, "cmd", "git.exe"), Endpoint,
        StageRoot, OutputRoot, Prior.Prior.Preflight, Prior.Prior.GitRoot);
    internal BoundFile[] Inputs() => Prior.Prior.Proof.Accepted.Evidence.Values.Concat(new[] {
        Prior.Prior.Preflight, Prior.Prior.PriorAttempt, Prior.Prior.PriorOutcome, Prior.DeniedAttempt,
        Prior.DeniedResult, RecheckAttempt, RecheckResult }).OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray();
}
internal sealed record SuccessorObservation(RecheckObservation Prior, string PermissionStageDigest);

internal static class SuccessorEvidence
{
    internal static V2Snapshot Read(SuccessorPlan p)
    {
        var d = p.Prior.Prior; var original = Reconciliation.Read(p.Prior);
        var a = Safe.Parse(Incident.Bound(d, p.RecheckAttempt)); var r = Safe.Parse(Incident.Bound(d, p.RecheckResult));
        Safe.Eq(a, "Schema", "matawaka.workbench-permission-recheck-attempt/v0.1");
        Safe.Eq(a, "State", "PERMISSION_RECHECK_CONSUMED_NO_RETRY");
        Safe.Eq(a, "ExplicitConfirmation", RecheckPlan.Confirmation);
        Safe.Eq(r, "Schema", "matawaka.workbench-permission-recheck/v0.1");
        Safe.Eq(r, "Status", "PERMISSION_RECHECK_RECORDED_NO_PUSH");
        Safe.Eq(r, "AttemptReceiptSha256", p.RecheckAttempt.Sha256);
        Safe.Eq(r, "SafeFailureCategory", "NONE");
        foreach (var j in new[] { a, r }) {
            Safe.Eq(j, "Endpoint", Spec.Remote);
            Safe.Eq(j, "PreflightSha256", d.Preflight.Sha256);
            Safe.Eq(j, "PriorPublicationAttemptSha256", d.PriorAttempt.Sha256);
            Safe.Eq(j, "PriorPublicationOutcomeSha256", d.PriorOutcome.Sha256);
            Safe.Eq(j, "DeniedDiscoveryAttemptSha256", p.Prior.DeniedAttempt.Sha256);
            Safe.Eq(j, "DeniedDiscoveryResultSha256", p.Prior.DeniedResult.Sha256);
            Safe.Eq(j, "OperatorDeclaration", "CONTENTS_READ_AND_WRITE_ADDED_AFTER_DENIED_DISCOVERY");
            Safe.False(j, "DeclarationIndependentlyVerified", "PriorAttemptRearmed", "PublicationAuthorized", "RetryAuthorized");
        }
        Safe.Need(a.GetProperty("MaxHelperInvocations").GetInt32() == 1 && a.GetProperty("MaxPushInvocations").GetInt32() == 0 &&
            a.GetProperty("MaxFetchInvocations").GetInt32() == 0, "RECHECK_ATTEMPT_BUDGET_DRIFT");
        Safe.Need(r.GetProperty("HelperInvocations").GetInt32() == 1 && r.GetProperty("PushInvocations").GetInt32() == 0 &&
            r.GetProperty("FetchInvocations").GetInt32() == 0, "RECHECK_EFFECT_DRIFT");
        Safe.True(r, "AdvertisedBaselineMatches", "LocalSnapshotAndBothPriorStagesReverified", "NewStageUnchanged");
        Safe.False(r, "WritePermissionProven", "HistoricalCauseRecovered", "ObjectTransferRequested", "RefUpdateRequested",
            "StoredCredentialAccessPerformed", "RawOutputPersisted", "SecretDerivedDigestsPersisted", "SourceMutationPerformed", "GitRefMutationPerformed");
        var probe = r.GetProperty("Probe"); Safe.Eq(probe, "Category", "RECEIVE_DISCOVERY_COMPLETED");
        Safe.Need(probe.GetProperty("ExitCode").GetInt32() == 0, "RECHECK_NOT_SUCCESSFUL");
        Safe.True(probe, "ProcessStarted", "ProcessExited"); Safe.False(probe, "TimedOut", "CleanupIncomplete");
        foreach (var key in new[] { "Stdout", "Stderr" }) {
            var pipe = probe.GetProperty(key); Safe.True(pipe, "EndOfStream"); Safe.False(pipe, "LimitExceeded");
            Safe.Need(pipe.GetProperty("CapturedBytes").GetInt32() is >= 0 and <= 524288, "RECHECK_PIPE_BOUND_DRIFT");
        }
        var refs = JsonSerializer.Deserialize<ListedRefs>(probe.GetProperty("Refs")) ?? throw new InvalidDataException("RECHECK_REFS_MISSING");
        Safe.Need(refs.Main == d.Proof.Accepted.Second && refs.Tag is null && refs.Peeled is null && refs.Historical is null &&
            refs.HistoricalPeeled is null && refs.ObservedRefCount is > 0 and <= 4096, "RECHECK_BASELINE_DRIFT");
        Safe.Need(JsonSerializer.Deserialize<V2Snapshot>(r.GetProperty("Snapshot")) == original, "RECHECK_SNAPSHOT_SUBSTITUTED");
        foreach (var pin in p.Inputs()) Incident.Bound(d, pin);
        return original;
    }
    internal static async Task<SuccessorObservation> Observe(SuccessorPlan p, GitRead git)
    {
        var snapshot = Read(p); var prior = await Reconciliation.Observe(p.Prior, git);
        var r = Safe.Parse(Incident.Bound(p.Prior.Prior, p.RecheckResult));
        Safe.Need(prior.Local.Snapshot == snapshot, "FRESH_SUCCESSOR_SNAPSHOT_DRIFT");
        Safe.Eq(r, "PriorPublicationStageDigest", prior.Local.PriorTransportDigest);
        Safe.Eq(r, "PriorDiscoveryStageDigest", prior.PriorDiscoveryStageDigest);
        Safe.NoReparse(p.Prior.Stage); Safe.Need(Directory.Exists(p.Prior.Stage), "PERMISSION_STAGE_MISSING");
        // Both no-push stages were created by the same qualified two-file empty-context constructor.
        var empty = new Dictionary<string, byte[]> {
            ["discovery.git/HEAD"] = "ref: refs/heads/unborn-diagnostic\n"u8.ToArray(),
            ["discovery.git/config"] = "[core]\n\trepositoryformatversion = 0\n\tbare = true\n\tlogallrefupdates = false\n"u8.ToArray()
        };
        var expected = Safe.Hash(Safe.Utf8.GetBytes(string.Join("\n", empty.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + "\t" + x.Value.Length + "\t" + Safe.Hash(x.Value)))));
        var stage = Files.Digest(p.Prior.Stage); Safe.Need(stage == expected, "PERMISSION_EMPTY_STAGE_BYTES_DRIFT");
        return new(prior, stage);
    }
}

internal sealed record NativeObservation(string Operation, string Category, int? ExitCode, bool ProcessStarted,
    bool ProcessExited, bool TimedOut, bool CleanupIncomplete, PipeObservation Stdout, PipeObservation Stderr)
{
    internal bool Complete => Category == "NATIVE_COMPLETED" && ExitCode == 0 && ProcessStarted && ProcessExited &&
        !TimedOut && !CleanupIncomplete && Stdout.EndOfStream && Stderr.EndOfStream && !Stdout.LimitExceeded && !Stderr.LimitExceeded;
    internal static NativeObservation From(Capture c, string operation)
    {
        var category = c.Failure != "NONE" ? c.Failure : !c.Exited || !c.Stdout.EndOfStream || !c.Stderr.EndOfStream ?
            "CAPTURE_INCOMPLETE" : c.ExitCode == 0 ? "NATIVE_COMPLETED" : Classification.Error(c.Error);
        return new(operation, category, c.ExitCode, c.Started, c.Exited, c.TimedOut, c.CleanupIncomplete, c.Stdout, c.Stderr);
    }
}
internal sealed record ReadObservation(NativeObservation Native, RemoteState? Refs);
internal interface IPublicationTransfer
{
    void Create(); IDisposable Lock();
    Task<ReadObservation> Read(); Task<NativeObservation> Push(string header);
    bool GuardVerified(); void Unchanged();
}
internal sealed class PublicationTransfer : IPublicationTransfer
{
    private readonly IsolatedTransport transport;
    internal PublicationTransfer(SuccessorPlan p) => transport = new(p.TransportPlan());
    public void Create() => transport.Create();
    public IDisposable Lock() { var locks = new ReadLocks(); try { locks.Tree(transport.Repo); return locks; } catch { locks.Dispose(); throw; } }
    public async Task<ReadObservation> Read()
    {
        var r = await transport.Read(); var n = transport.LastObservation ?? throw new InvalidDataException("TRANSPORT_OBSERVATION_MISSING");
        try {
            if (!n.Complete) return new(n, null);
            try { return new(n, IsolatedTransport.Parse(r)); }
            catch (Exception e) when (e is InvalidDataException or DecoderFallbackException) { return new(n with { Category = "REMOTE_REF_OUTPUT_REFUSED" }, null); }
        } finally { Array.Clear(r.Out); Array.Clear(r.Error); }
    }
    public async Task<NativeObservation> Push(string header)
    {
        var r = await transport.Push(header);
        try { return transport.LastObservation ?? throw new InvalidDataException("TRANSPORT_OBSERVATION_MISSING"); }
        finally { Array.Clear(r.Out); Array.Clear(r.Error); }
    }
    public bool GuardVerified() => transport.GuardVerified();
    public void Unchanged() => transport.Unchanged();
}

internal static class ReconciledPublisher
{
    internal const string Success = "EXACT_V0601_RECONCILED_TWO_REF_PUBLICATION_VERIFIED";
    internal static void Confirm(string? value, TimeSpan age)
    {
        Safe.Need(value == SuccessorPlan.Confirmation, "NEW_RECONCILED_PUBLICATION_CONFIRMATION_REQUIRED");
        Safe.Need(age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(5), "RECONCILED_PUBLICATION_PREVIEW_EXPIRED");
    }
    internal static void Unused(SuccessorPlan p)
    {
        foreach (var path in new[] { p.Attempt, p.Result, p.Outcome, p.StageRoot }) {
            Safe.NoReparse(path); Safe.Need(!File.Exists(path) && !Directory.Exists(path), "SUCCESSOR_CONSUMED_OR_PATH_EXISTS_NO_RETRY");
        }
        Safe.Need(new[] { p.Prior.Stage, p.Prior.Prior.Stage, p.Prior.Prior.PriorStage }.All(x =>
            !Path.GetFullPath(x).Equals(Path.GetFullPath(p.StageRoot), StringComparison.OrdinalIgnoreCase)), "OLD_STAGE_REUSE_REFUSED");
    }
    internal static async Task<string> Execute(SuccessorPlan p, SuccessorObservation expected,
        Func<Task<SuccessorObservation>> verify, string confirmation, TimeSpan age, string header,
        string toolSha, string toolDigest, IPublicationTransfer transfer)
    {
        var clock = Stopwatch.StartNew(); Confirm(confirmation, age); Unused(p);
        Safe.Need(SuccessorEvidence.Read(p) == expected.Prior.Local.Snapshot && await verify() == expected, "SUCCESSOR_INPUT_DRIFT");
        Confirm(confirmation, age + clock.Elapsed);
        var inputs = p.Inputs(); var binding = Safe.Hash(Files.Json(inputs)); var original = p.Prior.Prior;
        var attempt = Files.Json(new {
            Schema = "matawaka.workbench-reconciled-publication-attempt/v0.1", State = "NEW_PUBLICATION_ATTEMPT_CONSUMED_NO_RETRY",
            CreatedAt = DateTimeOffset.UtcNow, ExplicitConfirmation = confirmation, NewOneShotPublicationConfirmed = true,
            Endpoint = p.Endpoint, AcceptedHead = original.Proof.Accepted.Head, AcceptedTagObject = original.Proof.TagObject,
            ExpectedOldMain = original.Proof.Accepted.Second, AllowedRefs = new[] { PublishPlan.MainRef, PublishPlan.TagRef },
            Inputs = inputs, InputBindingSha256 = binding, ToolSha256 = toolSha, MinGitTreeManifestDigest = toolDigest,
            PriorPublicationAttemptSha256 = original.PriorAttempt.Sha256, PriorPublicationOutcomeSha256 = original.PriorOutcome.Sha256,
            SuccessfulRecheckSha256 = p.RecheckResult.Sha256, MaxPushInvocations = 1, MaxRemoteReadInvocations = 2,
            AtomicRequired = true, ExactAdvertisedOldMainGuardRequired = true, PriorAttemptRearmed = false,
            HistoricalCauseRecovered = false, HistoricalRemoteEffectsProvenAbsent = false, RetryAuthorized = false,
            CredentialStorageAllowed = false, RawOutputStorageAllowed = false, SourceMutationAllowed = false,
            Note = "New separately confirmed exact publication after audited incident reconciliation. Old attempts remain consumed; this is not historical retry authority or publication success."
        });
        Files.New(p.Attempt, attempt);
        ReadObservation? before = null, after = null; NativeObservation? push = null;
        bool mayHaveStarted = false, guard = false, localStable = false, stageStable = false;
        int reads = 0, pushes = 0; string reason = "NONE"; IDisposable? locks = null;
        try {
            transfer.Create(); locks = transfer.Lock(); Confirm(confirmation, age + clock.Elapsed);
            reads++; before = await transfer.Read();
            Safe.Need(before.Native.Complete && before.Refs is not null, "PRE_PUBLICATION_READ_NOT_VERIFIED");
            IsolatedTransport.Before(p.TransportPlan(), before.Refs!);
            Safe.Need(SuccessorEvidence.Read(p) == expected.Prior.Local.Snapshot && await verify() == expected, "LOCAL_DRIFT_BEFORE_SUCCESSOR_PUSH");
            Confirm(confirmation, age + clock.Elapsed);
            Safe.Need(Safe.Hash(Safe.Read(p.Attempt)) == Safe.Hash(attempt), "SUCCESSOR_ATTEMPT_BYTES_DRIFT");
            pushes++; mayHaveStarted = true; push = await transfer.Push(header);
            guard = transfer.GuardVerified();
            // One post-attempt observation only; never another push. Do not proceed with an incompletely cleaned process.
            if (!push.CleanupIncomplete) { reads++; after = await transfer.Read(); }
            localStable = SuccessorEvidence.Read(p) == expected.Prior.Local.Snapshot && await verify() == expected;
            transfer.Unchanged(); stageStable = true;
            Safe.Need(push.Complete && guard && after is not null && after.Native.Complete && after.Refs is not null &&
                IsolatedTransport.Target(p.TransportPlan(), after.Refs) && localStable, "SUCCESSOR_PUBLICATION_OR_READBACK_NOT_VERIFIED");
            Files.New(p.Result, Files.Json(new {
                Schema = "matawaka.workbench-reconciled-publication-receipt/v0.1", Status = Success, ObservedAt = DateTimeOffset.UtcNow,
                Version = "0.60.1", Endpoint = p.Endpoint, AcceptedHead = original.Proof.Accepted.Head,
                AcceptedTag = PublishPlan.TagRef, AcceptedTagObject = original.Proof.TagObject,
                AttemptReceiptSha256 = Safe.Hash(attempt), Inputs = inputs, InputBindingSha256 = binding,
                ToolSha256 = toolSha, MinGitTreeManifestDigest = toolDigest, Snapshot = expected.Prior.Local.Snapshot,
                PriorPublicationStageDigest = expected.Prior.Local.PriorTransportDigest,
                PriorDiscoveryStageDigest = expected.Prior.PriorDiscoveryStageDigest, PriorPermissionStageDigest = expected.PermissionStageDigest,
                RemoteBefore = before.Refs, RemoteAfter = after!.Refs, NativeBefore = before.Native, NativePush = push, NativeAfter = after.Native,
                PushInvocations = pushes, RemoteReadInvocations = reads, AtomicRequested = true, ExactAdvertisedOldMainGuardVerified = guard,
                LocalSnapshotAndAllThreePriorStagesReverified = localStable, NewTransportPreservedExceptGuard = stageStable,
                NetworkReadPerformed = true, RemoteWritePerformed = true, NewOneShotPublicationConfirmed = true,
                CredentialUsedForExactPushOnly = true, CredentialStored = false, RawOutputPersisted = false, SecretDerivedDigestsPersisted = false,
                SourceMutationPerformed = false, SourceGitRefMutationPerformed = false, SourceConfigMutationPerformed = false,
                SourceIndexMutationPerformed = false, SourceObjectMutationPerformed = false, OriginalReceiptsUnchanged = true,
                CommitOrTagRecreated = false, PriorAttemptRearmed = false, HistoricalCauseRecovered = false,
                HistoricalRemoteEffectsProvenAbsent = false, RetryAuthorized = false, FurtherPublicationAuthorized = false,
                OsNetworkIsolationProven = false, OsConcurrencyExclusionProven = false, EntireRuntimeBinaryTreeVerified = false,
                Note = "This new exact two-ref publication is verified by its own push, guard, readback and local observations. Prior failures remain unchanged. Independent GitHub commit/tree/parent/tag readback remains required for external closure."
            }));
            return Success;
        } catch (Exception ex) {
            reason = ex is InvalidDataException ? "LOCAL_OR_PUBLICATION_CONTRACT_REFUSED" : "SUCCESSOR_INTERNAL_FAILURE";
            // Only bounded local preservation checks on this branch; no hidden extra remote read.
            try { localStable = SuccessorEvidence.Read(p) == expected.Prior.Local.Snapshot && await verify() == expected; } catch { localStable = false; }
            try { transfer.Unchanged(); stageStable = true; } catch { stageStable = false; }
            var status = mayHaveStarted ? "RECONCILED_PUBLICATION_OUTCOME_UNVERIFIED_NO_RETRY" : "RECONCILED_PUBLICATION_STOPPED_BEFORE_PUSH_NO_RETRY";
            try { Files.New(p.Outcome, Files.Json(new {
                Schema = "matawaka.workbench-reconciled-publication-outcome/v0.1", Status = status, ObservedAt = DateTimeOffset.UtcNow,
                SafeFailureCategory = reason, AttemptReceiptSha256 = Safe.Hash(attempt), InputBindingSha256 = binding,
                SuccessfulRecheckSha256 = p.RecheckResult.Sha256, PriorPublicationOutcomeSha256 = original.PriorOutcome.Sha256,
                ToolSha256 = toolSha, NativeBefore = before?.Native, NativePush = push, NativeAfter = after?.Native,
                RemoteBefore = before?.Refs, RemoteAfter = after?.Refs, PushInvocations = pushes, RemoteReadInvocations = reads,
                PushMayHaveStarted = mayHaveStarted, ExactAdvertisedOldMainGuardObserved = guard,
                LocalSnapshotAndAllThreePriorStagesReverified = localStable, NewTransportPreservedExceptGuard = stageStable,
                PublicationSuccessClaimed = false, RemoteMutationProvenAbsent = false, PriorAttemptRearmed = false,
                HistoricalCauseRecovered = false, HistoricalRemoteEffectsProvenAbsent = false, RetryAuthorized = false,
                RawOutputPersisted = false, SecretDerivedDigestsPersisted = false, CredentialStored = false,
                Note = "STOP. Preserve all old/new records and stages. Native observations contain allowlisted categories, sizes and completion flags only. No retry, reset, rollback or manual push."
            })); } catch { /* Durable consumed attempt remains the minimum evidence; never overwrite a partial result. */ }
            throw new InvalidDataException(status);
        } finally { locks?.Dispose(); }
    }
}

internal static class SuccessorEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Safe.Need(args.Length == 0, "ARGUMENTS_NOT_ACCEPTED"); Safe.Need(OperatingSystem.IsWindows(), "WINDOWS_OPERATOR_HOST_REQUIRED");
            var p = SuccessorPlan.Fixed(); ReconciledPublisher.Unused(p);
            var self = Environment.ProcessPath ?? throw new InvalidDataException("SELF_PATH_MISSING"); Safe.NoReparse(self);
            Safe.Need(!Path.GetFullPath(self).StartsWith(Path.GetFullPath(p.Prior.Prior.Proof.Accepted.Root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "SUCCESSOR_MUST_BE_OUTSIDE_REPOSITORY");
            using var toolLocks = new ReadLocks(); toolLocks.Hold(self);
            var toolSha = Safe.Hash(Safe.Read(self, 128 * 1024 * 1024));
            var toolDigest = ToolTree.Verify(p.Prior.Prior.GitRoot, ToolTree.Embedded(), toolLocks);
            var git = new GitRead(V2.GitPath, p.Prior.Prior.Proof.Accepted.Root, V2.GitSha);
            Safe.Need(await git.Text("--version") == "git version 2.55.0.windows.4", "PINNED_GIT_VERSION_MISMATCH");
            var expected = await SuccessorEvidence.Observe(p, git);
            Safe.Eq(Safe.Parse(Incident.Bound(p.Prior.Prior, p.RecheckResult)), "MinGitTreeManifestDigest", toolDigest);
            Console.WriteLine("LOCAL RECONCILED PUBLICATION PREVIEW - NO NETWORK OR CREDENTIAL ACCESS YET");
            Console.WriteLine($"Repository: {p.Prior.Prior.Proof.Accepted.Root}\nDestination: {p.Endpoint}\nmain: {p.Prior.Prior.Proof.Accepted.Second} -> {p.Prior.Prior.Proof.Accepted.Head}\nTag: ABSENT -> {p.Prior.Prior.Proof.TagObject}\nTag ref: {PublishPlan.TagRef}\nSuccessful recheck SHA-256: {p.RecheckResult.Sha256}");
            Console.WriteLine("NEW one-shot publication, not a replay of V1. Preserve all 15 old records and 3 old stages. After confirmation: new attempt/result, new isolated object copy, at most TWO public reads and ONE atomic exact two-ref push. No force, reset, tag recreation, API writes, Update/Accept or automatic retry. A lost response may leave remote effects: STOP.");
            Console.WriteLine("Enter exactly: " + SuccessorPlan.Confirmation);
            var timer = Stopwatch.StartNew(); var value = Console.ReadLine();
            if (value != SuccessorPlan.Confirmation) { Console.WriteLine("CANCELLED_NO_EFFECTS"); return 2; }
            ReconciledPublisher.Confirm(value, timer.Elapsed); var header = Credential.ReadLocal(); ReconciledPublisher.Confirm(value, timer.Elapsed);
            using var locks = new ReadLocks(); locks.Tree(Safe.Under(p.Prior.Prior.Proof.Accepted.Root, ".git"));
            foreach (var stage in new[] { p.Prior.Prior.PriorStage, p.Prior.Prior.Stage, p.Prior.Stage }) locks.Tree(stage);
            foreach (var pin in p.Inputs()) locks.Hold(Safe.Under(p.Prior.Prior.Proof.Accepted.Root, pin.RelativePath));
            var status = await ReconciledPublisher.Execute(p, expected, () => SuccessorEvidence.Observe(p, git), value,
                timer.Elapsed, header, toolSha, toolDigest, new PublicationTransfer(p)); header = "";
            Console.WriteLine($"COMPLETED: {status}\nAttempt: {p.Attempt}\nResult: {p.Result}\nSTOP. Return both new JSON for independent verification; do not repeat.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine("REFUSED: " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
            Console.Error.WriteLine("STOP. Preserve attempt/outcome and every old/new stage. A push may have occurred; no retry, cleanup, reset or manual push.");
            return 1;
        }
    }
}
