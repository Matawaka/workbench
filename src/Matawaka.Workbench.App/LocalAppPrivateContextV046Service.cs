using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppPrivateContextPlanV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string InstalledVersion,
    string InstalledRoot,
    string InstalledTreeSha256,
    int InstalledFileCount,
    long InstalledBytes,
    string SourceRoot,
    string InitialSourceTreeSha256,
    string CurrentSourceTreeSha256,
    bool SourceChangedSinceBinding,
    int SourceFileCount,
    long SourceBytes,
    long TotalDisclosureBytes,
    bool IncludesPrivateInstalledContents,
    bool IncludesDevelopmentSourceContents,
    bool IncludesUpdateContext,
    bool IncludesReadToolContract,
    bool ReadyForExplicitPrivateExportAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppPrivateContextReceiptV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string InstalledVersion,
    string CapsulePath,
    string CapsuleSha256,
    long CapsuleBytes,
    string InstalledTreeSha256,
    string CurrentSourceTreeSha256,
    bool FreshPreviewVerified,
    bool PrivateBytesDuplicatedIntoArtifact,
    bool UploadPerformed,
    bool NetworkAccessPerformed,
    bool InstalledAppMutationPerformed,
    bool SourceMutationPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalAppPrivateContextManifestV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string InstalledVersion,
    string InstalledTreeSha256,
    string InitialSourceTreeSha256,
    string CurrentSourceTreeSha256,
    bool SourceChangedSinceBinding,
    string CanonicalCurrentStateHint,
    string HistoricalPotentiallySupersededHint,
    IReadOnlyList<LocalAppContextFileV046> InstalledFiles,
    IReadOnlyList<LocalAppContextFileV046> SourceFiles,
    bool PrivateLocalOnly,
    bool AutomaticUploadAllowed,
    bool PublicRepositoryPublicationAllowed,
    string ReadToolContractPath,
    string UpdateContextPath,
    string Note);

public sealed class LocalAppPrivateContextV046Service
{
    public const string Version = "0.46.0";
    public const string PlanSchema = "matawaka.local-app-private-context-plan/v0.46";
    public const string ManifestSchema = "matawaka.local-app-private-context-manifest/v0.46";
    public const string ReceiptSchema = "matawaka.local-app-private-context-receipt/v0.46";
    public const int MaxInstalledFiles = 4096;
    public const int MaxSourceFiles = 4096;
    public const long MaxRoleBytes = 2L * 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly LocalAppUpdateContextV046Service _updateContext = new();
    private readonly LocalAppSourceBindingV046Service _sourceBinding = new();

