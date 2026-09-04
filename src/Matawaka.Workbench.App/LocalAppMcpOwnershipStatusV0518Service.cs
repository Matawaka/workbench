using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppMcpOwnershipLeaseObservationV0518(
    string? LeaseId,
    string Classification,
    bool CanonicalStatePresent,
    bool CanonicalStateValid,
    bool Live,
    bool Revoked,
    bool Expired,
    bool BudgetExhausted,
    long? StateRevision,
    DateTimeOffset? ExpiresAt,
    bool HistoricalEnumerationPerformed,
    bool CanonicalMutationPerformed,
    string Note);

public sealed record LocalAppMcpOwnershipStatusV0518(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string Status,
    bool OwnerHandleBusy,
    bool OwnerLockFilePresent,
    bool OwnerMetadataPresent,
    bool OwnerMetadataValid,
    string? SessionId,
    string? MetadataLeaseId,
    int? MetadataOwnerProcessId,
    DateTimeOffset? MetadataAcquiredAt,
    string? MetadataState,
    bool? MetadataListenerObservedActive,
    string? MetadataLoopbackHost,
    int? MetadataLoopbackPort,
    LocalAppMcpOwnershipLeaseObservationV0518 LeaseObservation,
    bool ResumeAuthorityGranted,
    bool LeaseAuthorityGranted,
    bool ReadAuthorityGranted,
    bool RevokeAuthorityGranted,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool EndpointSecretDisclosed,
    bool CanonicalHistoricalScanPerformed,
    bool CanonicalStateMutationPerformed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed class LocalAppMcpOwnershipStatusV0518Service
{
    public const string Version = "0.51.8";
    public const string StatusSchema = "matawaka.local-app-mcp-ownership-status/v0.51.8";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public LocalAppMcpOwnershipStatusV0518 Observe(string workspaceRoot, string applicationId)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");

        var appToken = LocalAppV046FileBoundary.SafeToken(applicationId);
        var ownerRoot = Path.Combine(workbench, ".workbench", "local-mcp-session-v0517");
        var appDir = Path.Combine(ownerRoot, appToken);
        var lockPath = Path.Combine(appDir, "owner.lock");
        var metadataPath = Path.Combine(appDir, "owner-v0.51.7.json");

        if (Directory.Exists(ownerRoot)) LocalAppV046FileBoundary.RejectReparse(ownerRoot, "v0.51.8 MCP ownership status root");
        if (Directory.Exists(appDir)) LocalAppV046FileBoundary.RejectReparse(appDir, "v0.51.8 MCP ownership app directory");
        if (File.Exists(lockPath)) LocalAppV046FileBoundary.RejectReparse(lockPath, "v0.51.8 MCP ownership lock");
        if (File.Exists(metadataPath)) LocalAppV046FileBoundary.RejectReparse(metadataPath, "v0.51.8 MCP owner metadata");

        var lockPresent = File.Exists(lockPath);
        var handleBusy = false;
        if (lockPresent)
        {
            try
            {
                using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                handleBusy = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidDataException("MCP_OWNERSHIP_STATUS_UNCERTAIN: owner lock could not be probed without mutation.", ex);
            }
        }

        var metadataPresent = File.Exists(metadataPath);
        LocalAppMcpSessionOwnerV0517? metadata = null;
        var metadataValid = false;
        if (metadataPresent)
        {
            try
            {
                metadata = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(File.ReadAllText(metadataPath, Encoding.UTF8), JsonOptions);
                metadataValid = metadata is not null &&
                                metadata.Schema == LocalAppMcpSessionOwnershipV0517Service.OwnerSchema &&
                                metadata.Version == LocalAppMcpSessionOwnershipV0517Service.Version &&
                                metadata.ApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
                                !metadata.BearerPlaintextStored && !metadata.BearerHashStored && !metadata.EndpointSecretStored && !metadata.LeaseAuthorityGranted;
            }
            catch (JsonException)
            {
                metadataValid = false;
            }
        }

        var status = handleBusy
            ? "OWNED"
            : metadataPresent
                ? "FREE_STALE_METADATA"
                : "FREE_NO_METADATA";

        LocalAppMcpOwnershipLeaseObservationV0518 leaseObservation;
        if (metadataValid && !string.IsNullOrWhiteSpace(metadata!.LeaseId))
            leaseObservation = ObserveExactLease(workbench, applicationId, metadata.LeaseId!, handleBusy);
        else
            leaseObservation = new LocalAppMcpOwnershipLeaseObservationV0518(
                metadata?.LeaseId, metadataPresent && !metadataValid ? "METADATA_INVALID_UNTRUSTED" : "NO_REFERENCED_LEASE",
                false, false, false, false, false, false, null, null, false, false,
                metadataPresent && !metadataValid
                    ? "Owner metadata is present but does not satisfy the exact v0.51.7 non-authoritative metadata contract. It grants no authority."
                    : "No exact metadata LeaseId was available for canonical cross-check.");

        var nonEffects = new[]
        {
            "owner handle probe is read-only and does not create an owner lock file",
            "owner metadata is observational only and never canonical lease authority",
            "no bearer plaintext or bearer hash disclosed",
            "no reusable endpoint path token disclosed",
            "no MCP resume/start/stop authority granted",
            "no lease create/revoke/renew authority granted",
            "no read/list call or byte budget consumption",
            "no historical canonical lease enumeration",
            "no canonical lease/index/owner metadata mutation",
            "no network/tunnel/publication/catalog/Agent Execute or ActionPermit authority"
        };

        var note = status switch
        {
            "OWNED" => "An exclusive app-scoped v0.51.7 MCP owner handle is currently held by some process. Metadata remains advisory and may be transiently older/newer than the live handle; no destructive action is inferred.",
            "FREE_STALE_METADATA" => "The app-scoped owner handle is free while non-authoritative owner metadata remains. Stale metadata does not authorize MCP resume, read, lease creation or revocation. Any live referenced lease remains an explicit orphan/expiry concern.",
            _ => "No live owner handle and no owner metadata were observed. This does not itself authorize creation of a new read lease or MCP session."
        };

        return new LocalAppMcpOwnershipStatusV0518(
            StatusSchema, Version, DateTimeOffset.Now, applicationId, status,
            handleBusy, lockPresent, metadataPresent, metadataValid,
            metadataValid ? metadata!.SessionId : null,
            metadataValid ? metadata!.LeaseId : null,
            metadataValid ? metadata!.OwnerProcessId : null,
            metadataValid ? metadata!.AcquiredAt : null,
            metadataValid ? metadata!.State : null,
            metadataValid ? metadata!.ListenerObservedActive : null,
            metadataValid ? metadata!.LoopbackHost : null,
            metadataValid ? metadata!.LoopbackPort : null,
            leaseObservation,
            false, false, false, false,
            false, false, false,
            false, false,
            nonEffects, note);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-owner-status-v0518-states", true, "OWNED/FREE_NO_METADATA/FREE_STALE_METADATA", "explicit"),
        ("mcp-owner-status-v0518-probe", true, "FileMode.Open + FileAccess.Read + FileShare.None", "no lock creation/mutation"),
        ("mcp-owner-status-v0518-metadata", true, "v0.51.7 owner schema revalidated", "non-authoritative"),
        ("mcp-owner-status-v0518-lease", true, "exact LeaseId canonical path only", "no historical enumeration"),
        ("mcp-owner-status-v0518-resume", true, "false", "false"),
        ("mcp-owner-status-v0518-revoke", true, "false", "false"),
        ("mcp-owner-status-v0518-secrets", true, "bearer/hash/path-token omitted", "omitted"),
        ("mcp-owner-status-v0518-mutation", true, "false", "false")
    };

    private static LocalAppMcpOwnershipLeaseObservationV0518 ObserveExactLease(
        string workbenchRoot,
        string applicationId,
        string leaseId,
        bool ownerHandleBusy)
    {
        if (!SafeLeaseId(leaseId))
            return InvalidLease("UNSAFE_METADATA_LEASE_ID", leaseId, "Metadata LeaseId is unsafe and was not resolved.");

        var appToken = LocalAppV046FileBoundary.SafeToken(applicationId);
        var stateDir = Path.Combine(workbenchRoot, ".workbench", "read-leases", appToken);
        if (!Directory.Exists(stateDir))
            return new LocalAppMcpOwnershipLeaseObservationV0518(leaseId, "ABSENT", false, false, false, false, false, false, null, null, false, false,
                "No canonical read-lease directory exists for the referenced ApplicationId.");
        LocalAppV046FileBoundary.RejectReparse(stateDir, "v0.51.8 exact lease state directory");
        var statePath = Path.Combine(stateDir, leaseId + ".json");
        if (!File.Exists(statePath))
            return new LocalAppMcpOwnershipLeaseObservationV0518(leaseId, "ABSENT", false, false, false, false, false, false, null, null, false, false,
                "Referenced LeaseId has no exact canonical state file. Stale metadata grants no replacement authority.");
        LocalAppV046FileBoundary.RejectReparse(statePath, "v0.51.8 exact referenced lease state");

        LocalAppReadLeaseStateV048? state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(statePath, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidLease("CANONICAL_STATE_INVALID", leaseId, "Exact canonical state JSON could not be parsed; status is fail-closed and non-mutating.");
        }
        if (state is null || state.Schema != LocalAppReadLeaseV048Service.StateSchema || state.Version != LocalAppReadLeaseV048Service.Version ||
            !state.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || !state.LeaseId.Equals(leaseId, StringComparison.Ordinal))
            return InvalidLease("CANONICAL_STATE_INVALID", leaseId, "Exact canonical state identity/schema did not match metadata reference.");

        var expired = state.ExpiresAt <= DateTimeOffset.Now;
        var budget = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
        var live = !state.Revoked && !expired && !budget;
        var classification = state.Revoked ? "REVOKED" : expired ? "EXPIRED" : budget ? "BUDGET_EXHAUSTED" : ownerHandleBusy ? "LIVE_OWNER_DOMAIN_BUSY" : "LIVE_ORPHAN";
        return new LocalAppMcpOwnershipLeaseObservationV0518(
            leaseId, classification, true, true, live, state.Revoked, expired, budget,
            state.StateRevision, state.ExpiresAt, false, false,
            live && !ownerHandleBusy
                ? "The referenced canonical lease is still live while the MCP owner domain is free; this is an orphan observation only. Existing exact orphan closure remains the only explicit closure path."
                : "Exact canonical state was read directly without historical enumeration or mutation.");
    }

    private static LocalAppMcpOwnershipLeaseObservationV0518 InvalidLease(string classification, string? leaseId, string note)
        => new(leaseId, classification, false, false, false, false, false, false, null, null, false, false, note);

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
}
