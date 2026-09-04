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

    private static string ListenerPath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", app, "listener-readiness-v05112.json");

    private static LocalAppMcpListenerReadinessTransactionV05112 ReadListener(string workspace, string app)
        => JsonSerializer.Deserialize<LocalAppMcpListenerReadinessTransactionV05112>(File.ReadAllText(ListenerPath(workspace, app)))
           ?? throw new Exception("listener-readiness transaction parse failed");

    private static LocalAppReadLeasePreviewV048 Preview(string workspace, string app, string suffix, int ttl = 300)
    {
        var service = new LocalAppReadLeaseV048Service();
        return service.Preview(
            workspace,
            app,
            new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                "lease-request-v05112-" + suffix,
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

    private static void RequireNoAuthority(LocalAppMcpListenerReadinessTransactionV05112 tx)
    {
        if (tx.CanonicalHistoricalScanPerformed || tx.CanonicalLeaseMutationPerformed || tx.ActiveIndexMutationPerformed ||
            tx.LeaseAuthorityGranted || tx.ReadAuthorityGranted || tx.RevokeAuthorityGranted || tx.ResumeAuthorityGranted ||
            tx.BearerPlaintextDisclosed || tx.BearerHashDisclosed || tx.EndpointSecretDisclosed)
            throw new Exception($"listener transaction widened authority/non-effects: {tx.State}");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record BoundFixture(
        LocalAppHeldMcpSessionOwnershipV0517 Owner,
        LocalAppIndexedLeaseCreateResultV0515 Created,
        LocalAppMcpOwnerLeaseBindingTransactionV05111 OwnerBound);

    private static async Task<BoundFixture> CreateOwnerBoundAsync(
        string workspace,
        string app,
        string suffix,
        LocalAppMcpSessionOwnershipV0517Service ownerService,
        LocalAppMcpOwnerLeaseBindingV05111Service bindingService,
        LocalAppPreparedIndexedLeaseV05111Service preparedLeaseService)
    {
        var owner = await ownerService.AcquireAsync(workspace, app, "v05112-" + suffix, default, 500);
        var prepared = await bindingService.PrepareBindingAsync(owner, default);
        var created = await preparedLeaseService.CreatePreparedIndexedAsync(
            workspace, app, Preview(workspace, app, suffix), prepared.Transaction.PreparedLeaseId, false, default);
        _ = await bindingService.RecordLeaseCreatedAsync(owner, created, default);
        await ownerService.BindExactLeaseAsync(owner, created.Grant.LeaseId, default);
        var bound = await bindingService.CommitOwnerBoundAsync(owner, default);
        if (bound.Transaction.State != "OWNER_BOUND" || bound.Transaction.PreparedLeaseId != created.Grant.LeaseId)
            throw new Exception("fixture OWNER_BOUND failed");
        return new BoundFixture(owner, created, bound.Transaction);
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v05112-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        foreach (var app in new[] { "normal", "preparedcrash", "startedcrash", "terminal", "busy" })
        {
            MakeApp(workspace, app);
            await InitIndex(workspace, app);
        }

        var ownerService = new LocalAppMcpSessionOwnershipV0517Service();
        var bindingService = new LocalAppMcpOwnerLeaseBindingV05111Service();
        var preparedLeaseService = new LocalAppPreparedIndexedLeaseV05111Service();
        var listenerService = new LocalAppMcpListenerReadinessV05112Service();
        var indexedLifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();

        // Normal real listener: OWNER_BOUND -> PREPARED -> actual v0.49 StartAsync -> STARTED -> READY.
        var normal = await CreateOwnerBoundAsync(workspace, "normal", "normal", ownerService, bindingService, preparedLeaseService);
        var normalPrepared = await listenerService.PrepareAsync(normal.Owner, normal.OwnerBound, default);
        RequireNoAuthority(normalPrepared.Transaction);
        if (normalPrepared.Transaction.State != "PREPARED_LISTENER_START" || normalPrepared.Transaction.ListenerStartAttempted || normalPrepared.Transaction.ListenerObservedActive)
            throw new Exception("normal PREPARED_LISTENER_START overclaimed listener materialization");

        var normalAdapter = new LocalAppMcpReadAdapterV049Service();
        var normalGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(normal.Created.Grant);
        var normalAdapterPreview = normalAdapter.PreviewFromGrantJson(workspace, "normal", normalGrantJson, default);
        var normalAdapterGrant = await normalAdapter.StartAsync(workspace, "normal", normalAdapterPreview, normalGrantJson, default);
        if (!normalAdapter.IsActiveFor("normal")) throw new Exception("actual v0.49 listener did not become active");
        var normalStarted = await listenerService.RecordListenerStartedAsync(normal.Owner, normalAdapterGrant, normalAdapter.IsActiveFor("normal"), default);
        RequireNoAuthority(normalStarted.Transaction);
        if (normalStarted.Transaction.State != "LISTENER_STARTED" || !normalStarted.Transaction.ListenerStartAttempted || !normalStarted.Transaction.ListenerObservedActive ||
            normalStarted.Transaction.LoopbackHost != "127.0.0.1" || normalStarted.Transaction.LoopbackPort is null)
            throw new Exception("LISTENER_STARTED exact loopback evidence missing");
        var normalReady = await listenerService.CommitReadyAsync(normal.Owner, normalAdapterGrant, normalAdapter.IsActiveFor("normal"), default);
        RequireNoAuthority(normalReady.Transaction);
        if (normalReady.Transaction.State != "LISTENER_READY" || !normalReady.Transaction.ListenerObservedActive ||
            normalReady.Transaction.ListenerTransactionId != normalPrepared.Transaction.ListenerTransactionId)
            throw new Exception("LISTENER_READY continuity missing");
        await ownerService.MarkListenerReadyAsync(normal.Owner, normalAdapterGrant, default);
        var stop = await normalAdapter.StopAsync(workspace, default);
        if (!stop.Receipt.ListenerStopped || normalAdapter.IsActiveFor("normal")) throw new Exception("normal listener cleanup failed");
        _ = await ownerService.ReleaseAfterListenerStoppedAsync(normal.Owner, true, default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "normal", normal.Created.Grant.LeaseId, default);

        // Crash after PREPARED before StartAsync: reacquiring owner.lock must block on exact live bound lease.
        var preparedCrash = await CreateOwnerBoundAsync(workspace, "preparedcrash", "preparedcrash", ownerService, bindingService, preparedLeaseService);
        var preparedCrashTx = await listenerService.PrepareAsync(preparedCrash.Owner, preparedCrash.OwnerBound, default);
        RequireNoAuthority(preparedCrashTx.Transaction);
        await ownerService.ReleaseUnstartedAsync(preparedCrash.Owner, true, "qualification simulated crash after PREPARED_LISTENER_START", default);
        var preparedBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "preparedcrash", "v05112-preparedcrash-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_LISTENER_READINESS_LIVE_BOUND_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal))
        { preparedBlocked = true; }
        if (!preparedBlocked) throw new Exception("PREPARED crash did not block successor owner generation");
        var preparedRecovered = ReadListener(workspace, "preparedcrash");
        RequireNoAuthority(preparedRecovered);
        if (preparedRecovered.State != "LIVE_BOUND_NO_LISTENER" || preparedRecovered.ListenerObservedActive)
            throw new Exception("PREPARED crash did not become LIVE_BOUND_NO_LISTENER");
        var preparedState = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "preparedcrash", preparedCrash.Created.Grant.LeaseId);
        if (preparedState.Revoked) throw new Exception("PREPARED recovery auto-revoked canonical lease");

        // Crash after actual StartAsync + LISTENER_STARTED but before LISTENER_READY: stale start evidence cannot resume authority.
        var startedCrash = await CreateOwnerBoundAsync(workspace, "startedcrash", "startedcrash", ownerService, bindingService, preparedLeaseService);
        _ = await listenerService.PrepareAsync(startedCrash.Owner, startedCrash.OwnerBound, default);
        var startedAdapter = new LocalAppMcpReadAdapterV049Service();
        var startedGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(startedCrash.Created.Grant);
        var startedPreview = startedAdapter.PreviewFromGrantJson(workspace, "startedcrash", startedGrantJson, default);
        var startedGrant = await startedAdapter.StartAsync(workspace, "startedcrash", startedPreview, startedGrantJson, default);
        _ = await listenerService.RecordListenerStartedAsync(startedCrash.Owner, startedGrant, startedAdapter.IsActiveFor("startedcrash"), default);
        var startedTxBefore = ReadListener(workspace, "startedcrash");
        if (startedTxBefore.State != "LISTENER_STARTED") throw new Exception("started crash fixture missing LISTENER_STARTED");
        var startedStop = await startedAdapter.StopAsync(workspace, default);
        if (!startedStop.Receipt.ListenerStopped) throw new Exception("started crash simulated process-stop failed");
        await ownerService.ReleaseUnstartedAsync(startedCrash.Owner, true, "qualification simulated crash after LISTENER_STARTED", default);
        var startedBlocked = false;
        try { _ = await ownerService.AcquireAsync(workspace, "startedcrash", "v05112-startedcrash-successor", default, 500); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_LISTENER_READINESS_LIVE_BOUND_REQUIRES_EXPLICIT_CLOSURE", StringComparison.Ordinal))
        { startedBlocked = true; }
        if (!startedBlocked) throw new Exception("LISTENER_STARTED crash did not block successor owner generation");
        var startedRecovered = ReadListener(workspace, "startedcrash");
        RequireNoAuthority(startedRecovered);
        if (startedRecovered.State != "LIVE_BOUND_NO_LISTENER" || startedRecovered.ListenerObservedActive)
            throw new Exception("stale LISTENER_STARTED was treated as current listener readiness");
        var startedLease = LocalAppPreparedIndexedLeaseV05111Service.ReadExactCanonicalState(workspace, "startedcrash", startedCrash.Created.Grant.LeaseId);
        if (startedLease.Revoked) throw new Exception("LISTENER_STARTED recovery auto-revoked lease");

        // Terminal exact lease: recovery records terminal evidence and then successor owner may continue.
        var terminal = await CreateOwnerBoundAsync(workspace, "terminal", "terminal", ownerService, bindingService, preparedLeaseService);
        _ = await listenerService.PrepareAsync(terminal.Owner, terminal.OwnerBound, default);
        await ownerService.ReleaseUnstartedAsync(terminal.Owner, true, "qualification terminal fixture", default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "terminal", terminal.Created.Grant.LeaseId, default);
        var terminalOwner2 = await ownerService.AcquireAsync(workspace, "terminal", "v05112-terminal-successor", default, 500);
        var terminalTx = ReadListener(workspace, "terminal");
        RequireNoAuthority(terminalTx);
        if (terminalTx.State != "LEASE_REVOKED_BEFORE_LISTENER_RECOVERY")
            throw new Exception("revoked exact lease did not reconcile listener transaction terminally");
        await ownerService.ReleaseUnstartedAsync(terminalOwner2, true, "qualification terminal cleanup", default);

        // Busy domain: second owner cannot mutate/create listener transaction before getting owner.lock.
        var busyOwner = await ownerService.AcquireAsync(workspace, "busy", "v05112-busy-a", default, 500);
        var busyPath = ListenerPath(workspace, "busy");
        var busyExistedBefore = File.Exists(busyPath);
        var busyHashBefore = busyExistedBefore ? HashFile(busyPath) : null;
        var busyRefused = false;
        try { _ = await ownerService.AcquireAsync(workspace, "busy", "v05112-busy-b", default, 150); }
        catch (InvalidDataException ex) when (ex.Message.Contains("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal))
        { busyRefused = true; }
        var busyHashAfter = File.Exists(busyPath) ? HashFile(busyPath) : null;
        if (!busyRefused || File.Exists(busyPath) != busyExistedBefore || busyHashBefore != busyHashAfter)
            throw new Exception("busy owner contention mutated listener transaction");
        await ownerService.ReleaseUnstartedAsync(busyOwner, true, "qualification busy cleanup", default);

        // Explicit cleanup of live-bound crash fixtures only after proving they remained live/non-revoked.
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "preparedcrash", preparedCrash.Created.Grant.LeaseId, default);
        var preparedCleanup = await ownerService.AcquireAsync(workspace, "preparedcrash", "v05112-prepared-cleanup", default, 500);
        await ownerService.ReleaseUnstartedAsync(preparedCleanup, true, "qualification prepared cleanup", default);
        _ = await indexedLifecycle.RevokeExactIndexedAsync(workspace, "startedcrash", startedCrash.Created.Grant.LeaseId, default);
        var startedCleanup = await ownerService.AcquireAsync(workspace, "startedcrash", "v05112-started-cleanup", default, 500);
        await ownerService.ReleaseUnstartedAsync(startedCleanup, true, "qualification started cleanup", default);

        Console.WriteLine(
            "V05112_LISTENER_READINESS_RUNTIME_PASS normal=true actualLoopback=true preparedBeforeStart=true " +
            "listenerStarted=true listenerReady=true secondObservation=true preparedCrashBlocked=true " +
            "startedCrashBlocked=true staleStartedNotReady=true noAutoResume=true noAutoRevoke=true " +
            "terminalRecovery=true busyNoMutation=true historicalScan=false authority=false secrets=false");
    }
}
