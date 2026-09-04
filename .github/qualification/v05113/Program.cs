using System.Security.Cryptography;
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

    private static string ShutdownPath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", app, "shutdown-v05113.json");

    private static LocalAppMcpShutdownTransactionV05113 ReadShutdown(string workspace, string app)
        => JsonSerializer.Deserialize<LocalAppMcpShutdownTransactionV05113>(File.ReadAllText(ShutdownPath(workspace, app)))
           ?? throw new Exception("shutdown transaction parse failed");

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static LocalAppReadLeasePreviewV048 Preview(string workspace, string app, string suffix, int ttl = 300)
    {
        var service = new LocalAppReadLeaseV048Service();
        return service.Preview(
            workspace,
            app,
            new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                "lease-request-v05113-" + suffix,
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

    private static void RequireNoAuthority(LocalAppMcpShutdownTransactionV05113 tx)
    {
        if (tx.CanonicalHistoricalScanPerformed || tx.TransactionCanonicalLeaseMutationPerformed || tx.TransactionActiveIndexMutationPerformed ||
            tx.ShutdownTransactionGrantedAuthority || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted ||
            tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new Exception($"shutdown transaction widened authority/non-effects: {tx.State}");
    }

    private sealed record ReadyFixture(
        LocalAppHeldMcpSessionOwnershipV0517 Owner,
        LocalAppIndexedLeaseCreateResultV0515 Created,
        LocalAppMcpReadAdapterV049Service Adapter,
        LocalAppMcpAdapterGrantV049 AdapterGrant,
        LocalAppMcpListenerReadinessTransactionV05112 ListenerReady);

    private static async Task<ReadyFixture> CreateReadyAsync(
        string workspace,
        string app,
        string suffix,
        LocalAppMcpSessionOwnershipV0517Service ownerService,
        LocalAppMcpOwnerLeaseBindingV05111Service bindingService,
        LocalAppPreparedIndexedLeaseV05111Service preparedLeaseService,
        LocalAppMcpListenerReadinessV05112Service listenerService)
    {
        var owner = await ownerService.AcquireAsync(workspace, app, "v05113-" + suffix, default, 500);
        var prepared = await bindingService.PrepareBindingAsync(owner, default);
        var created = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, app, Preview(workspace, app, suffix), prepared.Transaction.PreparedLeaseId, false, default);
        _ = await bindingService.RecordLeaseCreatedAsync(owner, created, default);
        await ownerService.BindExactLeaseAsync(owner, created.Grant.LeaseId, default);
        var bound = await bindingService.CommitOwnerBoundAsync(owner, default);
        var listenerPrepared = await listenerService.PrepareAsync(owner, bound.Transaction, default);
        if (listenerPrepared.Transaction.State != "PREPARED_LISTENER_START") throw new Exception("ready fixture listener prepare failed");

        var adapter = new LocalAppMcpReadAdapterV049Service();
        var grantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
        var adapterPreview = adapter.PreviewFromGrantJson(workspace, app, grantJson, default);
        var adapterGrant = await adapter.StartAsync(workspace, app, adapterPreview, grantJson, default);
        _ = await listenerService.RecordListenerStartedAsync(owner, adapterGrant, adapter.IsActiveFor(app), default);
        var ready = await listenerService.CommitReadyAsync(owner, adapterGrant, adapter.IsActiveFor(app), default);
        await ownerService.MarkListenerReadyAsync(owner, adapterGrant, default);
        if (ready.Transaction.State != "LISTENER_READY" || !adapter.IsActiveFor(app)) throw new Exception("ready fixture LISTENER_READY failed");
        return new ReadyFixture(owner, created, adapter, adapterGrant, ready.Transaction);
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v05113-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        foreach (var app in new[] { "normal", "preparedcrash", "stoppedcrash", "releasedcrash", "terminal", "forged", "sibling" })
        {
            MakeApp(workspace, app);
            await InitIndex(workspace, app);
        }

        var ownerService = new LocalAppMcpSessionOwnershipV0517Service();
        var bindingService = new LocalAppMcpOwnerLeaseBindingV05111Service();
        var preparedLeaseService = new LocalAppPreparedIndexedLeaseV05111Service();
        var listenerService = new LocalAppMcpListenerReadinessV05112Service();
        var shutdownService = new LocalAppMcpShutdownTransactionV05113Service();
        var indexedLifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();

        // Normal: every reverse boundary is separately materialized.
        var normal = await CreateReadyAsync(workspace, "normal", "normal", ownerService, bindingService, preparedLeaseService, listenerService);
        var normalPrepared = await shutdownService.PrepareAsync(normal.Owner, default);
        RequireNoAuthority(normalPrepared.Transaction);
        if (normalPrepared.Transaction.State != "SHUTDOWN_PREPARED" || !normalPrepared.Transaction.StopRequested || normalPrepared.Transaction.ListenerObservedInactive)
            throw new Exception("normal SHUTDOWN_PREPARED overclaimed stop");
        var normalStop = await normal.Adapter.StopAsync(workspace, default);
        var normalStopped = await shutdownService.RecordListenerStoppedAsync(
            normal.Owner, normalStop.Receipt, normalStop.ReceiptPath, !normal.Adapter.IsActiveFor("normal"), default);
        RequireNoAuthority(normalStopped.Transaction);
        if (normalStopped.Transaction.State != "LISTENER_STOPPED" || !normalStopped.Transaction.ListenerObservedInactive || normalStopped.Transaction.OwnerReleaseObserved)
            throw new Exception("normal LISTENER_STOPPED boundary failed");
        var normalRelease = await ownerService.ReleaseAfterListenerStoppedAsync(normal.Owner, true, default);
        var normalOwnerReleased = await shutdownService.RecordOwnerReleasedAsync(
            workspace, "normal", normal.Owner.SessionId, normal.Created.Grant.LeaseId, normalRelease.ReceiptPath, default);
        RequireNoAuthority(normalOwnerReleased.Transaction);
        if (normalOwnerReleased.Transaction.State != "OWNER_RELEASED" || !normalOwnerReleased.Transaction.OwnerReleaseObserved || normalOwnerReleased.Transaction.ExactLeaseTerminalObserved)
            throw new Exception("normal OWNER_RELEASED boundary failed");
        var normalRevoked = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "normal", normal.Created.Grant.LeaseId, default);
        var normalTerminal = await shutdownService.RecordLeaseTerminalAsync(
            workspace, "normal", normal.Owner.SessionId, normal.Created.Grant.LeaseId,
            normalRevoked.ExactReceiptPath, normalRevoked.ExactReceipt.SiblingLeasesRevoked, default);
        RequireNoAuthority(normalTerminal.Transaction);
        if (normalTerminal.Transaction.State != "LEASE_REVOKED" || !normalTerminal.Transaction.ExactLeaseTerminalObserved || normalTerminal.Transaction.SiblingLeasesRevoked)
            throw new Exception("normal LEASE_REVOKED boundary failed");
        var normalDone = await shutdownService.CommitCompletedAsync(
            workspace, "normal", normal.Owner.SessionId, normal.Created.Grant.LeaseId, default);
        RequireNoAuthority(normalDone.Transaction);
        if (normalDone.Transaction.State != "SHUTDOWN_COMPLETED") throw new Exception("normal shutdown did not commit");

        // SHUTDOWN_PREPARED while listener/owner are still materially active: second owner is busy and tx is unchanged.
        var preparedCrash = await CreateReadyAsync(workspace, "preparedcrash", "preparedcrash", ownerService, bindingService, preparedLeaseService, listenerService);
        _ = await shutdownService.PrepareAsync(preparedCrash.Owner, default);
        var preparedHash = HashFile(ShutdownPath(workspace, "preparedcrash"));
        var preparedBusy = false;
        try { _ = await ownerService.AcquireAsync(workspace, "preparedcrash", "v05113-prepared-busy", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { preparedBusy = true; }
        if (!preparedBusy || preparedHash != HashFile(ShutdownPath(workspace, "preparedcrash")))
            throw new Exception("SHUTDOWN_PREPARED busy contention mutated shutdown transaction");
        var preparedStop = await preparedCrash.Adapter.StopAsync(workspace, default);
        if (!preparedStop.Receipt.ListenerStopped) throw new Exception("prepared crash stop cleanup failed");
        await ownerService.ReleaseUnstartedAsync(preparedCrash.Owner, true, "qualification simulated process loss after SHUTDOWN_PREPARED", default);
        var preparedBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "preparedcrash", "v05113-prepared-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SHUTDOWN_LIVE_LEASE_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal)) { preparedBlocked = true; }
        if (!preparedBlocked) throw new Exception("SHUTDOWN_PREPARED crash did not block live-lease successor");
        var preparedRecovered = ReadShutdown(workspace, "preparedcrash");
        RequireNoAuthority(preparedRecovered);
        if (preparedRecovered.State != "OWNER_RELEASED_LEASE_LIVE") throw new Exception("prepared crash recovery classification wrong");
        var preparedState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "preparedcrash", preparedCrash.Created.Grant.LeaseId);
        if (preparedState.Revoked) throw new Exception("prepared crash recovery auto-revoked lease");

        // Crash after listener stop but before owner release: while held, successor is still busy; after simulated process loss live lease remains explicit closure-needed.
        var stoppedCrash = await CreateReadyAsync(workspace, "stoppedcrash", "stoppedcrash", ownerService, bindingService, preparedLeaseService, listenerService);
        _ = await shutdownService.PrepareAsync(stoppedCrash.Owner, default);
        var stoppedStop = await stoppedCrash.Adapter.StopAsync(workspace, default);
        var stoppedTx = await shutdownService.RecordListenerStoppedAsync(
            stoppedCrash.Owner, stoppedStop.Receipt, stoppedStop.ReceiptPath, !stoppedCrash.Adapter.IsActiveFor("stoppedcrash"), default);
        if (stoppedTx.Transaction.State != "LISTENER_STOPPED") throw new Exception("stopped crash fixture missing LISTENER_STOPPED");
        var stoppedBusy = false;
        try { _ = await ownerService.AcquireAsync(workspace, "stoppedcrash", "v05113-stopped-busy", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { stoppedBusy = true; }
        if (!stoppedBusy) throw new Exception("LISTENER_STOPPED owner-held state did not serialize domain");
        await ownerService.ReleaseUnstartedAsync(stoppedCrash.Owner, true, "qualification simulated process loss after LISTENER_STOPPED", default);
        var stoppedBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "stoppedcrash", "v05113-stopped-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SHUTDOWN_LIVE_LEASE_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal)) { stoppedBlocked = true; }
        if (!stoppedBlocked || ReadShutdown(workspace, "stoppedcrash").State != "OWNER_RELEASED_LEASE_LIVE")
            throw new Exception("LISTENER_STOPPED crash did not recover to explicit live-lease closure state");

        // Crash after owner release before exact revoke: successor is blocked; no automatic revoke.
        var releasedCrash = await CreateReadyAsync(workspace, "releasedcrash", "releasedcrash", ownerService, bindingService, preparedLeaseService, listenerService);
        _ = await shutdownService.PrepareAsync(releasedCrash.Owner, default);
        var releasedStop = await releasedCrash.Adapter.StopAsync(workspace, default);
        _ = await shutdownService.RecordListenerStoppedAsync(releasedCrash.Owner, releasedStop.Receipt, releasedStop.ReceiptPath, true, default);
        var releasedOwnerReceipt = await ownerService.ReleaseAfterListenerStoppedAsync(releasedCrash.Owner, true, default);
        _ = await shutdownService.RecordOwnerReleasedAsync(
            workspace, "releasedcrash", releasedCrash.Owner.SessionId, releasedCrash.Created.Grant.LeaseId, releasedOwnerReceipt.ReceiptPath, default);
        var releasedBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "releasedcrash", "v05113-released-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SHUTDOWN_LIVE_LEASE_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal)) { releasedBlocked = true; }
        if (!releasedBlocked || ReadShutdown(workspace, "releasedcrash").State != "OWNER_RELEASED_LEASE_LIVE")
            throw new Exception("OWNER_RELEASED live lease did not block successor");
        var releasedState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "releasedcrash", releasedCrash.Created.Grant.LeaseId);
        if (releasedState.Revoked) throw new Exception("OWNER_RELEASED recovery auto-revoked exact lease");

        // Exact lease already terminal before shutdown transaction could record it: recovery closes evidence without canonical rewrite and permits successor.
        var terminal = await CreateReadyAsync(workspace, "terminal", "terminal", ownerService, bindingService, preparedLeaseService, listenerService);
        _ = await shutdownService.PrepareAsync(terminal.Owner, default);
        var terminalStop = await terminal.Adapter.StopAsync(workspace, default);
        _ = await shutdownService.RecordListenerStoppedAsync(terminal.Owner, terminalStop.Receipt, terminalStop.ReceiptPath, true, default);
        var terminalRelease = await ownerService.ReleaseAfterListenerStoppedAsync(terminal.Owner, true, default);
        _ = await shutdownService.RecordOwnerReleasedAsync(workspace, "terminal", terminal.Owner.SessionId, terminal.Created.Grant.LeaseId, terminalRelease.ReceiptPath, default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "terminal", terminal.Created.Grant.LeaseId, default);
        var terminalOwner2 = await ownerService.AcquireAsync(workspace, "terminal", "v05113-terminal-successor", default, 500);
        var terminalRecovered = ReadShutdown(workspace, "terminal");
        RequireNoAuthority(terminalRecovered);
        if (terminalRecovered.State != "LEASE_ALREADY_TERMINAL") throw new Exception("already-terminal lease did not reconcile without rewrite");
        await ownerService.ReleaseUnstartedAsync(terminalOwner2, true, "qualification terminal cleanup", default);

        // Forged shutdown SessionId fails closed before successor generation; canonical lease remains live.
        var forged = await CreateReadyAsync(workspace, "forged", "forged", ownerService, bindingService, preparedLeaseService, listenerService);
        _ = await shutdownService.PrepareAsync(forged.Owner, default);
        var forgedStop = await forged.Adapter.StopAsync(workspace, default);
        if (!forgedStop.Receipt.ListenerStopped) throw new Exception("forged fixture stop failed");
        await ownerService.ReleaseUnstartedAsync(forged.Owner, true, "qualification forged fixture", default);
        var forgedTx = ReadShutdown(workspace, "forged") with { OwnerSessionId = "mcpsess-forgedmismatch" };
        File.WriteAllText(ShutdownPath(workspace, "forged"), JsonSerializer.Serialize(forgedTx, JsonOptions), new UTF8Encoding(false));
        var forgedRefused = false;
        try { _ = await ownerService.AcquireAsync(workspace, "forged", "v05113-forged-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SHUTDOWN_TRANSACTION_INCONSISTENT", StringComparison.Ordinal)) { forgedRefused = true; }
        if (!forgedRefused) throw new Exception("forged shutdown identity did not fail closed");
        var forgedState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "forged", forged.Created.Grant.LeaseId);
        if (forgedState.Revoked) throw new Exception("forged shutdown reconciliation mutated canonical lease");

        // Exact shutdown must preserve a sibling lease.
        var sibling = await CreateReadyAsync(workspace, "sibling", "sibling-primary", ownerService, bindingService, preparedLeaseService, listenerService);
        var siblingLease = await indexedLifecycle.CreateIndexedAsync(
            workspace, "sibling", Preview(workspace, "sibling", "sibling-secondary"), false, default);
        _ = await shutdownService.PrepareAsync(sibling.Owner, default);
        var siblingStop = await sibling.Adapter.StopAsync(workspace, default);
        _ = await shutdownService.RecordListenerStoppedAsync(sibling.Owner, siblingStop.Receipt, siblingStop.ReceiptPath, true, default);
        var siblingRelease = await ownerService.ReleaseAfterListenerStoppedAsync(sibling.Owner, true, default);
        _ = await shutdownService.RecordOwnerReleasedAsync(workspace, "sibling", sibling.Owner.SessionId, sibling.Created.Grant.LeaseId, siblingRelease.ReceiptPath, default);
        var siblingRevoked = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "sibling", sibling.Created.Grant.LeaseId, default);
        if (siblingRevoked.ExactReceipt.SiblingLeasesRevoked != 0) throw new Exception("exact revoke unexpectedly revoked sibling lease");
        _ = await shutdownService.RecordLeaseTerminalAsync(
            workspace, "sibling", sibling.Owner.SessionId, sibling.Created.Grant.LeaseId,
            siblingRevoked.ExactReceiptPath, siblingRevoked.ExactReceipt.SiblingLeasesRevoked, default);
        _ = await shutdownService.CommitCompletedAsync(workspace, "sibling", sibling.Owner.SessionId, sibling.Created.Grant.LeaseId, default);
        var siblingState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "sibling", siblingLease.Grant.LeaseId);
        if (siblingState.Revoked || siblingState.ExpiresAt <= DateTimeOffset.Now) throw new Exception("sibling lease was not preserved");

        // Explicit qualification cleanup of intentionally live crash fixtures; no recovery path did these revokes.
        foreach (var item in new[]
        {
            ("preparedcrash", preparedCrash.Created.Grant.LeaseId),
            ("stoppedcrash", stoppedCrash.Created.Grant.LeaseId),
            ("releasedcrash", releasedCrash.Created.Grant.LeaseId),
            ("forged", forged.Created.Grant.LeaseId),
            ("sibling", siblingLease.Grant.LeaseId)
        })
        {
            _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, item.Item1, item.Item2, default);
        }

        Console.WriteLine(
            "V05113_SHUTDOWN_RUNTIME_PASS normal=true shutdownPrepared=true listenerStopped=true ownerReleased=true " +
            "leaseRevoked=true shutdownCompleted=true preparedBusyNoMutation=true preparedCrashBlocked=true " +
            "stoppedOwnerHeld=true stoppedCrashBlocked=true releasedCrashBlocked=true noAutoRevoke=true " +
            "terminalRecovery=true forgedRefused=true siblingPreserved=true historicalScan=false authority=false secrets=false");
    }
}
