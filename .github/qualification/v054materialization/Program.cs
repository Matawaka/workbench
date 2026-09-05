using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static int _sequence;

    public static async Task<int> Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "matawaka-v054-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var workspace = Path.Combine(root, "Workspace");
            var repo = Path.Combine(workspace, "Workbench");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "artifacts", "artifact-acquisition-v052"));

            await HappyPathAsync(root, workspace, repo);
            ReceiptHashRefusal(root, workspace, repo);
            ArchiveDriftRefusal(root, workspace, repo);
            ZipPathRefusals(root, workspace, repo);
            ZipLinkRefusal(root, workspace, repo);
            ZipCollisionRefusals(root, workspace, repo);
            CeilingRefusals(root, workspace, repo);
            ExistingFinalRootRefusal(root, workspace, repo);
            await WrongBearerRefusalAsync(root, workspace, repo);
            await ExpiryRefusalAsync(root, workspace, repo);
            await ReuseRefusalAsync(root, workspace, repo);

            Console.WriteLine("V054_HOSTILE_MATERIALIZATION_PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task HappyPathAsync(string root, string workspace, string repo)
    {
        var exe = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
        var dll = Encoding.UTF8.GetBytes("matawaka-v054-runtime-file\n");
        var archive = MakeZip(root, "happy", zip =>
        {
            WriteEntry(zip, "bin/smoke.exe", exe);
            WriteEntry(zip, "lib/runtime.dll", dll);
            zip.CreateEntry("empty/");
        });
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-happy", archive.Path, archive.Bytes, archive.Sha256));
        var destination = Path.Combine(root, "runtime-happy");
        var request = Request(receipt, new[] { "artifact-happy" }, destination, maxFiles: 10, maxBytes: 1024 * 1024, ttl: 60);
        var service = new BoundedRuntimeTreeMaterializationV054Service();
        var preview1 = service.Preview(workspace, request, CancellationToken.None);
        var preview2 = service.Preview(workspace, request, CancellationToken.None);
        Require(preview1.PlanSha256 == preview2.PlanSha256, "plan digest must be deterministic");
        Require(preview1.ExactFileCount == 2, "happy plan file count");
        Require(preview1.ExactExpandedBytes == exe.Length + dll.Length, "happy plan expanded byte total");
        Require(!preview1.FilesystemMutationPerformed && !preview1.ExtractionPerformed && !preview1.ProcessExecutionPerformed, "preview must be no-effect");

        var authority = await service.GrantAsync(workspace, preview1, CancellationToken.None);
        Require(!authority.Receipt.FilesystemMutationPerformed && !authority.Receipt.ExtractionPerformed && !authority.Receipt.ProcessExecutionPerformed && !authority.Receipt.NetworkAccessPerformed,
            "authority grant must be no-effect");
        var result = await service.MaterializeAsync(workspace, authority.Grant, CancellationToken.None);
        Require(result.Receipt.Status == "RUNTIME_TREE_MATERIALIZATION_VERIFIED", "happy terminal status");
        Require(result.Receipt.State == "MATERIALIZED_VERIFIED", "happy terminal state");
        Require(result.Receipt.MaterializedFiles == 2, "happy receipt file count");
        Require(result.Receipt.MaterializedBytes == exe.Length + dll.Length, "happy receipt byte total");
        Require(result.Receipt.ExtractionPerformed && result.Receipt.RootPromoted, "happy extraction/promotion evidence");
        Require(!result.Receipt.ProcessExecutionPerformed && !result.Receipt.RuntimeStartPerformed && !result.Receipt.NetworkAccessPerformed &&
                !result.Receipt.BenchmarkPerformed && !result.Receipt.ModelRequestPerformed && !result.Receipt.GameAccessPerformed,
            "materialization must grant no post-materialization effects");
        Require(Directory.Exists(destination), "final runtime root missing");
        Require(!Directory.Exists(Path.Combine(root, ".runtime-happy." + authority.Grant.LeaseId + ".partial")), "staging root should be atomically promoted");
        Require(File.ReadAllBytes(Path.Combine(destination, "bin", "smoke.exe")).SequenceEqual(exe), "materialized executable bytes drifted");

        var manifestPath = result.Receipt.RuntimeManifestPath;
        Require(File.Exists(manifestPath), "runtime-tree manifest missing");
        Require(HashFile(manifestPath) == result.Receipt.RuntimeManifestSha256, "runtime-tree manifest receipt hash mismatch");
        var manifest = JsonSerializer.Deserialize<RuntimeTreeManifestV053>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("runtime-tree manifest parse failed");
        Require(manifest.Schema == BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema, "runtime-tree manifest schema");
        Require(manifest.State == BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState, "runtime-tree manifest state");
        Require(manifest.RuntimeRoot == destination, "runtime-tree manifest root");

        using var execution = new BoundedRuntimeExecutionV053Service();
        var executionRequest = new RuntimeExecutionRequestV053(
            BoundedRuntimeExecutionV053Service.RequestSchema,
            "runtime-preview-v054-probe",
            manifestPath,
            HashFile(manifestPath),
            "bin/smoke.exe",
            HashBytes(exe),
            Array.Empty<string>(),
            ".",
            new Dictionary<string, string>(),
            60,
            0,
            true);
        var executionPreview = execution.Preview(workspace, executionRequest, CancellationToken.None);
        Require(executionPreview.ReadyForExplicitExecutionAuthority, "unchanged v0.53 execution preview must accept v0.54 manifest");
        Require(!executionPreview.ProcessExecutionPerformed && !executionPreview.RuntimeTreeMaterializationPerformed, "v0.53 preview must remain no-effect");
        Console.WriteLine($"PASS happy files={result.Receipt.MaterializedFiles} bytes={result.Receipt.MaterializedBytes} plan={preview1.PlanSha256}");
    }

    private static void ReceiptHashRefusal(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "receipt-hash", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-receipt-hash", archive.Path, archive.Bytes, archive.Sha256));
        var request = Request(receipt, new[] { "artifact-receipt-hash" }, NextDestination(root), 10, 1024, 60) with
        {
            AcquisitionReceiptSha256 = new string('0', 64)
        };
        Expect("ACQUISITION_RECEIPT_HASH_MISMATCH", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(workspace, request, CancellationToken.None));
    }

    private static void ArchiveDriftRefusal(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "archive-drift", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-drift", archive.Path, archive.Bytes, archive.Sha256));
        File.AppendAllBytes(archive.Path, new byte[] { 0x44 });
        var request = Request(receipt, new[] { "artifact-drift" }, NextDestination(root), 10, 1024, 60);
        Expect("ARCHIVE_SIZE_DRIFT", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(workspace, request, CancellationToken.None));
    }

    private static void ZipPathRefusals(string root, string workspace, string repo)
    {
        foreach (var raw in new[] { "../escape.txt", "/rooted.txt", "C:/drive.txt", "folder/a:b.txt", "CON.txt", "folder/trailing. ", "folder/a." })
        {
            var archive = MakeZip(root, "bad-path", zip => WriteEntry(zip, raw, "x"u8.ToArray()));
            var id = "artifact-path-" + Interlocked.Increment(ref _sequence);
            var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact(id, archive.Path, archive.Bytes, archive.Sha256));
            var request = Request(receipt, new[] { id }, NextDestination(root), 10, 1024, 60);
            Expect("ZIP_PATH_POLICY_REFUSED", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(workspace, request, CancellationToken.None));
        }
    }

    private static void ZipLinkRefusal(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "symlink", zip =>
        {
            var entry = zip.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)(0xA000u << 16));
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("target");
        });
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-link", archive.Path, archive.Bytes, archive.Sha256));
        var request = Request(receipt, new[] { "artifact-link" }, NextDestination(root), 10, 1024, 60);
        Expect("ZIP_LINK_ENTRY_REFUSED", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(workspace, request, CancellationToken.None));
    }

    private static void ZipCollisionRefusals(string root, string workspace, string repo)
    {
        var duplicate = MakeZip(root, "duplicate", zip =>
        {
            WriteEntry(zip, "x.dll", "1"u8.ToArray());
            WriteEntry(zip, "X.dll", "2"u8.ToArray());
        });
        var duplicateReceipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-duplicate", duplicate.Path, duplicate.Bytes, duplicate.Sha256));
        Expect("ZIP_PATH_COLLISION", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(duplicateReceipt, new[] { "artifact-duplicate" }, NextDestination(root), 10, 1024, 60), CancellationToken.None));

        var prefix = MakeZip(root, "prefix", zip =>
        {
            WriteEntry(zip, "a", "1"u8.ToArray());
            WriteEntry(zip, "a/b.txt", "2"u8.ToArray());
        });
        var prefixReceipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-prefix", prefix.Path, prefix.Bytes, prefix.Sha256));
        Expect("ZIP_PATH_COLLISION", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(prefixReceipt, new[] { "artifact-prefix" }, NextDestination(root), 10, 1024, 60), CancellationToken.None));

        var first = MakeZip(root, "cross-first", zip => WriteEntry(zip, "same.dll", "1"u8.ToArray()));
        var second = MakeZip(root, "cross-second", zip => WriteEntry(zip, "SAME.dll", "2"u8.ToArray()));
        var crossReceipt = WriteAcquisitionReceipt(repo,
            new SourceArtifact("artifact-cross-1", first.Path, first.Bytes, first.Sha256),
            new SourceArtifact("artifact-cross-2", second.Path, second.Bytes, second.Sha256));
        Expect("ZIP_PATH_COLLISION", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(crossReceipt, new[] { "artifact-cross-1", "artifact-cross-2" }, NextDestination(root), 10, 1024, 60), CancellationToken.None));
    }

    private static void CeilingRefusals(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "ceilings", zip =>
        {
            WriteEntry(zip, "a.txt", new byte[10]);
            WriteEntry(zip, "b.txt", new byte[10]);
        });
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-ceilings", archive.Path, archive.Bytes, archive.Sha256));
        Expect("FILE_CEILING_EXCEEDED", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(receipt, new[] { "artifact-ceilings" }, NextDestination(root), 1, 100, 60), CancellationToken.None));
        Expect("EXPANDED_BYTE_CEILING_EXCEEDED", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(receipt, new[] { "artifact-ceilings" }, NextDestination(root), 10, 15, 60), CancellationToken.None));
    }

    private static void ExistingFinalRootRefusal(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "existing-root", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-existing-root", archive.Path, archive.Bytes, archive.Sha256));
        var destination = NextDestination(root);
        Directory.CreateDirectory(destination);
        Expect("FINAL_ROOT_EXISTS", () => new BoundedRuntimeTreeMaterializationV054Service().Preview(
            workspace, Request(receipt, new[] { "artifact-existing-root" }, destination, 10, 1024, 60), CancellationToken.None));
    }

    private static async Task WrongBearerRefusalAsync(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "wrong-bearer", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-wrong-bearer", archive.Path, archive.Bytes, archive.Sha256));
        var service = new BoundedRuntimeTreeMaterializationV054Service();
        var preview = service.Preview(workspace, Request(receipt, new[] { "artifact-wrong-bearer" }, NextDestination(root), 10, 1024, 60), CancellationToken.None);
        var authority = await service.GrantAsync(workspace, preview, CancellationToken.None);
        var bad = authority.Grant with { Bearer = "wrong-bearer" };
        await ExpectAsync("AUTHORITY_BEARER_MISMATCH", () => service.MaterializeAsync(workspace, bad, CancellationToken.None));
    }

    private static async Task ExpiryRefusalAsync(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "expiry", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-expiry", archive.Path, archive.Bytes, archive.Sha256));
        var service = new BoundedRuntimeTreeMaterializationV054Service();
        var preview = service.Preview(workspace, Request(receipt, new[] { "artifact-expiry" }, NextDestination(root), 10, 1024, 1), CancellationToken.None);
        var authority = await service.GrantAsync(workspace, preview, CancellationToken.None);
        await Task.Delay(1200);
        await ExpectAsync("AUTHORITY_EXPIRED", () => service.MaterializeAsync(workspace, authority.Grant, CancellationToken.None));
    }

    private static async Task ReuseRefusalAsync(string root, string workspace, string repo)
    {
        var archive = MakeZip(root, "reuse", zip => WriteEntry(zip, "a.txt", "a"u8.ToArray()));
        var receipt = WriteAcquisitionReceipt(repo, new SourceArtifact("artifact-reuse", archive.Path, archive.Bytes, archive.Sha256));
        var service = new BoundedRuntimeTreeMaterializationV054Service();
        var preview = service.Preview(workspace, Request(receipt, new[] { "artifact-reuse" }, NextDestination(root), 10, 1024, 60), CancellationToken.None);
        var authority = await service.GrantAsync(workspace, preview, CancellationToken.None);
        _ = await service.MaterializeAsync(workspace, authority.Grant, CancellationToken.None);
        await ExpectAsync("AUTHORITY_ALREADY_COMPLETED", () => service.MaterializeAsync(workspace, authority.Grant, CancellationToken.None));
    }

    private static RuntimeMaterializationRequestV054 Request(
        ReceiptInfo receipt,
        IReadOnlyList<string> artifactIds,
        string destination,
        int maxFiles,
        long maxBytes,
        int ttl)
        => new(
            BoundedRuntimeTreeMaterializationV054Service.RequestSchema,
            "matreq-probe-" + Interlocked.Increment(ref _sequence),
            receipt.Path,
            receipt.Sha256,
            artifactIds,
            destination,
            maxFiles,
            maxBytes,
            ttl);

    private static ReceiptInfo WriteAcquisitionReceipt(string repo, params SourceArtifact[] artifacts)
    {
        var receiptDir = Path.Combine(repo, "artifacts", "artifact-acquisition-v052");
        Directory.CreateDirectory(receiptDir);
        var items = artifacts.Select(a => new ArtifactAcquisitionItemEvidenceV052(
            a.ArtifactId,
            "https://example.invalid/" + Uri.EscapeDataString(Path.GetFileName(a.Path)),
            Path.GetFileName(a.Path),
            a.Path,
            null,
            "SHA256_VERIFIED",
            0,
            0,
            a.Bytes,
            a.Sha256,
            true,
            true,
            true,
            false,
            false,
            null)).ToArray();
        var receipt = new ArtifactAcquisitionExecutionReceiptV052(
            BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema,
            BoundedArtifactAcquisitionV052Service.Version,
            DateTimeOffset.Now,
            "acqtx-probe-" + Guid.NewGuid().ToString("N"),
            "acqlease-probe-" + Guid.NewGuid().ToString("N"),
            "acqreq-probe-" + Guid.NewGuid().ToString("N"),
            "ACQUISITION_VERIFIED",
            0,
            items,
            Path.Combine(receiptDir, "fixture-transaction.json"),
            new string('0', 64),
            Path.Combine(receiptDir, "fixture-state.json"),
            new string('0', 64),
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            Array.Empty<string>(),
            "ARTIFACT_ACQUISITION_VERIFIED",
            "Qualification fixture representing exact v0.52 verified existing local artifact evidence.");
        var path = Path.Combine(receiptDir, "execution-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false));
        return new ReceiptInfo(path, HashFile(path));
    }

    private static ZipInfo MakeZip(string root, string role, Action<ZipArchive> fill)
    {
        var path = Path.Combine(root, role + "-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            fill(zip);
        return new ZipInfo(path, new FileInfo(path).Length, HashFile(path));
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string NextDestination(string root)
        => Path.Combine(root, "runtime-" + Interlocked.Increment(ref _sequence));

    private static void Expect(string expected, Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected refusal {expected} but operation succeeded.");
        }
        catch (RuntimeMaterializationExceptionV054 ex) when (ex.Classification == expected)
        {
            Console.WriteLine("PASS refused " + expected);
        }
    }

    private static async Task ExpectAsync(string expected, Func<Task> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Expected refusal {expected} but operation succeeded.");
        }
        catch (RuntimeMaterializationExceptionV054 ex) when (ex.Classification == expected)
        {
            Console.WriteLine("PASS refused " + expected);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Probe assertion failed: " + message);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record ZipInfo(string Path, long Bytes, string Sha256);
    private sealed record SourceArtifact(string ArtifactId, string Path, long Bytes, string Sha256);
    private sealed record ReceiptInfo(string Path, string Sha256);
}
