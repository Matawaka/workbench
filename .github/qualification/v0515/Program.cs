using System.IO;
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

    private static string StatePath(string workspace, string app, string lease)
        => Path.Combine(StateDir(workspace, app), lease + ".json");

    private static string IndexPath(string workspace, string app)
        => Path.Combine(workspace, "Workbench", ".workbench", "read-lease-index-v0515", app, "active-index-v0.51.5.json");

    private static Dictionary<string, string> Snapshot(string workspace, string app)
        => Directory.EnumerateFiles(StateDir(workspace, app), "lease-*.json")
            .ToDictionary(x => Path.GetFileName(x), Sha, StringComparer.OrdinalIgnoreCase);

    private static void RequireSameCanonical(
        Dictionary<string, string> before,
        Dictionary<string, string> after,
        string role)
    {
        if (before.Count != after.Count) throw new Exception(role + ": canonical state file count changed");
        foreach (var pair in before)
            if (!after.TryGetValue(pair.Key, out var sha) || sha != pair.Value)
                throw new Exception(role + ": canonical bytes changed: " + pair.Key);
    }

    public static async Task Main()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "matawaka-v0515-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "Workbench", "artifacts"));

        void MakeApp(string id)
        {
            var root = Path.Combine(workspace, "Apps", id);
            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(
                Path.Combine(root, ".matawaka-app.json"),
                JsonSerializer.Serialize(new LocalApplicationIdentity(LocalApplicationMaintenanceService.IdentitySchema, id, "1.0.0")),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "data", "state.json"), "{\"ok\":true}", new UTF8Encoding(false));
        }

        MakeApp("alpha");
        MakeApp("beta");

        var legacy = new LocalAppReadLeaseV048Service();
        var exactLegacy = new LocalAppReadLeaseExactRevokeV0512Service();
        var index = new LocalAppActiveLeaseIndexV0515Service();
        var lifecycle = new LocalAppReadLeaseIndexedLifecycleV0515Service();
        var secrets = new List<string>();

        async Task<(LocalAppReadLeaseGrantV048 Grant, LocalAppReadLeaseCreationReceiptV048 Receipt)> LegacyCreate(
            string app,
            string requestId,
            int calls = 8,
            int ttl = 900)
        {
            var request = new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                requestId,
                app,
                new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
                65536,
                262144,
                calls,
                ttl);
            var preview = legacy.Preview(workspace, app, request, default);
            var created = await legacy.CreateAsync(workspace, app, preview, false, default);
            secrets.Add(created.Grant.Bearer);
            secrets.Add(created.Receipt.BearerSha256);
            return (created.Grant, created.Receipt);
        }

        async Task<LocalAppIndexedLeaseCreateResultV0515> IndexedCreate(
            string app,
            string requestId,
            int calls = 8,
            int ttl = 900)
        {
            var request = new LocalAppReadLeaseRequestV048(
                LocalAppReadLeaseV048Service.RequestSchema,
                requestId,
                app,
                new[] { new LocalAppReadLeaseScopeV048("installed", "data/") },
                65536,
                262144,
                calls,
                ttl);
            var preview = legacy.Preview(workspace, app, request, default);
            var created = await lifecycle.CreateIndexedAsync(workspace, app, preview, false, default);
            secrets.Add(created.Grant.Bearer);
            secrets.Add(created.Receipt.BearerSha256);
            return created;
        }

        // Migration: many historical canonical states + a few live states, no index.
        for (var i = 0; i < 80; i++)
        {
            var historical = await LegacyCreate("alpha", $"migrate:hist:{i:D3}");
            _ = await exactLegacy.RevokeExactAsync(workspace, "alpha", historical.Grant.LeaseId, default);
        }
        var liveA = await LegacyCreate("alpha", "migrate:live:a");
        var liveB = await LegacyCreate("alpha", "migrate:live:b");
        var liveC = await LegacyCreate("alpha", "migrate:live:c");

        var beforeReconcile = Snapshot(workspace, "alpha");
        var initial = await index.GetReadinessAsync(workspace, "alpha", default);
        if (initial.Ready || !initial.ReconciliationRequired)
            throw new Exception("missing index was not reconciliation-required");

        var reconciled = await index.ReconcileAsync(workspace, "alpha", default);
        if (reconciled.Receipt.CanonicalStateRecords != 83 ||
            reconciled.Receipt.LiveCandidatesIndexed != 3 ||
            reconciled.Index.Entries.Count != 3)
            throw new Exception("migration reconciliation counts mismatch");
        RequireSameCanonical(beforeReconcile, Snapshot(workspace, "alpha"), "reconciliation");

        var indexText = File.ReadAllText(IndexPath(workspace, "alpha"));
        var receiptText = File.ReadAllText(reconciled.ReceiptPath);
        foreach (var secret in secrets)
            if (indexText.Contains(secret, StringComparison.OrdinalIgnoreCase) ||
                receiptText.Contains(secret, StringComparison.OrdinalIgnoreCase))
                throw new Exception("bearer/plain/hash leaked into index or reconciliation receipt");
        if (indexText.Contains("Scopes", StringComparison.Ordinal) ||
            indexText.Contains("BearerSha256", StringComparison.Ordinal))
            throw new Exception("derived index duplicated scope or bearer-hash authority material");

        var fast = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default);
        if (fast.LiveLeaseCount != 3 || fast.CanonicalHistoricalScanPerformed)
            throw new Exception("fast verified live status mismatch");

        // Prove fast live status does not parse historical canonical files after reconciliation.
        var historicalPath = beforeReconcile.Keys
            .Select(x => Path.Combine(StateDir(workspace, "alpha"), x))
            .First(x => File.ReadAllText(x).Contains("\"Revoked\": true", StringComparison.Ordinal));
        var historicalBytes = File.ReadAllBytes(historicalPath);
        File.WriteAllText(historicalPath, "{BROKEN-HISTORY", new UTF8Encoding(false));
        var fastWithBrokenHistory = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default);
        if (fastWithBrokenHistory.LiveLeaseCount != 3)
            throw new Exception("fast status depended on broken historical state");
        File.WriteAllBytes(historicalPath, historicalBytes);

        // Crash gap: dirty marker precedes canonical create; index use must refuse until bounded reconciliation.
        _ = await index.BeginMutationAsync(workspace, "alpha", "hostile-partial-create", null, default);
        var recoveredLive = await LegacyCreate("alpha", "partial:live:d");
        var refused = false;
        try { _ = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("ACTIVE_INDEX_RECONCILIATION_REQUIRED", StringComparison.Ordinal)) { refused = true; }
        if (!refused) throw new Exception("dirty partial create did not fail closed");

        var repaired = await index.ReconcileAsync(workspace, "alpha", default);
        if (!repaired.Index.Entries.Any(x => x.LeaseId == recoveredLive.Grant.LeaseId))
            throw new Exception("reconciliation did not recover omitted live canonical lease");

        // Budget exhaustion can only reduce authority: stale candidate is lazily pruned, canonical bytes remain untouched by prune.
        var exhausted = await IndexedCreate("alpha", "indexed:exhaust", calls: 1);
        var read = new LocalAppLeaseReadRequestV048(
            LocalAppReadLeaseV048Service.ReadRequestSchema,
            "consume:one",
            exhausted.Grant.LeaseId,
            exhausted.Grant.Bearer,
            "alpha",
            "installed",
            "data/state.json",
            0,
            64,
            null);
        _ = await legacy.AuthorizeAndReadAsync(workspace, read, default);
        var exhaustedPath = StatePath(workspace, "alpha", exhausted.Grant.LeaseId);
        var exhaustedSha = Sha(exhaustedPath);
        var afterExhaust = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default);
        if (afterExhaust.LiveAuthorities.Any(x => x.LeaseId == exhausted.Grant.LeaseId) || Sha(exhaustedPath) != exhaustedSha)
            throw new Exception("budget-exhausted lazy prune mutated canonical state or retained candidate");

        // Natural expiry is also lazy derived-index pruning only.
        var expiring = await IndexedCreate("alpha", "indexed:expire", ttl: 1);
        var expiringPath = StatePath(workspace, "alpha", expiring.Grant.LeaseId);
        var expiringSha = Sha(expiringPath);
        await Task.Delay(1300);
        var afterExpiry = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default);
        if (afterExpiry.LiveAuthorities.Any(x => x.LeaseId == expiring.Grant.LeaseId) || Sha(expiringPath) != expiringSha)
            throw new Exception("expiry prune mutated canonical state or retained candidate");

        // Exact indexed revoke removes only target candidate/canonical authority, sibling bytes remain identical.
        var siblingPath = StatePath(workspace, "alpha", liveC.Grant.LeaseId);
        var siblingSha = Sha(siblingPath);
        var closed = await lifecycle.RevokeExactIndexedAsync(workspace, "alpha", liveB.Grant.LeaseId, default);
        if (closed.ExactReceipt.SiblingLeasesRevoked != 0 || Sha(siblingPath) != siblingSha)
            throw new Exception("indexed exact revoke touched sibling canonical state");
        var afterClose = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default);
        if (afterClose.LiveAuthorities.Any(x => x.LeaseId == liveB.Grant.LeaseId))
            throw new Exception("revoked target remained indexed live");

        // Dirty marker even without canonical mutation must block index use until reconciliation.
        _ = await index.BeginMutationAsync(workspace, "alpha", "hostile-dirty-only", null, default);
        refused = false;
        try { _ = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("ACTIVE_INDEX_RECONCILIATION_REQUIRED", StringComparison.Ordinal)) { refused = true; }
        if (!refused) throw new Exception("dirty-only marker did not block index use");
        _ = await index.ReconcileAsync(workspace, "alpha", default);

        // Forged candidate with missing canonical state is explicit inconsistency, never silently omitted.
        var parsedIndex = JsonSerializer.Deserialize<LocalAppActiveLeaseIndexV0515>(File.ReadAllText(IndexPath(workspace, "alpha")))!;
        var forged = parsedIndex with
        {
            IndexRevision = parsedIndex.IndexRevision + 1,
            Entries = parsedIndex.Entries.Concat(new[] { new LocalAppActiveLeaseIndexEntryV0515("lease-forgedmissing", 0) }).ToArray()
        };
        File.WriteAllText(
            IndexPath(workspace, "alpha"),
            JsonSerializer.Serialize(forged, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        var inconsistent = false;
        try { _ = await index.ObserveLiveAuthorityAsync(workspace, "alpha", null, null, default); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("ACTIVE_INDEX_INCONSISTENT", StringComparison.Ordinal)) { inconsistent = true; }
        if (!inconsistent) throw new Exception("missing canonical indexed candidate was silently ignored");
        _ = await index.ReconcileAsync(workspace, "alpha", default);

        // Separate >32 live fixture: reconciliation retains all candidates, fast status refuses partial authority disclosure.
        for (var i = 0; i < 33; i++) _ = await LegacyCreate("beta", $"overflow:{i:D2}");
        var betaReconcile = await index.ReconcileAsync(workspace, "beta", default);
        if (betaReconcile.Index.Entries.Count != 33) throw new Exception("overflow reconciliation did not retain all 33 candidates");
        var overflow = false;
        try { _ = await index.ObserveLiveAuthorityAsync(workspace, "beta", null, null, default); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("LIVE_AUTHORITY_OVERFLOW:", StringComparison.Ordinal)) { overflow = true; }
        if (!overflow) throw new Exception("33 verified live candidates did not fail closed with LIVE_AUTHORITY_OVERFLOW");

        Console.WriteLine(
            "V0515_RUNTIME_PASS migrationCanonical=83 migrationLive=3 fastHistoricalScan=false " +
            "partialRecovered=true exhaustPruned=true expiryPruned=true exactSibling=false " +
            "dirtyRefusal=true inconsistentRefusal=true overflow=33 bearer=false");
    }
}
