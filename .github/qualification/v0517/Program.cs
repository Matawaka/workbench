using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private const string EndpointPathSecret = "TOPSECRET-ENDPOINT-PATH-V0517";

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string StateDir(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "read-leases", app);

    private static string OwnerMetadata(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "local-mcp-session-v0517", app, "owner-v0.51.7.json");

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

    private static Process StartChild(params string[] args)
    {
        var processPath = Environment.ProcessPath ?? throw new Exception("process path unavailable");
        var psi = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new Exception("child process failed to start");
    }

    private static async Task ChildHold(string workspace, string app, string leaseId)
    {
        var service = new LocalAppMcpSessionOwnershipV0517Service();
        var held = await service.AcquireAsync(workspace, app, "hostile-child-owner", default, 2000);
        await service.BindExactLeaseAsync(held, leaseId, default);
        var grant = new LocalAppMcpAdapterGrantV049(
            LocalAppMcpReadAdapterV049Service.GrantSchema,
            LocalAppMcpReadAdapterV049Service.Version,
            DateTimeOffset.Now, app, leaseId,
            "http://127.0.0.1:45678/mcp/" + EndpointPathSecret,
            "synthetic-not-persisted", DateTimeOffset.Now.AddMinutes(10),
            new[] { "read_local_app_chunk", "list_local_app_entries" }, true, false, false,
            "qualification-only listener observation");
        await service.MarkListenerReadyAsync(held, grant, default);
        Console.WriteLine("CHILD_READY");
        Console.Out.Flush();
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    public static async Task Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "hold")
        {
            await ChildHold(args[1], args[2], args[3]);
            return;
        }

        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v0517-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        MakeApp(workspace, "alpha");
        MakeApp(workspace, "beta");

        var legacy = new LocalAppReadLeaseV048Service();
        var request = new LocalAppReadLeaseRequestV048(
            LocalAppReadLeaseV048Service.RequestSchema,
            "v0517:owner:lease",
            "alpha",
            new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
            65536, 262144, 8, 900);
        var preview = legacy.Preview(workspace, "alpha", request, default);
        var created = await legacy.CreateAsync(workspace, "alpha", preview, false, default);
        var statePath = Path.Combine(StateDir(workspace, "alpha"), created.Grant.LeaseId + ".json");
        var canonicalBefore = Sha(statePath);
        var leaseCountBefore = Directory.GetFiles(StateDir(workspace, "alpha"), "lease-*.json").Length;

        using var child = StartChild("hold", workspace, "alpha", created.Grant.LeaseId);
        var readyTask = child.StandardOutput.ReadLineAsync();
        var ready = await readyTask.WaitAsync(TimeSpan.FromSeconds(8));
        if (ready != "CHILD_READY")
            throw new Exception("child owner did not become ready: " + ready + " stderr=" + await child.StandardError.ReadToEndAsync());

        var service = new LocalAppMcpSessionOwnershipV0517Service();
        var busy = false;
        try { _ = await service.AcquireAsync(workspace, "alpha", "second-process-attempt", default, 250); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { busy = true; }
        if (!busy) throw new Exception("same-app second owner did not fail closed");
        if (Directory.GetFiles(StateDir(workspace, "alpha"), "lease-*.json").Length != leaseCountBefore || Sha(statePath) != canonicalBefore)
            throw new Exception("busy ownership attempt mutated canonical lease state");

        var betaOwner = await service.AcquireAsync(workspace, "beta", "independent-app", default, 500);
        _ = await service.ReleaseUnstartedAsync(betaOwner, true, "beta independent check complete", default);

        var staleText = File.ReadAllText(OwnerMetadata(workspace, "alpha"), Encoding.UTF8);
        if (staleText.Contains(created.Grant.Bearer, StringComparison.OrdinalIgnoreCase) ||
            staleText.Contains(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase) ||
            staleText.Contains(EndpointPathSecret, StringComparison.Ordinal))
            throw new Exception("owner metadata leaked bearer/hash/endpoint path token");
        if (!staleText.Contains(created.Grant.LeaseId, StringComparison.Ordinal))
            throw new Exception("owner metadata lost exact LeaseId binding");

        child.Kill(true);
        await child.WaitForExitAsync();
        var recovered = await service.AcquireAsync(workspace, "alpha", "post-crash-recovery-owner", default, 1500);
        var activeAfterCrash = legacy.ListActive(workspace, "alpha");
        if (!activeAfterCrash.Any(x => x.LeaseId == created.Grant.LeaseId) || Sha(statePath) != canonicalBefore)
            throw new Exception("owner crash revoked or mutated canonical lease authority");

        var releaseRefused = false;
        try { _ = await service.ReleaseAfterListenerStoppedAsync(recovered, false, default); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("MCP_SESSION_RELEASE_REFUSED_LISTENER_STILL_ACTIVE", StringComparison.Ordinal)) { releaseRefused = true; }
        if (!releaseRefused || recovered.Released) throw new Exception("release without listener-stop proof did not fail closed");
        busy = false;
        try { _ = await service.AcquireAsync(workspace, "alpha", "while-release-refused", default, 250); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("MCP_SESSION_OWNED_BY_OTHER_PROCESS", StringComparison.Ordinal)) { busy = true; }
        if (!busy) throw new Exception("refused release did not retain cross-process ownership");
        _ = await service.ReleaseUnstartedAsync(recovered, true, "post-crash no listener exists", default);

        var normal = await service.AcquireAsync(workspace, "alpha", "normal-stop-order", default, 1000);
        await service.BindExactLeaseAsync(normal, created.Grant.LeaseId, default);
        var normalGrant = new LocalAppMcpAdapterGrantV049(
            LocalAppMcpReadAdapterV049Service.GrantSchema,
            LocalAppMcpReadAdapterV049Service.Version,
            DateTimeOffset.Now, "alpha", created.Grant.LeaseId,
            "http://127.0.0.1:45679/mcp/" + EndpointPathSecret,
            "synthetic-not-persisted", created.Grant.ExpiresAt,
            new[] { "read_local_app_chunk", "list_local_app_entries" }, true, false, false, "qualification-only");
        await service.MarkListenerReadyAsync(normal, normalGrant, default);
        var released = await service.ReleaseAfterListenerStoppedAsync(normal, true, default);
        if (!released.Receipt.CrossProcessHandleReleased || released.Receipt.CanonicalLeaseMutated ||
            released.Receipt.BearerPlaintextUsedOrDisclosed || released.Receipt.BearerHashUsedOrDisclosed ||
            released.Receipt.EndpointSecretUsedOrDisclosed || released.Receipt.LeaseAuthorityGranted)
            throw new Exception("normal owner release receipt safety mismatch");
        var receiptText = File.ReadAllText(released.ReceiptPath, Encoding.UTF8);
        if (receiptText.Contains(created.Grant.Bearer, StringComparison.OrdinalIgnoreCase) ||
            receiptText.Contains(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase) ||
            receiptText.Contains(EndpointPathSecret, StringComparison.Ordinal))
            throw new Exception("ownership release receipt leaked secret material");
        if (!legacy.ListActive(workspace, "alpha").Any(x => x.LeaseId == created.Grant.LeaseId))
            throw new Exception("ownership release silently revoked canonical lease");

        Console.WriteLine(
            "V0517_RUNTIME_PASS sameAppBusy=true busyBeforeLeaseMutation=true differentAppIndependent=true " +
            "killedOwnerReleased=true crashLeaseStillLive=true releaseWithoutStopRefused=true ownerRetainedOnRefusal=true " +
            "normalReleaseAfterStop=true bearer=false endpointSecret=false canonicalOwnershipMutation=false");
    }
}
