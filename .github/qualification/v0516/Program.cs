using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string StateDir(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "read-leases", app);

    private static string IndexPath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "read-lease-index-v0515", app, "active-index-v0.51.5.json");

    private static string FencePath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "active-index-fence-v0516", app, "active-index-v0.51.6.lock");

    private static async Task WaitForFileAsync(string path, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("child ready marker timeout: " + path);
            await Task.Delay(25);
        }
    }

    private static Process StartChild(params string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(assembly);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new Exception("failed to start child probe process");
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

    private static async Task<int> ChildAsync(string[] args)
    {
        var mode = args[0];
        var workspace = args[1];
        var app = args[2];
        var ready = args[3];
        var holdMs = int.Parse(args[4]);
        var fence = new LocalAppActiveIndexFenceV0516Service();
        await using var held = await fence.AcquireAsync(workspace, app, "hostile-child-" + mode, default, 5000);
        if (mode == "dirty-hold")
        {
            var index = new LocalAppActiveLeaseIndexV0515Service();
            _ = await index.BeginMutationAsync(workspace, app, "hostile-crash-gap", null, default);
        }
        File.WriteAllText(ready, "ready", new UTF8Encoding(false));
        await Task.Delay(holdMs);
        return 0;
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0) return await ChildAsync(args);

        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v0516-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));
        MakeApp(workspace, "alpha");
        MakeApp(workspace, "beta");

        var legacy = new LocalAppReadLeaseV048Service();
        var index = new LocalAppActiveLeaseIndexV0515Service();
        var lifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();
        var fence = new LocalAppActiveIndexFenceV0516Service();

        async Task<(LocalAppReadLeaseGrantV048 Grant, LocalAppReadLeaseCreationReceiptV048 Receipt)> LegacyCreate(string app, string requestId)
        {
            var request = new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                requestId,
                app,
                new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
                65536,
                262144,
                8,
                900);
            var preview = legacy.Preview(workspace, app, request, default);
            var created = await legacy.CreateAsync(workspace, app, preview, false, default);
            return (created.Grant, created.Receipt);
        }

        LocalAppReadLeasePreviewV048 Preview(string app, string requestId)
        {
            var request = new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                requestId,
                app,
                new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
                65536,
                262144,
                8,
                900);
            return legacy.Preview(workspace, app, request, default);
        }

        var seed = await LegacyCreate("alpha", "seed-live");
        _ = await index.ReconcileAsync(workspace, "alpha", default);
        _ = await index.ReconcileAsync(workspace, "beta", default);

        var seedStatePath = Path.Combine(StateDir(workspace, "alpha"), seed.Grant.LeaseId + ".json");
        var indexBeforeBusy = Sha(IndexPath(workspace, "alpha"));
        var seedBeforeBusy = Sha(seedStatePath);

        // Same-app contention: second process must fail closed and mutate nothing.
        var ready1 = Path.Combine(workspace, "ready-1.txt");
        using (var holder = StartChild("hold", workspace, "alpha", ready1, "1400"))
        {
            await WaitForFileAsync(ready1);
            var busy = false;
            try
            {
                await using var impossible = await fence.AcquireAsync(workspace, "alpha", "hostile-timeout", default, 250);
            }
            catch (InvalidDataException ex) when (ex.Message.StartsWith("ACTIVE_INDEX_FENCE_BUSY", StringComparison.Ordinal))
            {
                busy = true;
            }
            if (!busy) throw new Exception("same-app fence contention did not fail closed");
            if (Sha(IndexPath(workspace, "alpha")) != indexBeforeBusy || Sha(seedStatePath) != seedBeforeBusy)
                throw new Exception("fence timeout mutated canonical/index bytes");

            var betaSw = Stopwatch.StartNew();
            await using (var betaFence = await fence.AcquireAsync(workspace, "beta", "different-app", default, 500)) { }
            if (betaSw.ElapsedMilliseconds > 450) throw new Exception("different ApplicationId was blocked by alpha fence");
            await holder.WaitForExitAsync();
            if (holder.ExitCode != 0) throw new Exception("holder child failed");
        }

        // Released owner can be reacquired immediately.
        await using (var reacquired = await fence.AcquireAsync(workspace, "alpha", "post-release", default, 500))
        {
            if (!reacquired.Observation.CrossProcessFenceAcquired) throw new Exception("post-release fence not acquired");
        }

        // Killed owner releases OS handle ownership automatically.
        var ready2 = Path.Combine(workspace, "ready-2.txt");
        using (var killed = StartChild("hold", workspace, "alpha", ready2, "10000"))
        {
            await WaitForFileAsync(ready2);
            killed.Kill(true);
            await killed.WaitForExitAsync();
        }
        await using (var afterKill = await fence.AcquireAsync(workspace, "alpha", "after-kill", default, 1000)) { }

        // Crash after dirty marker: OS fence releases, but durable v0.51.5 dirty state still blocks status.
        var ready3 = Path.Combine(workspace, "ready-3.txt");
        using (var dirtyOwner = StartChild("dirty-hold", workspace, "alpha", ready3, "10000"))
        {
            await WaitForFileAsync(ready3);
            dirtyOwner.Kill(true);
            await dirtyOwner.WaitForExitAsync();
        }
        await using (var afterDirtyKill = await fence.AcquireAsync(workspace, "alpha", "after-dirty-kill", default, 1000)) { }
        var dirtyRefused = false;
        try
        {
            _ = await lifecycle.ObserveCoherentLiveAuthorityV0516Async(workspace, "alpha", null, null, default);
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("ACTIVE_INDEX_RECONCILIATION_REQUIRED", StringComparison.Ordinal))
        {
            dirtyRefused = true;
        }
        if (!dirtyRefused) throw new Exception("dirty crash-gap did not survive fence owner death");
        _ = await lifecycle.ReconcileIndexAsync(workspace, "alpha", default);

        // Coherent fast status evidence.
        var coherent = await lifecycle.ObserveCoherentLiveAuthorityV0516Async(workspace, "alpha", null, null, default);
        if (!coherent.CrossProcessFenceAcquired || !coherent.SnapshotCoherent ||
            coherent.IndexRevisionBeforeObservation != coherent.IndexRevisionAfterObservation ||
            !coherent.DirtyMarkerAbsentBeforeObservation || !coherent.DirtyMarkerAbsentAfterObservation ||
            coherent.CanonicalHistoricalScanPerformed || coherent.BearerPlaintextDisclosed || coherent.BearerHashDisclosed)
            throw new Exception("coherent live status contract mismatch");

        // Create must wait behind another process holding the same fence, then commit normally.
        var ready4 = Path.Combine(workspace, "ready-4.txt");
        using (var holder = StartChild("hold", workspace, "alpha", ready4, "700"))
        {
            await WaitForFileAsync(ready4);
            var sw = Stopwatch.StartNew();
            var created = await lifecycle.CreateIndexedAsync(workspace, "alpha", Preview("alpha", "serialized-create"), false, default);
            if (sw.ElapsedMilliseconds < 450) throw new Exception("indexed create entered while another process held same-app fence");

            // Exact revoke is also serialized behind an independent holder.
            var ready5 = Path.Combine(workspace, "ready-5.txt");
            using var holder2 = StartChild("hold", workspace, "alpha", ready5, "700");
            await WaitForFileAsync(ready5);
            sw.Restart();
            var revoked = await lifecycle.RevokeExactIndexedAsync(workspace, "alpha", created.Grant.LeaseId, default);
            if (sw.ElapsedMilliseconds < 450 || revoked.ExactReceipt.SiblingLeasesRevoked != 0)
                throw new Exception("exact revoke was not serialized or touched sibling authority");
            await holder2.WaitForExitAsync();
            await holder.WaitForExitAsync();
        }

        // Fence file is persistent but content-free and carries no bearer/hash material.
        var fencePath = FencePath(workspace, "alpha");
        if (!File.Exists(fencePath) || new FileInfo(fencePath).Length != 0)
            throw new Exception("fence file should persist as empty serialization control");
        var fenceText = File.ReadAllText(fencePath);
        if (fenceText.Contains(seed.Grant.Bearer, StringComparison.OrdinalIgnoreCase) ||
            fenceText.Contains(seed.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
            throw new Exception("bearer/plain/hash leaked into fence file");

        Console.WriteLine(
            "V0516_RUNTIME_PASS sameAppBusy=true differentAppIndependent=true killedOwnerReleased=true " +
            "dirtySurvivesCrash=true serializedCreate=true serializedRevoke=true coherent=true " +
            $"revision={coherent.IndexRevisionBeforeObservation}->{coherent.IndexRevisionAfterObservation} " +
            "historicalScan=false bearer=false canonicalTimeoutMutation=false");
        return 0;
    }
}
