using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

    private static string BindingPath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", app, "owner-lease-binding-v05111.json");

    private static LocalAppMcpOwnerLeaseBindingTransactionV05111 ReadBinding(string workspace, string app)
        => JsonSerializer.Deserialize<LocalAppMcpOwnerLeaseBindingTransactionV05111>(File.ReadAllText(BindingPath(workspace, app)))
           ?? throw new Exception("binding transaction parse failed");

    private static LocalAppReadLeasePreviewV048 Preview(string workspace, string app, string suffix, int ttl = 300)
    {
        var service = new LocalAppReadLeaseV048Service();
        return service.Preview(
            workspace,
            app,
            new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                "lease-request-v05111-" + suffix,
                app,
                new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
                4096,
                65536,
                4,
                ttl),
            default);
    }

    private static async Task InitIndex(string workspace, string app)
    {
        var lifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();
        _ = await lifecycle.ReconcileIndexAsync(workspace, app, default);
    }

    private static void RequireNoAuthority(LocalAppMcpOwnerLeaseBindingTransactionV05111 tx)
    {
        if (tx.CanonicalHistoricalScanPerformed || tx.CanonicalLeaseMutationPerformed || tx.ActiveIndexMutationPerformed ||
            tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted ||
            tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new Exception($"binding transaction widened authority/non-effects: {tx.State}");
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v05111-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        foreach (var app in new[] { "normal", "abandon", "orphan", "recovered", "forged", "busy" })
        {
            MakeApp(workspace, app);
            await InitIndex(workspace, app);
        }

        var ownerService = new LocalAppMcpSessionOwnershipV0517Service();
        var bindingService = new LocalAppMcpOwnerLeaseBindingV05111Service();
        var preparedLeaseService = new LocalAppPreparedIndexedLeaseV05111Service();
        var indexedLifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();

        // Normal: exact LeaseId exists in PREPARED before canonical state, then LEASE_CREATED, then OWNER_BOUND.
        var normalOwner = await ownerService.AcquireAsync(workspace, "normal", "v05111-normal", default, 500);
        var normalPrepared = await bindingService.PrepareBindingAsync(normalOwner, default);
        RequireNoAuthority(normalPrepared.Transaction);
        if (normalPrepared.Transaction.State != "PREPARED_BINDING") throw new Exception("normal PREPARED_BINDING missing");
        var normalPreparedPath = LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(
            workspace, "normal", normalPrepared.Transaction.PreparedLeaseId, false);
        if (File.Exists(normalPreparedPath)) throw new Exception("prepared LeaseId already had canonical state");

        var normalCreated = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, "normal", Preview(workspace, "normal", "normal"), normalPrepared.Transaction.PreparedLeaseId, false, default);
        if (normalCreated.Grant.LeaseId != normalPrepared.Transaction.PreparedLeaseId || !File.Exists(normalCreated.Receipt.StatePath))
            throw new Exception("prepared LeaseId was not materialized exactly");
        var normalLeaseCreated = await bindingService.RecordLeaseCreatedAsync(normalOwner, normalCreated, default);
        RequireNoAuthority(normalLeaseCreated.Transaction);
        if (normalLeaseCreated.Transaction.State != "LEASE_CREATED" || normalLeaseCreated.Transaction.LeaseStateSha256AtCreation != normalCreated.Receipt.StateSha256)
            throw new Exception("LEASE_CREATED evidence mismatch");
        await ownerService.BindExactLeaseAsync(normalOwner, normalCreated.Grant.LeaseId, default);
        var normalBound = await bindingService.CommitOwnerBoundAsync(normalOwner, default);
        RequireNoAuthority(normalBound.Transaction);
        if (normalBound.Transaction.State != "OWNER_BOUND" || string.IsNullOrWhiteSpace(normalBound.Transaction.OwnerMetadataSha256))
            throw new Exception("OWNER_BOUND exact metadata proof missing");
        await ownerService.ReleaseUnstartedAsync(normalOwner, true, "qualification normal cleanup", default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "normal", normalCreated.Grant.LeaseId, default);

        // PREPARED and no state: next owner acquisition closes it ABANDONED_BEFORE_LEASE before successor metadata overwrite.
        var abandonOwner = await ownerService.AcquireAsync(workspace, "abandon", "v05111-abandon-a", default, 500);
        var abandonPrepared = await bindingService.PrepareBindingAsync(abandonOwner, default);
        if (File.Exists(LocalAppPreparedIndexedLeaseV05111Service.ResolveExactStatePath(workspace, "abandon", abandonPrepared.Transaction.PreparedLeaseId, false)))
            throw new Exception("abandon fixture unexpectedly materialized lease");
        await ownerService.ReleaseUnstartedAsync(abandonOwner, true, "qualification crash-before-lease", default);
        var abandonOwner2 = await ownerService.AcquireAsync(workspace, "abandon", "v05111-abandon-b", default, 500);
        var abandonRecovered = ReadBinding(workspace, "abandon");
        RequireNoAuthority(abandonRecovered);
        if (abandonRecovered.State != "ABANDONED_BEFORE_LEASE") throw new Exception("PREPARED absence was not closed explicitly");
        await ownerService.ReleaseUnstartedAsync(abandonOwner2, true, "qualification abandon cleanup", default);

        // Crash after canonical state materializes but before transaction advances: exact prepared id recovers a live orphan and blocks successor start.
        var orphanOwner = await ownerService.AcquireAsync(workspace, "orphan", "v05111-orphan-a", default, 500);
        var orphanPrepared = await bindingService.PrepareBindingAsync(orphanOwner, default);
        var orphanCreated = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, "orphan", Preview(workspace, "orphan", "orphan"), orphanPrepared.Transaction.PreparedLeaseId, false, default);
        await ownerService.ReleaseUnstartedAsync(orphanOwner, true, "qualification simulated crash after canonical create", default);
        var orphanBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "orphan", "v05111-orphan-b", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_OWNER_LEASE_BINDING_LIVE_ORPHAN_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal))
        { orphanBlocked = true; }
        if (!orphanBlocked) throw new Exception("live incomplete binding did not block successor owner generation");
        var orphanTx = ReadBinding(workspace, "orphan");
        RequireNoAuthority(orphanTx);
        var orphanState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "orphan", orphanCreated.Grant.LeaseId);
        if (orphanTx.State != "LIVE_ORPHAN_AFTER_LEASE_CREATE" || orphanState.Revoked || orphanState.ExpiresAt <= DateTimeOffset.Now)
            throw new Exception("exact live orphan was not preserved without revoke");
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "orphan", orphanCreated.Grant.LeaseId, default);
        var orphanOwner3 = await ownerService.AcquireAsync(workspace, "orphan", "v05111-orphan-c", default, 500);
        var orphanTerminal = ReadBinding(workspace, "orphan");
        if (orphanTerminal.State != "LEASE_REVOKED_AFTER_CREATE") throw new Exception("revoked orphan did not reconcile terminally");
        await ownerService.ReleaseUnstartedAsync(orphanOwner3, true, "qualification orphan cleanup", default);

        // Crash after exact owner metadata bind but before OWNER_BOUND tx update: recover exact owner+lease relation.
        var recoveredOwner = await ownerService.AcquireAsync(workspace, "recovered", "v05111-recovered-a", default, 500);
        var recoveredPrepared = await bindingService.PrepareBindingAsync(recoveredOwner, default);
        var recoveredCreated = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, "recovered", Preview(workspace, "recovered", "recovered"), recoveredPrepared.Transaction.PreparedLeaseId, false, default);
        _ = await bindingService.RecordLeaseCreatedAsync(recoveredOwner, recoveredCreated, default);
        await ownerService.BindExactLeaseAsync(recoveredOwner, recoveredCreated.Grant.LeaseId, default);
        await ownerService.ReleaseUnstartedAsync(recoveredOwner, true, "qualification simulated crash after owner metadata bind", default);
        var recoveredOwner2 = await ownerService.AcquireAsync(workspace, "recovered", "v05111-recovered-b", default, 500);
        var recoveredTx = ReadBinding(workspace, "recovered");
        RequireNoAuthority(recoveredTx);
        if (recoveredTx.State != "OWNER_BOUND_RECOVERED" || recoveredTx.PreparedLeaseId != recoveredCreated.Grant.LeaseId)
            throw new Exception("OWNER_BOUND_RECOVERED exact relation missing");
        await ownerService.ReleaseUnstartedAsync(recoveredOwner2, true, "qualification recovered cleanup", default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "recovered", recoveredCreated.Grant.LeaseId, default);

        // Forged owner-session relation refuses before a successor owner generation is written.
        var forgedOwner = await ownerService.AcquireAsync(workspace, "forged", "v05111-forged-a", default, 500);
        var forgedPrepared = await bindingService.PrepareBindingAsync(forgedOwner, default);
        var forgedCreated = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, "forged", Preview(workspace, "forged", "forged"), forgedPrepared.Transaction.PreparedLeaseId, false, default);
        _ = await bindingService.RecordLeaseCreatedAsync(forgedOwner, forgedCreated, default);
        await ownerService.ReleaseUnstartedAsync(forgedOwner, true, "qualification forged fixture", default);
        var forgedTx = ReadBinding(workspace, "forged") with { OwnerSessionId = "mcpsess-forgedmismatch" };
        File.WriteAllText(BindingPath(workspace, "forged"), JsonSerializer.Serialize(forgedTx, JsonOptions), new UTF8Encoding(false));
        var forgedRefused = false;
        try { _ = await ownerService.AcquireAsync(workspace, "forged", "v05111-forged-b", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_OWNER_LEASE_BINDING_TRANSACTION_INCONSISTENT", StringComparison.Ordinal))
        { forgedRefused = true; }
        if (!forgedRefused) throw new Exception("forged owner SessionId did not fail closed");
        var forgedState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "forged", forgedCreated.Grant.LeaseId);
        if (forgedState.Revoked) throw new Exception("forged reconciliation mutated canonical lease");

        // Busy app domain refuses before v0.51.11 transaction reconciliation/mutation by the second process/request.
        var busyOwner = await ownerService.AcquireAsync(workspace, "busy", "v05111-busy-a", default, 500);
        var busyPath = BindingPath(workspace, "busy");
        var busyExistedBefore = File.Exists(busyPath);
        var busyRefused = false;
        try { _ = await ownerService.AcquireAsync(workspace, "busy", "v05111-busy-b", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal))
        { busyRefused = true; }
        if (!busyRefused || File.Exists(busyPath) != busyExistedBefore)
            throw new Exception("busy owner contention mutated v0.51.11 binding state");
        await ownerService.ReleaseUnstartedAsync(busyOwner, true, "qualification busy cleanup", default);

        Console.WriteLine(
            "V05111_OWNER_LEASE_BINDING_RUNTIME_PASS normal=true preparedExact=true leaseCreated=true ownerBound=true " +
            "abandonedBeforeLease=true exactCrashRecovery=true liveOrphanBlocked=true noAutoRevoke=true revokedTerminal=true " +
            "ownerBoundRecovered=true forgedRefused=true busyNoMutation=true historicalScan=false authority=false secrets=false");
    }
}
