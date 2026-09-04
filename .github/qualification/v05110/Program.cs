using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

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
    private static string TxPath(string workspace, string app)
        => Path.Combine(AppDir(workspace, app), "generation-transition-v05110.json");
    private static string EvidenceDir(string workspace, string app)
        => Path.Combine(AppDir(workspace, app), "generation-evidence-v0519");

    private static LocalAppMcpSessionOwnerV0517 Owner(string app, string session, string? lease = null, string state = "LISTENER_STOPPED_OWNER_RELEASING")
        => new(
            LocalAppMcpSessionOwnershipV0517Service.OwnerSchema,
            LocalAppMcpSessionOwnershipV0517Service.Version,
            DateTimeOffset.Now,
            app,
            session,
            lease,
            777,
            DateTimeOffset.Now,
            state,
            false,
            null,
            null,
            false, false, false, false,
            Array.Empty<string>(),
            "v0.51.10 qualification owner");

    private static void WriteOwner(string workspace, string app, LocalAppMcpSessionOwnerV0517 owner)
    {
        Directory.CreateDirectory(AppDir(workspace, app));
        File.WriteAllText(MetadataPath(workspace, app), JsonSerializer.Serialize(owner, Json), new UTF8Encoding(false));
    }

    private static LocalAppMcpOwnerGenerationTransactionV05110 ReadTx(string workspace, string app)
        => JsonSerializer.Deserialize<LocalAppMcpOwnerGenerationTransactionV05110>(File.ReadAllText(TxPath(workspace, app)))
           ?? throw new Exception("transaction parse failed");

    private static int EvidenceFileCount(string workspace, string app)
        => Directory.Exists(EvidenceDir(workspace, app))
            ? Directory.GetFiles(EvidenceDir(workspace, app), "*", SearchOption.AllDirectories).Length
            : 0;

    private static void RequireNoAuthority(LocalAppMcpOwnerGenerationTransactionV05110 tx)
    {
        if (tx.CanonicalLeaseMutated || tx.ActiveIndexMutated || tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted ||
            tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted || tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new Exception("generation transaction widened authority/secret boundary");
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v05110-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        foreach (var app in new[] { "normal", "abandoned", "preprepare", "recovered", "absent", "inconsistent", "hashbad", "busy" })
            MakeApp(workspace, app);

        var ownerService = new LocalAppMcpSessionOwnershipV0517Service();
        var generation = new LocalAppMcpOwnerGenerationV0519Service();
        var txService = new LocalAppMcpOwnerGenerationTransactionV05110Service();

        // Full integration: owner Acquire must return only after exact COMMITTED successor metadata observation.
        var normalHeld = await ownerService.AcquireAsync(workspace, "normal", "v05110-normal", default, 500);
        var normalTx = ReadTx(workspace, "normal");
        if (normalTx.State != "COMMITTED" || normalTx.SuccessorSessionId != normalHeld.SessionId ||
            !normalTx.SuccessorMetadataContractValid || string.IsNullOrWhiteSpace(normalTx.SuccessorMetadataSha256))
            throw new Exception("normal owner acquisition did not finish COMMITTED transaction");
        RequireNoAuthority(normalTx);
        await ownerService.ReleaseUnstartedAsync(normalHeld, true, "qualification", default);

        // PREPARED + exact prior bytes still active => abandoned before successor, with archive reuse.
        var abandonedPrior = Owner("abandoned", "mcpsess-abandoned-prior", "lease-abandoned-prior");
        WriteOwner(workspace, "abandoned", abandonedPrior);
        var abandonedPriorSha = ShaFile(MetadataPath(workspace, "abandoned"));
        var abandonedSuccA = "mcpsess-" + Guid.NewGuid().ToString("N");
        var genA = await generation.PreservePriorBeforeSuccessorAsync(workspace, "abandoned", abandonedSuccA, MetadataPath(workspace, "abandoned"), default);
        await txService.PrepareAsync(workspace, "abandoned", abandonedSuccA, MetadataPath(workspace, "abandoned"), genA.Receipt, false, default);
        var abandonedRecon = await txService.ReconcileBeforePrepareAsync(workspace, "abandoned", MetadataPath(workspace, "abandoned"), default);
        if (abandonedRecon.Status != "PRIOR_PREPARED_ABANDONED_REUSE_ARCHIVE" ||
            abandonedRecon.PriorTerminalState != "ABANDONED_BEFORE_SUCCESSOR" ||
            string.IsNullOrWhiteSpace(abandonedRecon.VerifiedReuseArchivePath) ||
            ShaFile(MetadataPath(workspace, "abandoned")) != abandonedPriorSha)
            throw new Exception("abandoned PREPARED transaction was not recovered exactly");
        var evidenceBeforeReuse = EvidenceFileCount(workspace, "abandoned");
        var abandonedSuccB = "mcpsess-" + Guid.NewGuid().ToString("N");
        var genB = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "abandoned", abandonedSuccB, MetadataPath(workspace, "abandoned"), default,
            abandonedRecon.VerifiedReuseArchivePath);
        if (genB.Receipt.ArchivePath != abandonedRecon.VerifiedReuseArchivePath || EvidenceFileCount(workspace, "abandoned") != evidenceBeforeReuse)
            throw new Exception("retry duplicated prior evidence instead of reusing verified archive");

        // Crash even before PREPARED: content-addressed v0.51.9 archive must deduplicate exact prior bytes.
        WriteOwner(workspace, "preprepare", Owner("preprepare", "mcpsess-preprepare-prior"));
        var preA = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "preprepare", "mcpsess-" + Guid.NewGuid().ToString("N"), MetadataPath(workspace, "preprepare"), default);
        var preCount = EvidenceFileCount(workspace, "preprepare");
        var preB = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "preprepare", "mcpsess-" + Guid.NewGuid().ToString("N"), MetadataPath(workspace, "preprepare"), default);
        if (preA.Receipt.ArchivePath != preB.Receipt.ArchivePath || EvidenceFileCount(workspace, "preprepare") != preCount)
            throw new Exception("pre-PREPARED retry duplicated content-addressed prior evidence");

        // PREPARED + exact successor metadata present => COMMITTED_RECOVERED.
        var recoverSucc = "mcpsess-" + Guid.NewGuid().ToString("N");
        var recoverGen = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "recovered", recoverSucc, MetadataPath(workspace, "recovered"), default);
        await txService.PrepareAsync(workspace, "recovered", recoverSucc, MetadataPath(workspace, "recovered"), recoverGen.Receipt, false, default);
        WriteOwner(workspace, "recovered", Owner("recovered", recoverSucc, null, "OWNERSHIP_ACQUIRED_UNBOUND"));
        var recovered = await txService.ReconcileBeforePrepareAsync(workspace, "recovered", MetadataPath(workspace, "recovered"), default);
        var recoveredTx = ReadTx(workspace, "recovered");
        if (recovered.Status != "PRIOR_PREPARED_COMMITTED_RECOVERED" || recoveredTx.State != "COMMITTED_RECOVERED" ||
            !recoveredTx.SuccessorMetadataContractValid || string.IsNullOrWhiteSpace(recoveredTx.SuccessorMetadataSha256))
            throw new Exception("successor metadata did not recover PREPARED as COMMITTED_RECOVERED");
        RequireNoAuthority(recoveredTx);

        // PREPARED + no active metadata is closed epistemically without claiming commit.
        var absentSucc = "mcpsess-" + Guid.NewGuid().ToString("N");
        var absentGen = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "absent", absentSucc, MetadataPath(workspace, "absent"), default);
        await txService.PrepareAsync(workspace, "absent", absentSucc, MetadataPath(workspace, "absent"), absentGen.Receipt, false, default);
        var absent = await txService.ReconcileBeforePrepareAsync(workspace, "absent", MetadataPath(workspace, "absent"), default);
        if (absent.Status != "PRIOR_PREPARED_CLOSED_METADATA_ABSENT" || ReadTx(workspace, "absent").State != "CLOSED_METADATA_ABSENT")
            throw new Exception("metadata-absent PREPARED transaction was not closed without commit claim");

        // Forged/mismatched active metadata must fail closed.
        WriteOwner(workspace, "inconsistent", Owner("inconsistent", "mcpsess-inconsistent-prior"));
        var inconsistentSucc = "mcpsess-" + Guid.NewGuid().ToString("N");
        var inconsistentGen = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "inconsistent", inconsistentSucc, MetadataPath(workspace, "inconsistent"), default);
        await txService.PrepareAsync(workspace, "inconsistent", inconsistentSucc, MetadataPath(workspace, "inconsistent"), inconsistentGen.Receipt, false, default);
        WriteOwner(workspace, "inconsistent", Owner("inconsistent", "mcpsess-unrelated-active"));
        var inconsistentRefused = false;
        try { _ = await txService.ReconcileBeforePrepareAsync(workspace, "inconsistent", MetadataPath(workspace, "inconsistent"), default); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_OWNER_GENERATION_TRANSACTION_INCONSISTENT", StringComparison.Ordinal)) { inconsistentRefused = true; }
        if (!inconsistentRefused) throw new Exception("inconsistent PREPARED transaction did not fail closed");

        // Tampered archive must fail before any retry generation can continue.
        WriteOwner(workspace, "hashbad", Owner("hashbad", "mcpsess-hashbad-prior"));
        var hashSucc = "mcpsess-" + Guid.NewGuid().ToString("N");
        var hashGen = await generation.PreservePriorBeforeSuccessorAsync(
            workspace, "hashbad", hashSucc, MetadataPath(workspace, "hashbad"), default);
        await txService.PrepareAsync(workspace, "hashbad", hashSucc, MetadataPath(workspace, "hashbad"), hashGen.Receipt, false, default);
        File.AppendAllText(hashGen.Receipt.ArchivePath!, "tamper", new UTF8Encoding(false));
        var hashRefused = false;
        try { _ = await txService.ReconcileBeforePrepareAsync(workspace, "hashbad", MetadataPath(workspace, "hashbad"), default); }
        catch (InvalidDataException ex) when (ex.Message.Contains("ARCHIVE_HASH_MISMATCH", StringComparison.Ordinal) || ex.Message.Contains("ARCHIVE_OVERSIZE", StringComparison.Ordinal)) { hashRefused = true; }
        if (!hashRefused) throw new Exception("tampered prior archive did not fail closed");

        // Busy owner must not mutate the committed transaction checkpoint.
        var busyHeld = await ownerService.AcquireAsync(workspace, "busy", "v05110-owner-a", default, 500);
        var busyTxSha = ShaFile(TxPath(workspace, "busy"));
        var busyRefused = false;
        try { _ = await ownerService.AcquireAsync(workspace, "busy", "v05110-owner-b", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { busyRefused = true; }
        if (!busyRefused || ShaFile(TxPath(workspace, "busy")) != busyTxSha)
            throw new Exception("busy owner contention mutated v0.51.10 transaction state");
        await ownerService.ReleaseUnstartedAsync(busyHeld, true, "qualification", default);

        Console.WriteLine(
            "V05110_GENERATION_TRANSACTION_RUNTIME_PASS committed=true abandoned=true archiveReuse=true prePreparedDedupe=true committedRecovered=true " +
            "metadataAbsentClosed=true inconsistentRefused=true hashMismatchRefused=true busyNoMutation=true authority=false secrets=false historicalScan=false");
    }
}
