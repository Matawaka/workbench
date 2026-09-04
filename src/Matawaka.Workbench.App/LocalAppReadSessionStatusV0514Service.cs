using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppReadSessionStatusV0514(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string? ActiveLocalMcpApplicationId,
    string? ActiveLocalMcpLeaseId,
    IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> LiveAuthorities,
    IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> HistoricalLeases,
    int TotalLeaseRecords,
    int LiveLeaseCount,
    int OrphanClosureEligibleCount,
    int HistoricalLeaseCount,
    int HistoryOffset,
    int HistoryLimit,
    int HistoricalReturned,
    int? NextHistoryOffset,
    bool HistoryTruncated,
    int LiveAuthorityHardLimit,
    int LeaseStateFilesParsed,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool ApplicationFileContentReadPerformed,
    bool ReadListBudgetConsumed,
    bool LeaseStateMutationPerformed,
    bool NetworkAccessPerformed,
    bool ProcessLaunchPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.51.4 bounded representation over preserved v0.48 lease-state evidence.
/// The service still performs the legacy v0.51.3 full state classification pass so it
/// never silently hides live authority. It bounds only the returned historical view.
/// A later index layer may bound the filesystem scan itself without changing this contract.
/// </summary>
public sealed class LocalAppReadSessionStatusV0514Service
{
    public const string Version = "0.51.4";
    public const string StatusSchema = "matawaka.local-app-read-session-status/v0.51.4";
    public const int DefaultHistoryLimit = 16;
    public const int MaxHistoryLimit = 64;
    public const int LiveAuthorityHardLimit = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public LocalAppReadSessionStatusV0514 Observe(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        int historyOffset = 0,
        int historyLimit = DefaultHistoryLimit)
    {
        ValidatePage(historyOffset, historyLimit);
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var now = DateTimeOffset.Now;
        var leases = ReadAllLeaseMetadata(workspaceRoot, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId, now);

        var live = leases
            .Where(IsLive)
            .OrderByDescending(x => x.BoundToActiveLocalMcp)
            .ThenByDescending(x => x.OrphanClosureEligible)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.LeaseId, StringComparer.Ordinal)
            .ToArray();

        if (live.Length > LiveAuthorityHardLimit)
            throw new InvalidDataException(
                $"LIVE_AUTHORITY_OVERFLOW: app={applicationId}; live={live.Length}; hardLimit={LiveAuthorityHardLimit}; bounded status refuses partial live-authority disclosure. Use explicit recovery controls; no automatic revocation was performed.");

        var historical = leases
            .Where(x => !IsLive(x))
            .OrderByDescending(x => x.IssuedAt)
            .ThenByDescending(x => x.ExpiresAt)
            .ThenBy(x => x.LeaseId, StringComparer.Ordinal)
            .ToArray();

        if (historyOffset > historical.Length)
            throw new InvalidDataException(
                $"HistoryOffset {historyOffset} exceeds historical lease count {historical.Length}.");

        var page = historical.Skip(historyOffset).Take(historyLimit).ToArray();
        var next = historyOffset + page.Length < historical.Length
            ? historyOffset + page.Length
            : (int?)null;
        var orphanCount = live.Count(x => x.OrphanClosureEligible);

        return new LocalAppReadSessionStatusV0514(
            StatusSchema,
            Version,
            now,
            applicationId,
            activeLocalMcpApplicationId,
            activeLocalMcpLeaseId,
            live,
            page,
            leases.Count,
            live.Length,
            orphanCount,
            historical.Length,
            historyOffset,
            historyLimit,
            page.Length,
            next,
            page.Length != historical.Length,
            LiveAuthorityHardLimit,
            leases.Count,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "no bearer plaintext or bearer hash disclosure",
                "no application file content read",
                "no read/list lease budget consumption",
                "no lease creation, renewal, scope widening, deletion, archival mutation or automatic revocation",
                "no historical lease-state deletion or compaction",
                "no local MCP/tunnel listener creation",
                "no network access",
                "no application/source/catalog mutation",
                "no process launch, Agent Execute or ActionPermit authority"
            },
            "READ_SESSION_STATUS_BOUNDED",
            "All lease-state records are classified to avoid hiding live authority. Live authorities are returned in full up to the fixed hard ceiling; historical evidence is paginated newest-first. Filesystem scan/index optimization is intentionally deferred.");
    }

    public LocalAppReadSessionStatusLeaseV0513 ObserveExactLease(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId)
    {
        if (!SafeLeaseId(leaseId))
            throw new InvalidDataException("Unsafe exact LeaseId for v0.51.4 status lookup.");
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var path = ResolveExactStatePath(workspaceRoot, applicationId, leaseId);
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.4 exact read session status file");
        var state = ReadState(path, applicationId);
        return ToStatus(state, DateTimeOffset.Now, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("status-v0514-history-default", DefaultHistoryLimit == 16, DefaultHistoryLimit.ToString(), "16"),
        ("status-v0514-history-max", MaxHistoryLimit == 64, MaxHistoryLimit.ToString(), "64"),
        ("status-v0514-live-hard-limit", LiveAuthorityHardLimit == 32, LiveAuthorityHardLimit.ToString(), "32"),
        ("status-v0514-live-not-paginated", true, "all live returned or explicit overflow", "never silently partial"),
        ("status-v0514-history-order", true, "IssuedAt descending + deterministic tie-break", "newest-first"),
        ("status-v0514-bearer", true, "plaintext/hash omitted", "omitted"),
        ("status-v0514-evidence-preserved", true, "status performs no lease-state writes/deletes", "preserved"),
        ("status-v0514-filesystem-index", true, "deferred", "no v0.48 writer change")
    };

    private static List<LocalAppReadSessionStatusLeaseV0513> ReadAllLeaseMetadata(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId,
        DateTimeOffset now)
    {
        var result = new List<LocalAppReadSessionStatusLeaseV0513>();
        var directory = ResolveStateDirectory(workspaceRoot, applicationId);
        if (!Directory.Exists(directory)) return result;

        LocalAppV046FileBoundary.RejectReparse(directory, "v0.51.4 read session status directory");
        foreach (var path in Directory.EnumerateFiles(directory, "lease-*.json"))
        {
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.4 read session status file");
            var state = ReadState(path, applicationId);
            result.Add(ToStatus(state, now, applicationId, activeLocalMcpApplicationId, activeLocalMcpLeaseId));
        }
        return result;
    }

    private static LocalAppReadSessionStatusLeaseV0513 ToStatus(
        LocalAppReadLeaseStateV048 state,
        DateTimeOffset now,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId)
    {
        var expired = state.ExpiresAt <= now;
        var exhausted = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
        var bound =
            !string.IsNullOrWhiteSpace(activeLocalMcpApplicationId) &&
            !string.IsNullOrWhiteSpace(activeLocalMcpLeaseId) &&
            activeLocalMcpApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
            activeLocalMcpLeaseId.Equals(state.LeaseId, StringComparison.Ordinal);
        var live = !state.Revoked && !expired && !exhausted;

        return new LocalAppReadSessionStatusLeaseV0513(
            state.LeaseId,
            state.Scopes,
            state.IssuedAt,
            state.ExpiresAt,
            state.RemainingCalls,
            state.RemainingBytes,
            state.StateRevision,
            state.Revoked,
            expired,
            exhausted,
            bound,
            live && !bound);
    }

    private static bool IsLive(LocalAppReadSessionStatusLeaseV0513 lease)
        => !lease.Revoked && !lease.Expired && !lease.BudgetExhausted;

    private static LocalAppReadLeaseStateV048 ReadState(string path, string applicationId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Read session status state is missing.");
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Read session status state could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Read session status encountered invalid lease JSON.", ex);
        }

        if (state.Schema != LocalAppReadLeaseV048Service.StateSchema || state.Version != LocalAppReadLeaseV048Service.Version)
            throw new InvalidDataException("Read session status encountered unexpected lease schema/version.");
        if (!state.ApplicationId.Equals(applicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Read session status lease ApplicationId mismatch.");
        var expectedName = LocalAppV046FileBoundary.SafeToken(state.LeaseId) + ".json";
        if (!Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Read session status lease filename/identity mismatch.");
        return state;
    }

    private static string ResolveStateDirectory(string workspaceRoot, string applicationId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var stateRoot = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-leases"));
        var appRoot = Path.GetFullPath(Path.Combine(stateRoot, LocalAppV046FileBoundary.SafeToken(applicationId)));
        var prefix = stateRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!appRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Read session status directory escaped read-leases root.");
        return appRoot;
    }

    private static string ResolveExactStatePath(string workspaceRoot, string applicationId, string leaseId)
    {
        var appRoot = ResolveStateDirectory(workspaceRoot, applicationId);
        var path = Path.GetFullPath(Path.Combine(appRoot, LocalAppV046FileBoundary.SafeToken(leaseId) + ".json"));
        var prefix = appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact status path escaped application lease root.");
        return path;
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0) throw new InvalidDataException("HistoryOffset must be non-negative.");
        if (limit < 1 || limit > MaxHistoryLimit)
            throw new InvalidDataException($"HistoryLimit must be between 1 and {MaxHistoryLimit}.");
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.StartsWith("lease-", StringComparison.Ordinal) &&
           value.Length <= 80 &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
}
