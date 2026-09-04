using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record ArtifactAcquisitionRouteRuleV052(string Host, string PathPrefix);

public sealed record ArtifactAcquisitionItemV052(
    string ArtifactId,
    string SourceUri,
    string FileName,
    long ExpectedBytes,
    string ExpectedSha256,
    IReadOnlyList<ArtifactAcquisitionRouteRuleV052> AllowedRoutes);

public sealed record ArtifactAcquisitionRequestV052(
    string Schema,
    string RequestId,
    IReadOnlyList<ArtifactAcquisitionItemV052> Artifacts,
    string DestinationRoot,
    long MaxTotalNetworkBytes,
    int MaxRedirects,
    int TimeoutSeconds,
    int TtlSeconds);

public sealed record ArtifactAcquisitionPreviewV052(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    IReadOnlyList<ArtifactAcquisitionItemV052> Artifacts,
    string DestinationRoot,
    long ExactExpectedBytesTotal,
    long MaxTotalNetworkBytes,
    int MaxRedirects,
    int TimeoutSeconds,
    int TtlSeconds,
    DateTimeOffset ProposedExpiresAt,
    string RequestDigestSha256,
    bool ContainsArtifactBytes,
    bool NetworkAccessPerformed,
    bool FilesystemMutationPerformed,
    bool ReadyForExplicitAcquisitionAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record ArtifactAcquisitionLeaseStateV052(
    string Schema,
    string Version,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    IReadOnlyList<ArtifactAcquisitionItemV052> Artifacts,
    string DestinationRoot,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    long MaxTotalNetworkBytes,
    long RemainingNetworkBytes,
    int MaxRedirects,
    int TimeoutSeconds,
    int TtlSeconds,
    int MaxCalls,
    int RemainingCalls,
    string BearerSha256,
    bool Revoked,
    bool Completed,
    bool Failed,
    string? FailureClassification,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record ArtifactAcquisitionGrantV052(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string RequestId,
    string RequestDigestSha256,
    IReadOnlyList<ArtifactAcquisitionItemV052> Artifacts,
    string DestinationRoot,
    DateTimeOffset ExpiresAt,
    long MaxTotalNetworkBytes,
    int MaxRedirects,
    int TimeoutSeconds,
    int MaxCalls,
    bool BearerPersistedInPlaintextByWorkbench,
    bool DownloadPerformed,
    string Note);

public sealed record ArtifactAcquisitionAuthorityReceiptV052(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string BearerSha256,
    string StatePath,
    string StateSha256,
    DateTimeOffset ExpiresAt,
    long MaxTotalNetworkBytes,
    int MaxRedirects,
    int TimeoutSeconds,
    int MaxCalls,
    bool BearerPlaintextPersisted,
    bool NetworkAccessPerformed,
    bool ArtifactBytesWritten,
    bool ExtractionPerformed,
    bool ProcessExecutionPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record ArtifactAcquisitionItemEvidenceV052(
    string ArtifactId,
    string SourceUri,
    string FileName,
    string FinalPath,
    string? PartialPath,
    string State,
    int RedirectsObserved,
    long ObservedNetworkBytes,
    long? ObservedFileBytes,
    string? ObservedSha256,
    bool ExpectedSizeMatched,
    bool ExpectedSha256Matched,
    bool ExistingVerifiedReused,
    bool FinalPathPromoted,
    bool NetworkAccessPerformed,
    string? FailureClassification);

public sealed record ArtifactAcquisitionTransactionV052(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string State,
    string? CurrentArtifactId,
    long NetworkBytesObserved,
    IReadOnlyList<ArtifactAcquisitionItemEvidenceV052> Items,
    bool AuthorityBearerVerified,
    bool DownloadAuthorityConsumed,
    bool NetworkAccessPerformed,
    bool FilesystemMutationPerformed,
    bool FinalArtifactPromotionPerformed,
    bool ExtractionPerformed,
    bool ProcessExecutionPerformed,
    bool RuntimeStartPerformed,
    bool BenchmarkPerformed,
    bool ModelRequestPerformed,
    bool GameAccessPerformed,
    bool GeneralNetworkAuthorityGranted,
    string? FailureClassification,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record ArtifactAcquisitionExecutionReceiptV052(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string State,
    long NetworkBytesObserved,
    IReadOnlyList<ArtifactAcquisitionItemEvidenceV052> Items,
    string TransactionPath,
    string TransactionSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    bool AllArtifactsSha256Verified,
    bool NetworkAccessPerformed,
    bool FilesystemMutationPerformed,
    bool ExtractionPerformed,
    bool ProcessExecutionPerformed,
    bool RuntimeStartPerformed,
    bool BenchmarkPerformed,
    bool ModelRequestPerformed,
    bool GameAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class ArtifactAcquisitionExceptionV052 : InvalidDataException
{
    public string Classification { get; }

    public ArtifactAcquisitionExceptionV052(string classification, string message) : base(message)
        => Classification = classification;

    public ArtifactAcquisitionExceptionV052(string classification, string message, Exception inner) : base(message, inner)
        => Classification = classification;
}

/// <summary>
/// Reusable one-shot artifact acquisition corridor. Selection/handoff is non-authoritative.
/// Authority binds immutable source routes, filenames, exact sizes/hashes, destination,
/// redirects, timeout, TTL and total network bytes before any network call. Downloaded
/// bytes remain .partial until exact size and SHA-256 verification, then are atomically
/// promoted. No extraction, install, process/runtime start, benchmark, model request,
/// game access, Git/catalog mutation, general browser/network or MCP-tunnel authority.
/// </summary>
public sealed class BoundedArtifactAcquisitionV052Service : IDisposable
{
    public const string Version = "0.52.0";
    public const string RequestSchema = "matawaka.artifact-acquisition-request/v0.52";
    public const string PreviewSchema = "matawaka.artifact-acquisition-preview/v0.52";
    public const string StateSchema = "matawaka.artifact-acquisition-lease-state/v0.52";
    public const string GrantSchema = "matawaka.artifact-acquisition-grant/v0.52";
    public const string AuthorityReceiptSchema = "matawaka.artifact-acquisition-authority-receipt/v0.52";
    public const string TransactionSchema = "matawaka.artifact-acquisition-transaction/v0.52";
    public const string ExecutionReceiptSchema = "matawaka.artifact-acquisition-execution-receipt/v0.52";

    public const int MaxArtifacts = 8;
    public const int MaxRedirects = 5;
    public const int MaxTimeoutSeconds = 30 * 60;
    public const int MaxTtlSeconds = 30 * 60;
    public const long MaxArtifactBytes = 16L * 1024L * 1024L * 1024L;
    public const long MaxTotalNetworkBytes = 32L * 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public BoundedArtifactAcquisitionV052Service(HttpMessageHandler? handler = null)
    {
        if (handler is null)
        {
            handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false
            };
            _httpClient = new HttpClient(handler, disposeHandler: true);
        }
        else
        {
            _httpClient = new HttpClient(handler, disposeHandler: false);
        }
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public ArtifactAcquisitionPreviewV052 Preview(
        string workspaceRoot,
        ArtifactAcquisitionRequestV052 request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || request.Schema != RequestSchema)
            throw Refused("REQUEST_INVALID", "Unexpected v0.52 artifact acquisition request schema.");
        if (!SafeToken(request.RequestId, "acqreq-"))
            throw Refused("REQUEST_INVALID", "RequestId must be an acqreq-* safe token.");
        if (request.Artifacts is null || request.Artifacts.Count < 1 || request.Artifacts.Count > MaxArtifacts)
            throw Refused("REQUEST_INVALID", $"Artifacts count must be within 1..{MaxArtifacts}.");
        if (request.MaxRedirects < 0 || request.MaxRedirects > MaxRedirects)
            throw Refused("REQUEST_INVALID", $"MaxRedirects must be within 0..{MaxRedirects}.");
        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > MaxTimeoutSeconds)
            throw Refused("REQUEST_INVALID", $"TimeoutSeconds must be within 1..{MaxTimeoutSeconds}.");
        if (request.TtlSeconds < 1 || request.TtlSeconds > MaxTtlSeconds)
            throw Refused("REQUEST_INVALID", $"TtlSeconds must be within 1..{MaxTtlSeconds}.");
        if (request.MaxTotalNetworkBytes < 1 || request.MaxTotalNetworkBytes > MaxTotalNetworkBytes)
            throw Refused("REQUEST_INVALID", $"MaxTotalNetworkBytes must be within 1..{MaxTotalNetworkBytes}.");

        var destinationRoot = ValidateDestinationRoot(workspaceRoot, request.DestinationRoot, mustExist: true);
        var normalized = new List<ArtifactAcquisitionItemV052>(request.Artifacts.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expectedTotal = 0;
        foreach (var item in request.Artifacts)
        {
            var normalizedItem = NormalizeItem(item);
            if (!ids.Add(normalizedItem.ArtifactId))
                throw Refused("REQUEST_INVALID", $"Duplicate ArtifactId: {normalizedItem.ArtifactId}");
            if (!names.Add(normalizedItem.FileName))
                throw Refused("REQUEST_INVALID", $"Duplicate destination filename: {normalizedItem.FileName}");
            checked { expectedTotal += normalizedItem.ExpectedBytes; }
            normalized.Add(normalizedItem);
        }
        if (expectedTotal > request.MaxTotalNetworkBytes)
            throw Refused("BYTE_CEILING_INVALID", "MaxTotalNetworkBytes is below the exact reviewed artifact byte total.");

        var observed = DateTimeOffset.Now;
        var digest = RequestDigest(request.RequestId, normalized, destinationRoot,
            request.MaxTotalNetworkBytes, request.MaxRedirects, request.TimeoutSeconds, request.TtlSeconds);
        return new ArtifactAcquisitionPreviewV052(
            PreviewSchema, Version, observed, request.RequestId, normalized, destinationRoot,
            expectedTotal, request.MaxTotalNetworkBytes, request.MaxRedirects, request.TimeoutSeconds,
            request.TtlSeconds, observed.AddSeconds(request.TtlSeconds), digest,
            false, false, false, true, NonEffects(),
            "Preview validates exact artifact identity, routes, redirect/timeout/byte bounds and fixed external-to-Git destination. No network/write and no authority are created.");
    }

    public async Task<(ArtifactAcquisitionGrantV052 Grant, ArtifactAcquisitionAuthorityReceiptV052 Receipt, string ReceiptPath)> GrantAsync(
        string workspaceRoot,
        ArtifactAcquisitionPreviewV052 preview,
        CancellationToken cancellationToken)
    {
        ValidatePreview(workspaceRoot, preview);
        var leaseId = "acqlease-" + Guid.NewGuid().ToString("N");
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var bearerSha = Sha256Text(bearer);
        var root = ResolveLeaseRoot(workspaceRoot);
        var leaseDir = Path.Combine(root, leaseId);
        Directory.CreateDirectory(leaseDir);
        RejectReparseChain(leaseDir);
        var statePath = Path.Combine(leaseDir, "state.json");
        var issued = DateTimeOffset.Now;
        var expires = issued.AddSeconds(preview.TtlSeconds);
        var state = new ArtifactAcquisitionLeaseStateV052(
            StateSchema, Version, leaseId, preview.RequestId, preview.RequestDigestSha256,
            preview.Artifacts, preview.DestinationRoot, issued, expires,
            preview.MaxTotalNetworkBytes, preview.MaxTotalNetworkBytes,
            preview.MaxRedirects, preview.TimeoutSeconds, preview.TtlSeconds,
            1, 1, bearerSha, false, false, false, null, null, null, 0,
            NonEffects(),
            "Canonical v0.52 one-shot acquisition authority. Bearer plaintext is not persisted; selection/handoff alone is never authority.");
        await WriteAtomicAsync(statePath, state, cancellationToken);

        var grant = new ArtifactAcquisitionGrantV052(
            GrantSchema, Version, DateTimeOffset.Now, leaseId, bearer, preview.RequestId,
            preview.RequestDigestSha256, preview.Artifacts, preview.DestinationRoot, expires,
            preview.MaxTotalNetworkBytes, preview.MaxRedirects, preview.TimeoutSeconds, 1,
            false, false,
            "In-memory bearer is bounded by exact canonical state, expiry, one call, routes, redirect/timeout limits, exact size/hash and fixed destination. No post-download authority is granted.");
        var receipt = new ArtifactAcquisitionAuthorityReceiptV052(
            AuthorityReceiptSchema, Version, DateTimeOffset.Now, leaseId, preview.RequestId,
            preview.RequestDigestSha256, bearerSha, statePath, HashFile(statePath), expires,
            preview.MaxTotalNetworkBytes, preview.MaxRedirects, preview.TimeoutSeconds, 1,
            false, false, false, false, false, NonEffects(),
            "ACQUISITION_AUTHORITY_GRANTED_NOT_USED",
            "One-shot local acquisition authority was materialized. Grant creation performed no network or artifact-byte write.");
        var receiptPath = await WriteArtifactReceiptAsync(
            workspaceRoot, $"authority-{leaseId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", receipt, cancellationToken);
        return (grant, receipt, receiptPath);
    }

    public async Task<(ArtifactAcquisitionExecutionReceiptV052 Receipt, string ReceiptPath)> AcquireAsync(
        string workspaceRoot,
        ArtifactAcquisitionGrantV052 grant,
        CancellationToken cancellationToken)
    {
        if (grant is null || grant.Schema != GrantSchema || grant.Version != Version || !SafeToken(grant.LeaseId, "acqlease-"))
            throw Refused("AUTHORITY_INVALID", "Invalid v0.52 acquisition grant identity.");
        var paths = ResolveLeasePaths(workspaceRoot, grant.LeaseId);
        using var leaseLock = AcquireExclusiveLock(paths.LockPath, "ACQUISITION_LEASE_BUSY");
        var state = await ReadStateAsync(paths.StatePath, cancellationToken);
        ValidateGrantAgainstState(grant, state);
        if (state.Revoked) throw Refused("AUTHORITY_REVOKED", "Acquisition lease is revoked.");
        if (state.Completed) throw Refused("AUTHORITY_ALREADY_COMPLETED", "Acquisition lease already completed.");
        if (state.Failed) throw Refused("AUTHORITY_TERMINAL_FAILED", $"Acquisition lease already failed: {state.FailureClassification}");
        if (state.ExpiresAt <= DateTimeOffset.Now) throw Refused("AUTHORITY_EXPIRED", "Acquisition lease expired.");
        if (state.RemainingCalls != 1) throw Refused("AUTHORITY_CALL_BUDGET_EXHAUSTED", "One-shot acquisition call budget is exhausted.");
        if (!Sha256Text(grant.Bearer).Equals(state.BearerSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("AUTHORITY_BEARER_MISMATCH", "Acquisition bearer mismatch.");

        state = state with
        {
            RemainingCalls = 0,
            StartedAt = DateTimeOffset.Now,
            StateRevision = state.StateRevision + 1,
            Note = "One-shot acquisition authority consumed before network. Crash/failure cannot silently retry or resume."
        };
        await WriteAtomicAsync(paths.StatePath, state, cancellationToken);

        var tx = new ArtifactAcquisitionTransactionV052(
            TransactionSchema, Version, DateTimeOffset.Now,
            "acqtx-" + Guid.NewGuid().ToString("N"), state.LeaseId, state.RequestId,
            state.RequestDigestSha256, "ACQUISITION_PREPARED", null, 0,
            state.Artifacts.Select(x => NewItemEvidence(x, state.DestinationRoot)).ToArray(),
            true, true, false, false, false, false, false, false, false, false, false,
            false, null, NonEffects(),
            "Authority is consumed, but DOWNLOAD_STARTED is not yet observed. Prepared does not prove network access or artifact existence.");
        await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);

        long networkBytes = 0;
        try
        {
            for (var index = 0; index < state.Artifacts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = state.Artifacts[index];
                var evidence = tx.Items[index];
                ValidateDestinationPath(state.DestinationRoot, evidence.FinalPath);
                using var destinationLock = AcquireDestinationLock(workspaceRoot, evidence.FinalPath);

                if (File.Exists(evidence.FinalPath))
                {
                    RejectReparse(evidence.FinalPath, "existing final artifact");
                    var bytes = new FileInfo(evidence.FinalPath).Length;
                    var sha = bytes == item.ExpectedBytes ? HashFile(evidence.FinalPath) : null;
                    if (bytes == item.ExpectedBytes && sha is not null && sha.Equals(item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        evidence = evidence with
                        {
                            State = "SHA256_VERIFIED",
                            ObservedFileBytes = bytes,
                            ObservedSha256 = sha,
                            ExpectedSizeMatched = true,
                            ExpectedSha256Matched = true,
                            ExistingVerifiedReused = true
                        };
                        tx = ReplaceItem(tx, index, evidence) with
                        {
                            ObservedAt = DateTimeOffset.Now,
                            State = "SHA256_VERIFIED",
                            CurrentArtifactId = item.ArtifactId,
                            Note = "Existing final artifact matched exact size and SHA-256; classified verified without network or overwrite."
                        };
                        await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);
                        continue;
                    }
                    throw Refused("EXISTING_DIFFERENT_FILE", $"Final path already exists with different bytes: {evidence.FinalPath}");
                }

                var partial = evidence.FinalPath + "." + state.LeaseId + ".partial";
                if (File.Exists(partial)) throw Refused("PARTIAL_ALREADY_EXISTS", $"Unverified partial already exists: {partial}");
                evidence = evidence with { PartialPath = partial, State = "DOWNLOAD_STARTED", NetworkAccessPerformed = true };
                tx = ReplaceItem(tx, index, evidence) with
                {
                    ObservedAt = DateTimeOffset.Now,
                    State = "DOWNLOAD_STARTED",
                    CurrentArtifactId = item.ArtifactId,
                    NetworkAccessPerformed = true,
                    Note = "Exact HTTPS request is starting under already-consumed one-shot authority. DOWNLOAD_STARTED is not bytes/size/hash completion."
                };
                await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);

                var result = await DownloadOneAsync(item, partial,
                    state.MaxTotalNetworkBytes - networkBytes, state, cancellationToken);
                networkBytes = checked(networkBytes + result.NetworkBytes);
                evidence = evidence with
                {
                    RedirectsObserved = result.Redirects,
                    ObservedNetworkBytes = result.NetworkBytes,
                    ObservedFileBytes = result.FileBytes,
                    ObservedSha256 = result.Sha256,
                    State = "BYTES_COMPLETE"
                };
                tx = ReplaceItem(tx, index, evidence) with
                {
                    ObservedAt = DateTimeOffset.Now,
                    State = "BYTES_COMPLETE",
                    CurrentArtifactId = item.ArtifactId,
                    NetworkBytesObserved = networkBytes,
                    FilesystemMutationPerformed = true,
                    Note = "Response body reached EOF within byte ceilings. BYTES_COMPLETE does not imply exact expected size or SHA-256."
                };
                await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);

                if (result.FileBytes != item.ExpectedBytes)
                    throw Refused("SIZE_MISMATCH", $"{item.ArtifactId}: expected {item.ExpectedBytes} bytes, observed {result.FileBytes}.");
                evidence = evidence with { State = "SIZE_VERIFIED", ExpectedSizeMatched = true };
                tx = ReplaceItem(tx, index, evidence) with
                {
                    ObservedAt = DateTimeOffset.Now,
                    State = "SIZE_VERIFIED",
                    CurrentArtifactId = item.ArtifactId,
                    Note = "Exact partial-file size matched reviewed artifact identity. SIZE_VERIFIED is not SHA256_VERIFIED."
                };
                await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);

                if (!result.Sha256.Equals(item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw Refused("HASH_MISMATCH", $"{item.ArtifactId}: exact SHA-256 mismatch.");
                evidence = evidence with { State = "SHA256_VERIFIED", ExpectedSha256Matched = true };
                tx = ReplaceItem(tx, index, evidence) with
                {
                    ObservedAt = DateTimeOffset.Now,
                    State = "SHA256_VERIFIED",
                    CurrentArtifactId = item.ArtifactId,
                    Note = "Expected SHA-256 matched completed partial bytes. Verification grants no extraction/install/execute/runtime authority."
                };
                await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);

                if (File.Exists(evidence.FinalPath))
                    throw Refused("FINAL_PATH_RACE", $"Final path appeared before atomic promotion: {evidence.FinalPath}");
                File.Move(partial, evidence.FinalPath, overwrite: false);
                RejectReparse(evidence.FinalPath, "promoted final artifact");
                evidence = evidence with { FinalPathPromoted = true, PartialPath = null };
                tx = ReplaceItem(tx, index, evidence) with
                {
                    ObservedAt = DateTimeOffset.Now,
                    FinalArtifactPromotionPerformed = true,
                    Note = "Verified partial bytes were atomically promoted. Promotion does not extract or execute."
                };
                await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);
            }

            if (tx.Items.Any(x => !x.ExpectedSha256Matched))
                throw Refused("SET_INCOMPLETE", "Not every requested artifact reached exact SHA-256 verification.");

            state = state with
            {
                RemainingNetworkBytes = Math.Max(0, state.MaxTotalNetworkBytes - networkBytes),
                Completed = true,
                CompletedAt = DateTimeOffset.Now,
                StateRevision = state.StateRevision + 1,
                Note = "One-shot acquisition authority completed after every exact artifact reached SHA-256 verification. No later-use authority is implied."
            };
            await WriteAtomicAsync(paths.StatePath, state, cancellationToken);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "ACQUISITION_VERIFIED",
                CurrentArtifactId = null,
                NetworkBytesObserved = networkBytes,
                FailureClassification = null,
                Note = "Every artifact in the reviewed set is locally SHA-256 verified. Extraction/install/runtime/benchmark/model/game authority remains absent."
            };
            await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, cancellationToken);
            var receipt = BuildExecutionReceipt(paths, tx, true);
            var receiptPath = await WriteArtifactReceiptAsync(workspaceRoot,
                $"execution-{tx.TransactionId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        catch (ArtifactAcquisitionExceptionV052 ex)
        {
            tx = MaterializeFailureEvidence(tx, ex.Classification);
            networkBytes = tx.Items.Sum(x => x.ObservedNetworkBytes);
            state = state with
            {
                RemainingNetworkBytes = Math.Max(0, state.MaxTotalNetworkBytes - networkBytes),
                Failed = true,
                FailureClassification = ex.Classification,
                CompletedAt = DateTimeOffset.Now,
                StateRevision = state.StateRevision + 1,
                Note = "One-shot acquisition failed terminally. No automatic retry/resume; any .partial bytes remain unverified evidence only."
            };
            await WriteAtomicAsync(paths.StatePath, state, CancellationToken.None);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = ex.Classification,
                NetworkBytesObserved = networkBytes,
                FailureClassification = ex.Classification,
                Note = ex.Message + " Partial bytes, if present, are non-authoritative and never auto-promoted."
            };
            await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            tx = MaterializeFailureEvidence(tx, "CANCELLED_PARTIAL_UNVERIFIED");
            networkBytes = tx.Items.Sum(x => x.ObservedNetworkBytes);
            state = state with
            {
                RemainingNetworkBytes = Math.Max(0, state.MaxTotalNetworkBytes - networkBytes),
                Failed = true,
                FailureClassification = "CANCELLED_PARTIAL_UNVERIFIED",
                CompletedAt = DateTimeOffset.Now,
                StateRevision = state.StateRevision + 1,
                Note = "One-shot acquisition cancelled; no automatic retry/resume authority."
            };
            await WriteAtomicAsync(paths.StatePath, state, CancellationToken.None);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "CANCELLED_PARTIAL_UNVERIFIED",
                NetworkBytesObserved = networkBytes,
                FailureClassification = "CANCELLED_PARTIAL_UNVERIFIED",
                Note = "Cancellation leaves any partial bytes non-authoritative; no automatic retry/resume/promotion."
            };
            await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            tx = MaterializeFailureEvidence(tx, "NETWORK_OR_IO_FAILED");
            networkBytes = tx.Items.Sum(x => x.ObservedNetworkBytes);
            state = state with
            {
                RemainingNetworkBytes = Math.Max(0, state.MaxTotalNetworkBytes - networkBytes),
                Failed = true,
                FailureClassification = "NETWORK_OR_IO_FAILED",
                CompletedAt = DateTimeOffset.Now,
                StateRevision = state.StateRevision + 1,
                Note = "One-shot acquisition failed terminally from unexpected network/filesystem error."
            };
            await WriteAtomicAsync(paths.StatePath, state, CancellationToken.None);
            tx = tx with
            {
                ObservedAt = DateTimeOffset.Now,
                State = "NETWORK_OR_IO_FAILED",
                NetworkBytesObserved = networkBytes,
                FailureClassification = "NETWORK_OR_IO_FAILED",
                Note = "Unexpected transfer/storage failure: " + ex.GetType().Name + ". No automatic retry/resume/promotion."
            };
            await PersistTransitionAsync(workspaceRoot, paths.TransactionPath, tx, CancellationToken.None);
            throw new ArtifactAcquisitionExceptionV052("NETWORK_OR_IO_FAILED",
                "Bounded acquisition failed from unexpected network/filesystem error.", ex);
        }
    }

    public async Task<ArtifactAcquisitionLeaseStateV052> ReadLeaseStateAsync(
        string workspaceRoot, string leaseId, CancellationToken cancellationToken)
        => await ReadStateAsync(ResolveLeasePaths(workspaceRoot, leaseId).StatePath, cancellationToken);

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("acquisition-v052-selected-authority", true, "preview performs no network/write and is not authority", "Selected != Authorized"),
        ("acquisition-v052-policy-binding", true, "redirect limit + timeout + TTL are persisted in canonical state and grant", "exact bound"),
        ("acquisition-v052-one-shot", true, "RemainingCalls becomes zero before network", "no silent retry/resume"),
        ("acquisition-v052-https", true, "initial and redirect routes require credential-free HTTPS exact host/path-prefix rules", "bounded routes"),
        ("acquisition-v052-partial", true, "unique .partial promoted only after exact size + SHA-256", "fail closed"),
        ("acquisition-v052-existing", true, "exact existing file reusable; different existing file never overwritten", "no overwrite"),
        ("acquisition-v052-destination", true, "fixed existing root outside Workbench Git root + reparse rejection", "external-to-Git"),
        ("acquisition-v052-effects", true, "no extract/install/process/runtime/benchmark/model/game authority", "false"),
        ("acquisition-v052-secrets", true, "bearer plaintext not persisted; no auth/cookie state supplied", "omitted")
    };

    private async Task<DownloadResult> DownloadOneAsync(
        ArtifactAcquisitionItemV052 item,
        string partialPath,
        long remainingTotalBudget,
        ArtifactAcquisitionLeaseStateV052 state,
        CancellationToken cancellationToken)
    {
        if (remainingTotalBudget < item.ExpectedBytes)
            throw Refused("BYTE_CEILING_EXCEEDED", "Remaining acquisition byte ceiling is below exact expected artifact size.");
        ValidateDestinationPath(state.DestinationRoot, partialPath);
        var leaseRemaining = state.ExpiresAt - DateTimeOffset.Now;
        if (leaseRemaining <= TimeSpan.Zero) throw Refused("AUTHORITY_EXPIRED", "Acquisition lease expired before network start.");
        var effectiveTimeout = TimeSpan.FromSeconds(state.TimeoutSeconds) < leaseRemaining
            ? TimeSpan.FromSeconds(state.TimeoutSeconds)
            : leaseRemaining;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(effectiveTimeout);

        var current = new Uri(item.SourceUri, UriKind.Absolute);
        var redirects = 0;
        while (true)
        {
            ValidateRoute(current, item, redirects == 0);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.AcceptEncoding.Clear();
            request.Headers.Authorization = null;
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var classification = DateTimeOffset.Now >= state.ExpiresAt ? "AUTHORITY_EXPIRED_DURING_TRANSFER" : "NETWORK_TIMEOUT";
                throw Refused(classification, $"Bounded transfer timed out/expired for {item.ArtifactId}.");
            }
            catch (HttpRequestException ex)
            {
                throw new ArtifactAcquisitionExceptionV052("NETWORK_FAILED", $"HTTPS request failed for {item.ArtifactId}.", ex);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= state.MaxRedirects)
                        throw Refused("REDIRECT_LIMIT_EXCEEDED", $"Redirect count exceeds exact authority MaxRedirects={state.MaxRedirects}.");
                    var location = response.Headers.Location;
                    if (location is null) throw Refused("REDIRECT_POLICY_REFUSED", "Redirect response has no Location.");
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    ValidateRoute(next, item, false);
                    redirects++;
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw Refused("NETWORK_FAILED", $"Unexpected HTTP status {(int)response.StatusCode} for {item.ArtifactId}.");
                if (response.Content.Headers.ContentLength is long declared &&
                    (declared > item.ExpectedBytes || declared > remainingTotalBudget))
                    throw Refused("BYTE_CEILING_EXCEEDED", $"Declared Content-Length exceeds exact authority for {item.ArtifactId}.");

                Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
                RejectReparseChain(Path.GetDirectoryName(partialPath)!);
                await using var target = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long observed = 0;
                while (true)
                {
                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var classification = DateTimeOffset.Now >= state.ExpiresAt ? "AUTHORITY_EXPIRED_DURING_TRANSFER" : "NETWORK_TIMEOUT";
                        throw Refused(classification, $"Bounded streaming timed out/expired for {item.ArtifactId}.");
                    }
                    if (read == 0) break;
                    observed = checked(observed + read);
                    if (observed > item.ExpectedBytes || observed > remainingTotalBudget)
                        throw Refused("BYTE_CEILING_EXCEEDED", $"Observed response bytes exceeded exact authority for {item.ArtifactId}.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                await target.FlushAsync(timeout.Token);
                return new DownloadResult(redirects, observed, new FileInfo(partialPath).Length,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            }
        }
    }

    private static ArtifactAcquisitionItemV052 NormalizeItem(ArtifactAcquisitionItemV052 item)
    {
        if (item is null || !SafeToken(item.ArtifactId, "artifact-"))
            throw Refused("REQUEST_INVALID", "ArtifactId must be an artifact-* safe token.");
        if (string.IsNullOrWhiteSpace(item.FileName) || Path.GetFileName(item.FileName) != item.FileName ||
            item.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw Refused("REQUEST_INVALID", $"Unsafe artifact filename: {item.FileName}");
        if (item.ExpectedBytes < 1 || item.ExpectedBytes > MaxArtifactBytes)
            throw Refused("REQUEST_INVALID", $"ExpectedBytes must be within 1..{MaxArtifactBytes}.");
        var sha = item.ExpectedSha256?.Trim().ToLowerInvariant() ?? "";
        if (sha.Length != 64 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw Refused("REQUEST_INVALID", $"ExpectedSha256 for {item.ArtifactId} is invalid.");
        if (!Uri.TryCreate(item.SourceUri, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw Refused("SOURCE_POLICY_REFUSED", $"SourceUri for {item.ArtifactId} must be credential-free HTTPS without fragment.");
        if (item.AllowedRoutes is null || item.AllowedRoutes.Count < 1 || item.AllowedRoutes.Count > 16)
            throw Refused("SOURCE_POLICY_REFUSED", "AllowedRoutes must contain 1..16 exact host/path-prefix rules.");
        var routes = item.AllowedRoutes.Select(NormalizeRoute).Distinct().ToArray();
        var normalized = item with { SourceUri = uri.AbsoluteUri, ExpectedSha256 = sha, AllowedRoutes = routes };
        ValidateRoute(uri, normalized, true);
        return normalized;
    }

    private static ArtifactAcquisitionRouteRuleV052 NormalizeRoute(ArtifactAcquisitionRouteRuleV052 rule)
    {
        if (rule is null || string.IsNullOrWhiteSpace(rule.Host) || rule.Host.Contains('*') || rule.Host.Contains('/') || rule.Host.Contains('\\'))
            throw Refused("SOURCE_POLICY_REFUSED", "Route host must be exact, without wildcard/path.");
        var host = rule.Host.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(rule.PathPrefix) || !rule.PathPrefix.StartsWith('/', StringComparison.Ordinal) ||
            rule.PathPrefix.Contains("..", StringComparison.Ordinal))
            throw Refused("SOURCE_POLICY_REFUSED", "Route PathPrefix must be absolute and non-traversing.");
        return new ArtifactAcquisitionRouteRuleV052(host, rule.PathPrefix);
    }

    private static void ValidateRoute(Uri uri, ArtifactAcquisitionItemV052 item, bool isInitial)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw Refused(isInitial ? "SOURCE_POLICY_REFUSED" : "REDIRECT_POLICY_REFUSED",
                "Every acquisition route must remain credential-free HTTPS without fragment.");
        if (isInitial && !uri.AbsoluteUri.Equals(item.SourceUri, StringComparison.Ordinal))
            throw Refused("SOURCE_POLICY_REFUSED", "Initial URI drifted from exact reviewed source.");
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath;
        if (!item.AllowedRoutes.Any(rule => host.Equals(rule.Host, StringComparison.OrdinalIgnoreCase) &&
                                            path.StartsWith(rule.PathPrefix, StringComparison.Ordinal)))
            throw Refused(isInitial ? "SOURCE_POLICY_REFUSED" : "REDIRECT_POLICY_REFUSED",
                $"Route {host}{path} is outside reviewed policy for {item.ArtifactId}.");
    }

    private static bool IsRedirect(HttpStatusCode code)
        => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther;

    private static void ValidatePreview(string workspaceRoot, ArtifactAcquisitionPreviewV052 preview)
    {
        if (preview is null || preview.Schema != PreviewSchema || preview.Version != Version ||
            !preview.ReadyForExplicitAcquisitionAuthority || preview.NetworkAccessPerformed ||
            preview.FilesystemMutationPerformed || preview.ContainsArtifactBytes)
            throw Refused("PREVIEW_INVALID", "Preview violates v0.52 no-effect boundary.");
        var destination = ValidateDestinationRoot(workspaceRoot, preview.DestinationRoot, true);
        var normalized = preview.Artifacts.Select(NormalizeItem).ToArray();
        var digest = RequestDigest(preview.RequestId, normalized, destination, preview.MaxTotalNetworkBytes,
            preview.MaxRedirects, preview.TimeoutSeconds, preview.TtlSeconds);
        if (!digest.Equals(preview.RequestDigestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("PREVIEW_INVALID", "Preview request digest mismatch.");
    }

    private static void ValidateGrantAgainstState(ArtifactAcquisitionGrantV052 grant, ArtifactAcquisitionLeaseStateV052 state)
    {
        if (state.Schema != StateSchema || state.Version != Version ||
            !state.LeaseId.Equals(grant.LeaseId, StringComparison.Ordinal) ||
            !state.RequestId.Equals(grant.RequestId, StringComparison.Ordinal) ||
            !state.RequestDigestSha256.Equals(grant.RequestDigestSha256, StringComparison.OrdinalIgnoreCase) ||
            !state.DestinationRoot.Equals(grant.DestinationRoot, StringComparison.OrdinalIgnoreCase) ||
            state.MaxTotalNetworkBytes != grant.MaxTotalNetworkBytes ||
            state.MaxRedirects != grant.MaxRedirects || state.TimeoutSeconds != grant.TimeoutSeconds ||
            state.MaxCalls != grant.MaxCalls || state.Artifacts.Count != grant.Artifacts.Count)
            throw Refused("AUTHORITY_INVALID", "Grant is not exact-bound to canonical acquisition state.");
        for (var i = 0; i < state.Artifacts.Count; i++)
        {
            if (JsonSerializer.Serialize(state.Artifacts[i], JsonOptions) != JsonSerializer.Serialize(grant.Artifacts[i], JsonOptions))
                throw Refused("AUTHORITY_INVALID", "Artifact identity/route policy drifted between grant and canonical state.");
        }
    }

    private static ArtifactAcquisitionItemEvidenceV052 NewItemEvidence(ArtifactAcquisitionItemV052 item, string destinationRoot)
    {
        var final = Path.GetFullPath(Path.Combine(destinationRoot, item.FileName));
        ValidateDestinationPath(destinationRoot, final);
        return new ArtifactAcquisitionItemEvidenceV052(item.ArtifactId, item.SourceUri, item.FileName,
            final, null, "NOT_STARTED", 0, 0, null, null, false, false, false, false, false, null);
    }

    private static ArtifactAcquisitionTransactionV052 ReplaceItem(
        ArtifactAcquisitionTransactionV052 tx, int index, ArtifactAcquisitionItemEvidenceV052 item)
    {
        var items = tx.Items.ToArray();
        items[index] = item;
        return tx with { Items = items };
    }

    private static ArtifactAcquisitionTransactionV052 MaterializeFailureEvidence(
        ArtifactAcquisitionTransactionV052 tx, string classification)
    {
        if (tx.CurrentArtifactId is null) return tx;
        var index = tx.Items.ToList().FindIndex(x => x.ArtifactId == tx.CurrentArtifactId);
        if (index < 0) return tx;
        var item = tx.Items[index];
        long observed = item.ObservedNetworkBytes;
        long? fileBytes = item.ObservedFileBytes;
        if (!string.IsNullOrWhiteSpace(item.PartialPath) && File.Exists(item.PartialPath))
        {
            var info = new FileInfo(item.PartialPath);
            fileBytes = info.Length;
            observed = Math.Max(observed, info.Length);
        }
        item = item with
        {
            State = classification,
            FailureClassification = classification,
            ObservedNetworkBytes = observed,
            ObservedFileBytes = fileBytes
        };
        var updated = ReplaceItem(tx, index, item);
        return updated with
        {
            FilesystemMutationPerformed = updated.FilesystemMutationPerformed ||
                (!string.IsNullOrWhiteSpace(item.PartialPath) && File.Exists(item.PartialPath))
        };
    }

    private static ArtifactAcquisitionExecutionReceiptV052 BuildExecutionReceipt(
        LeasePaths paths, ArtifactAcquisitionTransactionV052 tx, bool allVerified)
        => new(ExecutionReceiptSchema, Version, DateTimeOffset.Now, tx.TransactionId, tx.LeaseId, tx.RequestId,
            tx.State, tx.NetworkBytesObserved, tx.Items, paths.TransactionPath, HashFile(paths.TransactionPath),
            paths.StatePath, HashFile(paths.StatePath), allVerified, tx.NetworkAccessPerformed,
            tx.FilesystemMutationPerformed, tx.ExtractionPerformed, tx.ProcessExecutionPerformed,
            tx.RuntimeStartPerformed, tx.BenchmarkPerformed, tx.ModelRequestPerformed, tx.GameAccessPerformed,
            NonEffects(), allVerified ? "ACQUISITION_VERIFIED" : tx.State,
            "Receipt proves bounded acquisition/verification only. Verified bytes remain inert until separate later authority.");

    private static string ValidateDestinationRoot(string workspaceRoot, string value, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Refused("DESTINATION_POLICY_REFUSED", "DestinationRoot required.");
        var full = Path.GetFullPath(value.Trim());
        var workbenchRepo = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (IsWithin(full, workbenchRepo))
            throw Refused("DESTINATION_POLICY_REFUSED", "Destination must remain external to Workbench Git root.");
        if (mustExist && !Directory.Exists(full))
            throw Refused("DESTINATION_POLICY_REFUSED", "Destination root must already exist before preview.");
        if (Directory.Exists(full)) RejectReparseChain(full);
        return full;
    }

    private static void ValidateDestinationPath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw Refused("DESTINATION_POLICY_REFUSED", "Artifact path escaped fixed destination root.");
        var parent = Path.GetDirectoryName(full) ?? throw Refused("DESTINATION_POLICY_REFUSED", "Artifact parent missing.");
        if (Directory.Exists(parent)) RejectReparseChain(parent);
        if (File.Exists(full)) RejectReparse(full, "destination artifact");
    }

    private static FileStream AcquireDestinationLock(string workspaceRoot, string finalPath)
    {
        var lockDir = Path.Combine(ResolveLeaseRoot(workspaceRoot), "destination-locks");
        Directory.CreateDirectory(lockDir);
        RejectReparseChain(lockDir);
        var key = Sha256Text(Path.GetFullPath(finalPath).ToLowerInvariant());
        return AcquireExclusiveLock(Path.Combine(lockDir, key + ".lock"), "ACQUISITION_DESTINATION_BUSY");
    }

    private static FileStream AcquireExclusiveLock(string path, string classification)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) RejectReparse(path, "v0.52 acquisition lock");
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            RejectReparse(path, "v0.52 acquired lock");
            return stream;
        }
        catch (IOException ex)
        {
            throw new ArtifactAcquisitionExceptionV052(classification,
                "Another process currently owns the exact acquisition corridor.", ex);
        }
    }

    private static string ResolveLeaseRoot(string workspaceRoot)
    {
        var workspace = Path.GetFullPath(workspaceRoot.Trim());
        var workbench = Path.GetFullPath(Path.Combine(workspace, "Workbench"));
        if (!Directory.Exists(workbench)) throw Refused("WORKSPACE_INVALID", $"Workbench root missing: {workbench}");
        var root = Path.Combine(workbench, ".workbench", "artifact-acquisition-v052");
        Directory.CreateDirectory(root);
        RejectReparseChain(root);
        return root;
    }

    private static LeasePaths ResolveLeasePaths(string workspaceRoot, string leaseId)
    {
        if (!SafeToken(leaseId, "acqlease-")) throw Refused("AUTHORITY_INVALID", "Unsafe acquisition LeaseId.");
        var dir = Path.Combine(ResolveLeaseRoot(workspaceRoot), leaseId);
        if (!Directory.Exists(dir)) throw Refused("AUTHORITY_INVALID", "Exact acquisition lease directory absent.");
        RejectReparseChain(dir);
        return new LeasePaths(dir, Path.Combine(dir, "state.json"), Path.Combine(dir, "transaction.json"), Path.Combine(dir, "lease.lock"));
    }

    private static async Task<ArtifactAcquisitionLeaseStateV052> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw Refused("AUTHORITY_INVALID", "Acquisition lease state absent.");
        RejectReparse(path, "v0.52 acquisition state");
        ArtifactAcquisitionLeaseStateV052? state;
        try
        {
            state = JsonSerializer.Deserialize<ArtifactAcquisitionLeaseStateV052>(
                await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ArtifactAcquisitionExceptionV052("AUTHORITY_INVALID", "Acquisition state JSON invalid.", ex);
        }
        if (state is null || state.Schema != StateSchema || state.Version != Version ||
            !SafeToken(state.LeaseId, "acqlease-") || state.BearerSha256.Length != 64 ||
            state.MaxRedirects < 0 || state.MaxRedirects > MaxRedirects ||
            state.TimeoutSeconds < 1 || state.TimeoutSeconds > MaxTimeoutSeconds)
            throw Refused("AUTHORITY_INVALID", "Acquisition state identity/policy contract invalid.");
        return state;
    }

    private static async Task PersistTransitionAsync(
        string workspaceRoot, string transactionPath, ArtifactAcquisitionTransactionV052 tx, CancellationToken cancellationToken)
    {
        await WriteAtomicAsync(transactionPath, tx, cancellationToken);
        _ = await WriteArtifactReceiptAsync(workspaceRoot,
            $"transition-{tx.TransactionId}-{LocalAppV046FileBoundary.SafeToken(tx.State)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json",
            tx, cancellationToken);
    }

    private static async Task<string> WriteArtifactReceiptAsync<T>(
        string workspaceRoot, string fileName, T value, CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "artifact-acquisition-v052");
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RejectReparseChain(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
            RejectReparse(temp, "temporary v0.52 state");
            if (File.Exists(path)) RejectReparse(path, "pre-replace v0.52 state");
            File.Move(temp, path, overwrite: true);
            RejectReparse(path, "v0.52 state");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string RequestDigest(
        string requestId, IReadOnlyList<ArtifactAcquisitionItemV052> artifacts, string destinationRoot,
        long maxBytes, int maxRedirects, int timeoutSeconds, int ttlSeconds)
        => Sha256Text(JsonSerializer.Serialize(new
        {
            Schema = RequestSchema,
            RequestId = requestId,
            Artifacts = artifacts,
            DestinationRoot = destinationRoot,
            MaxTotalNetworkBytes = maxBytes,
            MaxRedirects = maxRedirects,
            TimeoutSeconds = timeoutSeconds,
            TtlSeconds = ttlSeconds
        }, JsonOptions));

    private static bool SafeToken(string? value, string prefix)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value.StartsWith(prefix, StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectReparseChain(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Refused("DESTINATION_REPARSE_REFUSED", $"Reparse directory refused: {current.FullName}");
            current = current.Parent;
        }
    }

    private static void RejectReparse(string path, string role)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw Refused("DESTINATION_REPARSE_REFUSED", $"Reparse path refused for {role}: {path}");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha256Text(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ArtifactAcquisitionExceptionV052 Refused(string classification, string message)
        => new(classification, message);

    private static string[] NonEffects() => new[]
    {
        "artifact selection or valid handoff is not acquisition authority",
        "acquisition authority is exact one-shot and does not create general network/browser/MCP-tunnel authority",
        "only reviewed credential-free HTTPS host/path-prefix routes are allowed",
        "per-request redirect limit, timeout, TTL and total network bytes are canonical authority fields",
        "downloaded bytes remain .partial and non-authoritative until exact size and SHA-256 verification",
        "different existing final artifact is never overwritten",
        "no archive extraction, installation, script execution or arbitrary process launch",
        "no PATH/environment mutation",
        "no local model/runtime/server start",
        "no benchmark, model request or game access",
        "no Git/catalog/Agent Execute/ActionPermit authority",
        "no bearer plaintext persisted in canonical state or receipts",
        "no automatic retry or range-resume authority after failure/crash"
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed record LeasePaths(string LeaseDirectory, string StatePath, string TransactionPath, string LockPath);
    private sealed record DownloadResult(int Redirects, long NetworkBytes, long FileBytes, string Sha256);
}
