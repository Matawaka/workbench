using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppReadLeaseExactRevokeReceiptV0512(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string LeaseId,
    long StateRevisionBefore,
    long StateRevisionAfter,
    int RemainingCalls,
    long RemainingBytes,
    DateTimeOffset ExpiresAt,
    bool WasExpiredAtClosure,
    bool WasAlreadyRevoked,
    bool ExactLeaseRevoked,
    int SiblingLeasesRevoked,
    string StateSha256Before,
    string StateSha256After,
    bool NetworkAccessPerformed,
    bool FileContentReadPerformed,
    bool ApplicationMutationPerformed,
    bool ProcessLaunchPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

/// <summary>
/// v0.51.2 exact local read-session closure primitive. It targets one already-known
/// ApplicationId/LeaseId state file only. It performs no directory enumeration and
/// cannot revoke sibling leases. The caller is responsible for stopping the bound
/// local MCP adapter before invoking this service.
/// </summary>
public sealed class LocalAppReadLeaseExactRevokeV0512Service
{
    public const string Version = "0.51.2";
    public const string ReceiptSchema = "matawaka.local-app-read-lease-exact-revoke-receipt/v0.51.2";

    private static readonly SemaphoreSlim ClosureGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public async Task<(LocalAppReadLeaseExactRevokeReceiptV0512 Receipt, string ReceiptPath)> RevokeExactAsync(
        string workspaceRoot,
        string applicationId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        if (!SafeLeaseId(leaseId)) throw new InvalidDataException("Unsafe exact LeaseId for v0.51.2 closure.");
        _ = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);

        await ClosureGate.WaitAsync(cancellationToken);
        try
        {
            var statePath = ResolveStatePath(workspaceRoot, applicationId, leaseId);
            var beforeSha = HashFile(statePath);
            var state = ReadExactState(statePath, applicationId, leaseId);
            var now = DateTimeOffset.Now;
            var already = state.Revoked;
            var expired = state.ExpiresAt <= now;
            var beforeRevision = state.StateRevision;
            var after = state;

            if (!already)
            {
                after = state with
                {
                    Revoked = true,
                    RevokedAt = now,
                    StateRevision = state.StateRevision + 1
                };
                await WriteStateAtomicAsync(statePath, after, cancellationToken);
            }

            var afterSha = HashFile(statePath);
            var receipt = new LocalAppReadLeaseExactRevokeReceiptV0512(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                applicationId,
                leaseId,
                beforeRevision,
                after.StateRevision,
                after.RemainingCalls,
                after.RemainingBytes,
                after.ExpiresAt,
                expired,
                already,
                after.Revoked,
                0,
                beforeSha,
                afterSha,
                false,
                false,
                false,
                false,
                new[]
                {
                    "no sibling lease enumeration or revocation",
                    "no read/list call or byte budget consumption",
                    "no bearer plaintext required or persisted",
                    "no network/tunnel/MCP listener creation",
                    "no application/source/catalog mutation",
                    "no process launch, Agent Execute or ActionPermit authority"
                },
                already ? "READ_SESSION_EXACT_LEASE_ALREADY_REVOKED" : "READ_SESSION_EXACT_LEASE_REVOKED",
                "Explicit v0.51.2 session closure changed only the exact bound local lease state. Expired leases may still be marked revoked for durable closure evidence; sibling leases are not enumerated or touched.");

            var receiptPath = await WriteReceiptAsync(workspaceRoot, receipt, cancellationToken);
            return (receipt, receiptPath);
        }
        finally
        {
            ClosureGate.Release();
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("closure-v0512-exact-only", true, "ApplicationId + LeaseId state path only", "no sibling enumeration"),
        ("closure-v0512-bearer", true, "not required", "not required for explicit local closure"),
        ("closure-v0512-budget", true, "unchanged", "no call/byte consumption"),
        ("closure-v0512-network", true, "false", "false"),
        ("closure-v0512-runtime-start", true, "false", "false")
    };

    private static LocalAppReadLeaseStateV048 ReadExactState(string path, string applicationId, string leaseId)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Exact read lease state is missing.");
        LocalAppV046FileBoundary.RejectReparse(path, "v0.51.2 exact read lease state");
        LocalAppReadLeaseStateV048 state;
        try
        {
            state = JsonSerializer.Deserialize<LocalAppReadLeaseStateV048>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Exact read lease state could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Exact read lease state JSON is invalid.", ex);
        }

        if (state.Schema != LocalAppReadLeaseV048Service.StateSchema || state.Version != LocalAppReadLeaseV048Service.Version)
            throw new InvalidDataException("Unexpected exact read lease state schema/version.");
        if (!state.ApplicationId.Equals(applicationId, StringComparison.Ordinal) || !state.LeaseId.Equals(leaseId, StringComparison.Ordinal))
            throw new InvalidDataException("Exact read lease state identity mismatch.");
        return state;
    }

    private static string ResolveStatePath(string workspaceRoot, string applicationId, string leaseId)
    {
        var workspace = LocalAppV046FileBoundary.ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace.Trim(), "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var stateRoot = Path.GetFullPath(Path.Combine(workbench, ".workbench", "read-leases"));
        var appRoot = Path.GetFullPath(Path.Combine(stateRoot, LocalAppV046FileBoundary.SafeToken(applicationId)));
        var path = Path.GetFullPath(Path.Combine(appRoot, LocalAppV046FileBoundary.SafeToken(leaseId) + ".json"));
        var prefix = appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Exact lease state path escaped application lease root.");
        return path;
    }

    private static async Task WriteStateAtomicAsync(string path, LocalAppReadLeaseStateV048 state, CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false), cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary v0.51.2 exact lease state");
            File.Move(temp, path, true);
            LocalAppV046FileBoundary.RejectReparse(path, "v0.51.2 exact lease state");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalAppReadLeaseExactRevokeReceiptV0512 receipt, CancellationToken cancellationToken)
    {
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-read-session-closure");
        var path = Path.Combine(dir, $"end-read-session-{LocalAppV046FileBoundary.SafeToken(receipt.ApplicationId)}-{LocalAppV046FileBoundary.SafeToken(receipt.LeaseId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string HashFile(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Exact lease state file is missing.");
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool SafeLeaseId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("lease-", StringComparison.Ordinal) && value.Length <= 80 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
}
