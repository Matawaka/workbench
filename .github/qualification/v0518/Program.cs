using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private const string EndpointSecret = "V0518-ENDPOINT-SECRET-MUST-NOT-APPEAR";

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void MakeApp(string workspace, string id)
    {
        var root = Path.Combine(workspace, "Apps", id);
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(
            Path.Combine(root, ".matawaka-app.json"),
            JsonSerializer.Serialize(new LocalApplicationIdentity(LocalApplicationMaintenanceService.IdentitySchema, id, "1.0.0")),
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "data", "state.json"), "{\"ok\":true}", new UTF8Encoding(false));
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v0518-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        MakeApp(workspace, "alpha");

        var statusService = new LocalAppMcpOwnershipStatusV0518Service();
        var initial = statusService.Observe(workspace, "alpha");
        if (initial.Status != "FREE_NO_METADATA" || initial.OwnerHandleBusy || initial.OwnerMetadataPresent ||
            initial.ResumeAuthorityGranted || initial.LeaseAuthorityGranted || initial.ReadAuthorityGranted || initial.RevokeAuthorityGranted)
            throw new Exception("initial FREE_NO_METADATA status mismatch");

        var leases = new LocalAppReadLeaseV048Service();
        var request = new LocalAppReadLeaseRequestV048(
            LocalAppReadLeaseV048Service.RequestSchema,
            "v0518:status:lease",
            "alpha",
            new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
            65536, 262144, 8, 900);
        var preview = leases.Preview(workspace, "alpha", request, default);
        var created = await leases.CreateAsync(workspace, "alpha", preview, false, default);
        var statePath = created.Receipt.StatePath;
        var stateBefore = Sha(statePath);

        var ownerService = new LocalAppMcpSessionOwnershipV0517Service();
        var held = await ownerService.AcquireAsync(workspace, "alpha", "v0518-status-hostile", default, 1000);
        await ownerService.BindExactLeaseAsync(held, created.Grant.LeaseId, default);
        var grant = new LocalAppMcpAdapterGrantV049(
            LocalAppMcpReadAdapterV049Service.GrantSchema,
            LocalAppMcpReadAdapterV049Service.Version,
            DateTimeOffset.Now,
            "alpha",
            created.Grant.LeaseId,
            "http://127.0.0.1:45680/mcp/" + EndpointSecret,
            "qualification-only-not-persisted",
            created.Grant.ExpiresAt,
            new[] { "read_local_app_chunk", "list_local_app_entries" },
            true, false, false,
            "v0.51.8 qualification only");
        await ownerService.MarkListenerReadyAsync(held, grant, default);

        var owned = statusService.Observe(workspace, "alpha");
        if (owned.Status != "OWNED" || !owned.OwnerHandleBusy || !owned.OwnerMetadataPresent || !owned.OwnerMetadataValid ||
            owned.MetadataLeaseId != created.Grant.LeaseId || owned.LeaseObservation.Classification != "LIVE_OWNER_DOMAIN_BUSY" ||
            !owned.LeaseObservation.Live || owned.CanonicalHistoricalScanPerformed || owned.CanonicalStateMutationPerformed)
            throw new Exception("OWNED status mismatch");
        if (Sha(statePath) != stateBefore) throw new Exception("owned status mutated canonical lease bytes");

        var ownedJson = JsonSerializer.Serialize(owned);
        if (ownedJson.Contains(created.Grant.Bearer, StringComparison.OrdinalIgnoreCase) ||
            ownedJson.Contains(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase) ||
            ownedJson.Contains(EndpointSecret, StringComparison.Ordinal))
            throw new Exception("owned status leaked bearer/hash/endpoint path token");

        // Simulate process crash: release only the OS/file handle; leave LISTENER_READY metadata untouched.
        await held.DisposeAsync();
        var stale = statusService.Observe(workspace, "alpha");
        if (stale.Status != "FREE_STALE_METADATA" || stale.OwnerHandleBusy || !stale.OwnerMetadataPresent || !stale.OwnerMetadataValid ||
            stale.MetadataLeaseId != created.Grant.LeaseId || stale.LeaseObservation.Classification != "LIVE_ORPHAN" ||
            !stale.LeaseObservation.Live || stale.ResumeAuthorityGranted || stale.RevokeAuthorityGranted)
            throw new Exception("FREE_STALE_METADATA live-orphan status mismatch");
        if (Sha(statePath) != stateBefore) throw new Exception("stale status mutated canonical lease bytes");

        var metadataPath = Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", "alpha", "owner-v0.51.7.json");
        var metadataBefore = Sha(metadataPath);
        _ = statusService.Observe(workspace, "alpha");
        if (Sha(metadataPath) != metadataBefore) throw new Exception("status mutated stale owner metadata");

        // Forge non-secret but non-existent exact LeaseId metadata; it must classify ABSENT and remain non-authoritative.
        var forged = new LocalAppMcpSessionOwnerV0517(
            LocalAppMcpSessionOwnershipV0517Service.OwnerSchema,
            LocalAppMcpSessionOwnershipV0517Service.Version,
            DateTimeOffset.Now,
            "alpha",
            "mcpsess-forged-observation-only",
            "lease-00000000000000000000000000000000",
            424242,
            DateTimeOffset.Now.AddMinutes(-1),
            "LISTENER_READY_OWNED",
            true,
            "127.0.0.1",
            45681,
            false, false, false, false,
            Array.Empty<string>(),
            "qualification forged metadata; grants no authority");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(forged), new UTF8Encoding(false));
        var absent = statusService.Observe(workspace, "alpha");
        if (absent.Status != "FREE_STALE_METADATA" || !absent.OwnerMetadataValid ||
            absent.LeaseObservation.Classification != "ABSENT" || absent.LeaseObservation.CanonicalStatePresent ||
            absent.ResumeAuthorityGranted || absent.LeaseAuthorityGranted || absent.ReadAuthorityGranted || absent.RevokeAuthorityGranted)
            throw new Exception("forged metadata ABSENT classification mismatch");

        var absentJson = JsonSerializer.Serialize(absent);
        if (absentJson.Contains(created.Grant.Bearer, StringComparison.OrdinalIgnoreCase) ||
            absentJson.Contains(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase) ||
            absentJson.Contains(EndpointSecret, StringComparison.Ordinal))
            throw new Exception("stale/absent status leaked secret material");

        Console.WriteLine(
            "V0518_STATUS_RUNTIME_PASS freeNoMetadata=true owned=true freeStaleMetadata=true liveOwnerBusy=true liveOrphan=true absent=true " +
            "historicalScan=false canonicalMutation=false ownerMetadataMutation=false resumeAuthority=false revokeAuthority=false bearer=false endpointSecret=false");
    }
}