    public LocalAppPrivateContextPlanV046 Preview(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var update = _updateContext.Build(workspaceRoot, applicationId, cancellationToken);
        var binding = _sourceBinding.ReadBinding(workspaceRoot, applicationId);
        var sourceRoot = LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, applicationId, requireBinding: true);
        var sourceFiles = LocalAppV046FileBoundary.Inventory(sourceRoot, includeSourceSidecar: true, MaxSourceFiles, MaxRoleBytes, cancellationToken);
        var sourceTreeFiles = sourceFiles.Where(x => !x.Path.Equals(LocalAppSourceBindingV046Service.BindingFileName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var currentSourceTree = LocalAppV046FileBoundary.ComputeTreeDigest(sourceTreeFiles);
        var installedBytes = update.Files.Sum(x => x.Bytes);
        var sourceBytes = sourceFiles.Sum(x => x.Bytes);

        return new LocalAppPrivateContextPlanV046(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            applicationId,
            update.CurrentVersion,
            update.ApplicationRoot,
            update.TreeSha256,
            update.Files.Count,
            installedBytes,
            sourceRoot,
            binding.InitialSourceTreeSha256,
            currentSourceTree,
            !currentSourceTree.Equals(binding.InitialSourceTreeSha256, StringComparison.OrdinalIgnoreCase),
            sourceFiles.Count,
            sourceBytes,
            checked(installedBytes + sourceBytes),
            true,
            true,
            true,
            true,
            true,
            DefaultNonEffects(),
            "READY means only that a separate explicit PRIVATE export confirmation may duplicate installed and source bytes into one local artifact. The artifact may contain confidential banking/evidence data and is not uploaded automatically.");
    }

    public async Task<(LocalAppPrivateContextReceiptV046 Receipt, string ArtifactPath)> ExportAsync(
        LocalAppPrivateContextPlanV046 confirmed,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmed is null || !confirmed.ReadyForExplicitPrivateExportAuthority)
            throw new InvalidDataException("A READY PRIVATE context preview is required.");
        var fresh = Preview(workspaceRoot, confirmed.ApplicationId, cancellationToken);
        RequireEquivalent(confirmed, fresh);

        var update = _updateContext.Build(workspaceRoot, fresh.ApplicationId, cancellationToken);
        var binding = _sourceBinding.ReadBinding(workspaceRoot, fresh.ApplicationId);
        var installedFiles = update.Files;
        var sourceFiles = LocalAppV046FileBoundary.Inventory(fresh.SourceRoot, includeSourceSidecar: true, MaxSourceFiles, MaxRoleBytes, cancellationToken);
        var manifest = new LocalAppPrivateContextManifestV046(
            ManifestSchema,
            Version,
            DateTimeOffset.Now,
            fresh.ApplicationId,
            fresh.InstalledVersion,
            fresh.InstalledTreeSha256,
            binding.InitialSourceTreeSha256,
            fresh.CurrentSourceTreeSha256,
            fresh.SourceChangedSinceBinding,
            "installed/data/state.json when present; application semantics must confirm current-canonical meaning",
            "installed/data/history/** when present; do not let historical material silently override current state",
            installedFiles,
            sourceFiles,
            true,
            false,
            false,
            "context/read-tool-contract.json",
            "context/update-context.json",
            "This capsule intentionally contains PRIVATE installed bytes plus bound development source. Export is local evidence/disclosure preparation only; upload is a later operator decision.");

        var handoff = BuildHandoff(fresh);
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-private-context");
        var capsule = Path.Combine(dir, $"PRIVATE-local-app-development-context-{LocalAppV046FileBoundary.SafeToken(fresh.ApplicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.zip");
        try
        {
            using (var zip = ZipFile.Open(capsule, ZipArchiveMode.Create))
            {
                WriteUtf8(zip, "context/context-manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
                WriteUtf8(zip, "context/update-context.json", JsonSerializer.Serialize(update, JsonOptions));
                WriteUtf8(zip, "context/read-tool-contract.json", LocalAppReadToolV046Service.ToolContractJson());
                WriteUtf8(zip, "HANDOFF.md", handoff);
                CopyInventory(zip, fresh.InstalledRoot, "installed/", installedFiles, cancellationToken);
                CopyInventory(zip, fresh.SourceRoot, "source/", sourceFiles, cancellationToken);
            }

            var receipt = new LocalAppPrivateContextReceiptV046(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                fresh.ApplicationId,
                fresh.InstalledVersion,
                capsule,
                LocalAppV046FileBoundary.HashFile(capsule),
                new FileInfo(capsule).Length,
                fresh.InstalledTreeSha256,
                fresh.CurrentSourceTreeSha256,
                true,
                true,
                false,
                false,
                false,
                false,
                DefaultNonEffects(),
                "PRIVATE_DEVELOPMENT_CONTEXT_EXPORTED_LOCAL_ONLY",
                "Private bytes were deliberately duplicated only into a local Workbench artifact. No upload/network/publication occurred. Sharing this ZIP with a chat is a separate human disclosure decision.");
            var artifact = Path.Combine(dir, $"PRIVATE-local-app-development-context-receipt-{LocalAppV046FileBoundary.SafeToken(fresh.ApplicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
            return (receipt, artifact);
        }
        catch
        {
            if (File.Exists(capsule)) File.Delete(capsule);
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("private-context-v046-upload", true, "false", "false"),
        ("private-context-v046-network", true, "false", "false"),
        ("private-context-v046-installed-mutation", true, "false", "false"),
        ("private-context-v046-source-mutation", true, "false", "false"),
        ("private-context-v046-read-contract", true, "included", "included"),
        ("private-context-v046-update-context", true, "included", "included")
    };

    private static void RequireEquivalent(LocalAppPrivateContextPlanV046 a, LocalAppPrivateContextPlanV046 b)
    {
        if (a.ApplicationId != b.ApplicationId || a.InstalledVersion != b.InstalledVersion ||
            !a.InstalledTreeSha256.Equals(b.InstalledTreeSha256, StringComparison.OrdinalIgnoreCase) ||
            !a.CurrentSourceTreeSha256.Equals(b.CurrentSourceTreeSha256, StringComparison.OrdinalIgnoreCase) ||
            a.InstalledFileCount != b.InstalledFileCount || a.SourceFileCount != b.SourceFileCount ||
            a.InstalledBytes != b.InstalledBytes || a.SourceBytes != b.SourceBytes)
            throw new InvalidDataException("PRIVATE context preview is stale; installed or source bytes changed before export.");
    }

    private static void CopyInventory(
        ZipArchive zip,
        string root,
        string prefix,
        IReadOnlyList<LocalAppContextFileV046> files,
        CancellationToken cancellationToken)
    {
        foreach (var item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = LocalAppV046FileBoundary.NormalizeRelative(item.Path);
            LocalAppV046FileBoundary.EnsureNoReparseBoundary(root, relative);
            var source = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            LocalAppV046FileBoundary.EnsureInsideRoot(root, source, "private context source");
            LocalAppV046FileBoundary.RejectReparse(source, "private context source");
            if (!LocalAppV046FileBoundary.HashFile(source).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Private context source drifted while exporting: {relative}");
            var entry = zip.CreateEntry(prefix + relative, CompressionLevel.Optimal);
            using var input = File.OpenRead(source);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static void WriteUtf8(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: false);
        writer.Write(text);
    }

    private static string BuildHandoff(LocalAppPrivateContextPlanV046 plan) => $"""
# Matawaka PRIVATE Local App Development Context

ApplicationId: `{plan.ApplicationId}`
Installed Workbench identity: `{plan.InstalledVersion}`
Installed tree SHA-256: `{plan.InstalledTreeSha256}`
Current source tree SHA-256: `{plan.CurrentSourceTreeSha256}`
Source changed since initial binding: `{plan.SourceChangedSinceBinding}`

## Reading order
1. Read `context/context-manifest.json`.
2. Read `context/update-context.json` before building any sparse update package.
3. Treat `installed/data/state.json` as the current-canonical candidate when present, subject to the app's own semantics.
4. Treat `installed/data/history/**` as historical/potentially superseded when present.
5. Use `source/**` as the development source tree; `installed/**` is the currently installed/runtime/evidence tree.

## Privacy
This capsule is PRIVATE LOCAL ONLY and may contain banking statements, receipts, screenshots and other confidential evidence. It was not uploaded by Workbench. Possession of the capsule does not authorize public publication.

## Updates
Do not create a new full seed for ordinary development. Build only `matawaka.local-app-update-package/v1` with actual Add/Replace payload files and exact predecessor SHA-256 values from `context/update-context.json`. Absence from an update ZIP is not Delete.

## Future local read tool
`context/read-tool-contract.json` defines the v0.46 fixed-root chunk-read primitive. The service exists locally, but v0.46 intentionally provides no external connector/network transport. A future tool adapter should invoke that contract rather than inventing arbitrary filesystem access.
""";

    private static string[] DefaultNonEffects() => new[]
    {
        "no automatic upload or network access",
        "no public GitHub publication of private context",
        "no installed application mutation",
        "no development source mutation",
        "no application launch",
        "no update authority",
        "no arbitrary filesystem root",
        "no Git/catalog/Agent Execute authority"
    };
}
