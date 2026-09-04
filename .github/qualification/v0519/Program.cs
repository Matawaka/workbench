using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ShaFile(string path)
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

    private static string AppDir(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", app);

    private static string MetadataPath(string workspace, string app)
        => Path.Combine(AppDir(workspace, app), "owner-v0.51.7.json");

    private static string EvidenceDir(string workspace, string app)
        => Path.Combine(AppDir(workspace, app), "generation-evidence-v0519");

    private static LocalAppMcpSessionOwnerV0517 Prior(string app, string session, string? lease = null, string state = "LISTENER_STOPPED_OWNER_RELEASING")
        => new(
            LocalAppMcpSessionOwnershipV0517Service.OwnerSchema,
            LocalAppMcpSessionOwnershipV0517Service.Version,
            DateTimeOffset.Now.AddMinutes(-2),
            app,
            session,
            lease,
            12345,
            DateTimeOffset.Now.AddMinutes(-3),
            state,
            false,
            null,
            null,
            false, false, false, false,
            Array.Empty<string>(),
            "qualification prior metadata");

    private static LocalAppMcpOwnerGenerationTransitionReceiptV0519 LatestGenerationReceipt(string workspace)
    {
        var dir = Path.Combine(workspace, "Workbench", "artifacts", "local-mcp-owner-generation-v0519");
        var file = Directory.GetFiles(dir, "owner-generation-*.json").OrderByDescending(File.GetLastWriteTimeUtc).First();
        return JsonSerializer.Deserialize<LocalAppMcpOwnerGenerationTransitionReceiptV0519>(File.ReadAllText(file))
               ?? throw new Exception("generation receipt parse failed");
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v0519-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        foreach (var app in new[] { "none", "valid", "opaque", "oversize", "failarchive", "busy" }) MakeApp(workspace, app);
        var service = new LocalAppMcpSessionOwnershipV0517Service();

        // No prior metadata: successor generation starts normally and records NO_PRIOR_OWNER_METADATA.
        var noPrior = await service.AcquireAsync(workspace, "none", "v0519-no-prior", default, 500);
        var noPriorReceipt = LatestGenerationReceipt(workspace);
        if (noPriorReceipt.Status != "NO_PRIOR_OWNER_METADATA" || noPriorReceipt.PriorMetadataPresent || noPriorReceipt.PriorMetadataArchived)
            throw new Exception("no-prior generation receipt mismatch");
        await service.ReleaseUnstartedAsync(noPrior, true, "qualification", default);

        // Valid stale metadata: exact prior bytes are archived/hash-verified before successor metadata replaces active slot.
        Directory.CreateDirectory(AppDir(workspace, "valid"));
        var validPrior = Prior("valid", "mcpsess-priorvalid", "lease-priorvalid");
        var validBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(validPrior));
        File.WriteAllBytes(MetadataPath(workspace, "valid"), validBytes);
        var validSha = Sha(validBytes);
        var validHeld = await service.AcquireAsync(workspace, "valid", "v0519-valid", default, 500);
        var validReceipt = LatestGenerationReceipt(workspace);
        if (validReceipt.Status != "PRIOR_OWNER_METADATA_PRESERVED_VALID" || !validReceipt.PriorMetadataPresent ||
            !validReceipt.PriorMetadataContractValid || validReceipt.PriorSessionId != validPrior.SessionId ||
            validReceipt.PriorLeaseId != validPrior.LeaseId || validReceipt.PriorMetadataSha256 != validSha ||
            !validReceipt.PriorMetadataArchived || !validReceipt.ArchiveHashVerified || validReceipt.ArchivePath is null ||
            ShaFile(validReceipt.ArchivePath) != validSha)
            throw new Exception("valid prior generation preservation mismatch");
        var successor = JsonSerializer.Deserialize<LocalAppMcpSessionOwnerV0517>(File.ReadAllText(MetadataPath(workspace, "valid")))
                        ?? throw new Exception("successor owner parse failed");
        if (successor.SessionId == validPrior.SessionId || successor.SessionId != validHeld.SessionId)
            throw new Exception("successor owner generation did not replace active slot correctly");
        await service.ReleaseUnstartedAsync(validHeld, true, "qualification", default);

        // Invalid JSON is preserved as opaque bytes and never promoted to authority fields.
        Directory.CreateDirectory(AppDir(workspace, "opaque"));
        var opaqueBytes = Encoding.UTF8.GetBytes("{\"corrupt\":true,\"payload\":\"opaque-prior\"}");
        File.WriteAllBytes(MetadataPath(workspace, "opaque"), opaqueBytes);
        var opaqueSha = Sha(opaqueBytes);
        var opaqueHeld = await service.AcquireAsync(workspace, "opaque", "v0519-opaque", default, 500);
        var opaqueReceipt = LatestGenerationReceipt(workspace);
        if (opaqueReceipt.Status != "PRIOR_OWNER_METADATA_PRESERVED_OPAQUE_UNTRUSTED" || !opaqueReceipt.PriorMetadataPresent ||
            opaqueReceipt.PriorMetadataContractValid || opaqueReceipt.PriorSessionId is not null || opaqueReceipt.PriorLeaseId is not null ||
            opaqueReceipt.PriorMetadataSha256 != opaqueSha || !opaqueReceipt.ArchiveHashVerified || opaqueReceipt.ArchivePath is null ||
            ShaFile(opaqueReceipt.ArchivePath) != opaqueSha || opaqueReceipt.LeaseAuthorityGranted || opaqueReceipt.ReadAuthorityGranted ||
            opaqueReceipt.RevokeAuthorityGranted || opaqueReceipt.ResumeAuthorityGranted)
            throw new Exception("opaque prior generation preservation mismatch");
        await service.ReleaseUnstartedAsync(opaqueHeld, true, "qualification", default);

        // Oversize metadata refuses before successor overwrite; prior bytes remain and owner handle is released.
        Directory.CreateDirectory(AppDir(workspace, "oversize"));
        var oversizePath = MetadataPath(workspace, "oversize");
        var oversizeBytes = new byte[LocalAppMcpOwnerGenerationV0519Service.MaxPriorMetadataBytes + 1];
        Array.Fill<byte>(oversizeBytes, (byte)'x');
        File.WriteAllBytes(oversizePath, oversizeBytes);
        var oversizeSha = Sha(oversizeBytes);
        var oversizeRefused = false;
        try { _ = await service.AcquireAsync(workspace, "oversize", "v0519-oversize", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_OWNER_GENERATION_PRIOR_METADATA_OVERSIZE", StringComparison.Ordinal)) { oversizeRefused = true; }
        if (!oversizeRefused || ShaFile(oversizePath) != oversizeSha)
            throw new Exception("oversize prior metadata did not fail closed intact");
        using (var proveReleased = new FileStream(Path.Combine(AppDir(workspace, "oversize"), "owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        // Evidence-directory failure refuses before successor overwrite and releases ownership.
        Directory.CreateDirectory(AppDir(workspace, "failarchive"));
        var failPrior = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Prior("failarchive", "mcpsess-priorfail")));
        var failPath = MetadataPath(workspace, "failarchive");
        File.WriteAllBytes(failPath, failPrior);
        var failSha = Sha(failPrior);
        File.WriteAllText(EvidenceDir(workspace, "failarchive"), "block-directory-creation");
        var archiveRefused = false;
        try { _ = await service.AcquireAsync(workspace, "failarchive", "v0519-failarchive", default, 500); }
        catch (Exception) { archiveRefused = true; }
        if (!archiveRefused || ShaFile(failPath) != failSha)
            throw new Exception("archive failure did not preserve prior active metadata");
        using (var proveReleased = new FileStream(Path.Combine(AppDir(workspace, "failarchive"), "owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        // Busy domain refuses before second generation preservation/metadata mutation.
        var busyHeld = await service.AcquireAsync(workspace, "busy", "v0519-owner-a", default, 500);
        var busyMetadata = MetadataPath(workspace, "busy");
        var busyShaBefore = ShaFile(busyMetadata);
        var busyEvidenceBefore = Directory.Exists(EvidenceDir(workspace, "busy")) ? Directory.GetFiles(EvidenceDir(workspace, "busy")).Length : 0;
        var busyRefused = false;
        try { _ = await service.AcquireAsync(workspace, "busy", "v0519-owner-b", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { busyRefused = true; }
        var busyEvidenceAfter = Directory.Exists(EvidenceDir(workspace, "busy")) ? Directory.GetFiles(EvidenceDir(workspace, "busy")).Length : 0;
        if (!busyRefused || ShaFile(busyMetadata) != busyShaBefore || busyEvidenceBefore != busyEvidenceAfter)
            throw new Exception("busy owner contention mutated generation evidence/metadata");
        await service.ReleaseUnstartedAsync(busyHeld, true, "qualification", default);

        Console.WriteLine(
            "V0519_GENERATION_RUNTIME_PASS noPrior=true validArchived=true opaqueArchived=true oversizeRefused=true archiveFailureRefused=true busyNoMutation=true " +
            "archiveHashVerified=true ownerReleasedOnFailure=true authority=false secrets=false");
    }
}
