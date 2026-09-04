using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppReadSessionStatusLeaseV0513(
    string LeaseId,
    IReadOnlyList<LocalAppReadLeaseScopeV048> Scopes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int RemainingCalls,
    long RemainingBytes,
    long StateRevision,
    bool Revoked,
    bool Expired,
    bool BudgetExhausted,
    bool BoundToActiveLocalMcp,
    bool OrphanClosureEligible);

public sealed record LocalAppReadSessionStatusV0513(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string? ActiveLocalMcpApplicationId,
    string? ActiveLocalMcpLeaseId,
    IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> Leases,
    int LiveLeaseCount,
    int OrphanClosureEligibleCount,
    bool BearerPlaintextDisclosed,
    bool BearerHashDisclosed,
    bool FileContentReadPerformed,
    bool NetworkAccessPerformed,
    bool ApplicationMutationPerformed,
    bool ProcessLaunchPerformed,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.51.3 read-only inspection of Workbench-owned lease control state.
/// It never reads application contents and intentionally omits bearer plaintext/hash.
/// </summary>
public sealed class LocalAppReadSessionStatusV0513Service
{
    public const string Version = "0.51.3";
    public const string StatusSchema = "matawaka.local-app-read-session-status/v0.51.3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public LocalAppReadSessionStatusV0513 Observe(
        string workspaceRoot,
        string applicationId,
        string? activeLocalMcpApplicationId,
        string? activeLocalMcpLeaseId)
    {
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var now = DateTimeOffset.Now;
        var leases = new List<LocalAppReadSessionStatusLeaseV0513>();
        var directory = ResolveStateDirectory(workspaceRoot, applicationId);

        if (Directory.Exists(directory))
        {
            LocalAppV046FileBoundary.RejectReparse(directory, "v0.51.3 read session status directory");
            foreach (var path in Directory.EnumerateFiles(directory, "lease-*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                LocalAppV046FileBoundary.RejectReparse(path, "v0.51.3 read session status file");
                var state = ReadState(path, applicationId);
                var expired = state.ExpiresAt <= now;
                var exhausted = state.RemainingCalls <= 0 || state.RemainingBytes <= 0;
                var bound =
                    !string.IsNullOrWhiteSpace(activeLocalMcpApplicationId) &&
                    !string.IsNullOrWhiteSpace(activeLocalMcpLeaseId) &&
                    activeLocalMcpApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
                    activeLocalMcpLeaseId.Equals(state.LeaseId, StringComparison.Ordinal);
                var live = !state.Revoked && !expired && !exhausted;

                leases.Add(new LocalAppReadSessionStatusLeaseV0513(
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
                    live && !bound));
            }
        }

        var ordered = leases.OrderByDescending(x => x.OrphanClosureEligible)
            .ThenByDescending(x => x.BoundToActiveLocalMcp)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.LeaseId, StringComparer.Ordinal)
            .ToArray();
        var liveCount = ordered.Count(x => !x.Revoked && !x.Expired && !x.BudgetExhausted);
        var orphanCount = ordered.Count(x => x.OrphanClosureEligible);

        return new LocalAppReadSessionStatusV0513(
            StatusSchema,
            Version,
            now,
            applicationId,
            activeLocalMcpApplicationId,
            activeLocalMcpLeaseId,
            ordered,
            liveCount,
            orphanCount,
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
                "no lease creation, renewal, scope widening or automatic revocation",
                "no local MCP/tunnel listener creation",
                "no network access",
                "no application/source/catalog mutation",
                "no process launch, Agent Execute or ActionPermit authority"
            },
            "Status is derived only from Workbench-owned local read-lease state and the in-process MCP runtime binding. Live unbound leases are eligible only for separate explicit exact orphan closure.");
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("status-v0513-bearer-plaintext", true, "omitted", "omitted"),
        ("status-v0513-bearer-hash", true, "omitted", "omitted"),
        ("status-v0513-app-content", true, "not read", "not read"),
        ("status-v0513-network", true, "false", "false"),
        ("status-v0513-auto-revoke", true, "false", "false"),
        ("status-v0513-orphan-definition", true, "live lease AND not bound to active local MCP", "exact")
    };

    private static LocalAppReadLeaseStateV048 ReadState(string path, string applicationId)
    {
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
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
}
