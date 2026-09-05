using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.App;

static string Sha(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static void WriteJson<T>(string path, T value)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
}

var root = Path.Combine(Path.GetTempPath(), "matawaka-v0541-status-" + Guid.NewGuid().ToString("N"));
try
{
    var workspace = Path.Combine(root, "Workspace");
    var repo = Path.Combine(workspace, "Workbench");
    Directory.CreateDirectory(Path.Combine(repo, ".git"));
    var acquisitionReceipts = Path.Combine(repo, "artifacts", "artifact-acquisition-v052");
    Directory.CreateDirectory(acquisitionReceipts);
    var acquiredRoot = Path.Combine(root, "acquired");
    Directory.CreateDirectory(acquiredRoot);

    var archivePath = Path.Combine(acquiredRoot, "smoke.zip");
    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
    {
        var entry = zip.CreateEntry("bin/smoke.exe", CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(new byte[] { 0x4d, 0x5a, 0x90, 0x00 });
    }
    var archiveBytes = new FileInfo(archivePath).Length;
    var archiveSha = Sha(archivePath);
    var item = new ArtifactAcquisitionItemEvidenceV052(
        "artifact-v0541-status-smoke",
        "https://example.invalid/immutable/smoke.zip",
        "smoke.zip",
        archivePath,
        null,
        "SHA256_VERIFIED",
        0,
        archiveBytes,
        archiveBytes,
        archiveSha,
        true,
        true,
        false,
        true,
        true,
        null);

    ArtifactAcquisitionExecutionReceiptV052 Receipt(string status) => new(
        BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema,
        BoundedArtifactAcquisitionV052Service.Version,
        DateTimeOffset.UtcNow,
        "acqtx-v0541-status-smoke",
        "acqlease-v0541-status-smoke",
        "acqreq-v0541-status-smoke",
        "ACQUISITION_VERIFIED",
        archiveBytes,
        new[] { item },
        Path.Combine(root, "transaction.json"),
        new string('1', 64),
        Path.Combine(root, "state.json"),
        new string('2', 64),
        true,
        true,
        true,
        false,
        false,
        false,
        false,
        false,
        false,
        Array.Empty<string>(),
        status,
        "qualification fixture");

    var canonicalReceiptPath = Path.Combine(acquisitionReceipts, "execution-canonical.json");
    WriteJson(canonicalReceiptPath, Receipt("ACQUISITION_VERIFIED"));
    var service = new BoundedRuntimeTreeMaterializationV054Service();
    var canonicalRequest = new RuntimeMaterializationRequestV054(
        BoundedRuntimeTreeMaterializationV054Service.RequestSchema,
        "matreq-v0541-canonical-status",
        canonicalReceiptPath,
        Sha(canonicalReceiptPath),
        new[] { item.ArtifactId },
        Path.Combine(root, "runtime-canonical"),
        10,
        1024,
        60);
    var preview = service.Preview(workspace, canonicalRequest, CancellationToken.None);
    if (!preview.ReadyForExplicitMaterializationAuthority || preview.ExactFileCount != 1)
        throw new Exception("canonical ACQUISITION_VERIFIED receipt did not reach no-effect materialization preview");
    Console.WriteLine("PASS canonical execution-receipt Status=ACQUISITION_VERIFIED accepted");

    var uiReceiptPath = Path.Combine(acquisitionReceipts, "execution-ui-wrapper-label.json");
    WriteJson(uiReceiptPath, Receipt("ARTIFACT_ACQUISITION_VERIFIED"));
    var uiRequest = canonicalRequest with
    {
        RequestId = "matreq-v0541-ui-wrapper-status",
        AcquisitionReceiptPath = uiReceiptPath,
        AcquisitionReceiptSha256 = Sha(uiReceiptPath),
        DestinationRoot = Path.Combine(root, "runtime-ui")
    };
    try
    {
        _ = service.Preview(workspace, uiRequest, CancellationToken.None);
        throw new Exception("UI wrapper status was incorrectly accepted as canonical v0.52 execution receipt status");
    }
    catch (RuntimeMaterializationExceptionV054 ex) when (ex.Classification == "ACQUISITION_RECEIPT_NOT_VERIFIED")
    {
        Console.WriteLine("PASS UI-only ARTIFACT_ACQUISITION_VERIFIED status refused");
    }

    Console.WriteLine("V0541_RECEIPT_STATUS_BINDING_PASS");
    return 0;
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}
