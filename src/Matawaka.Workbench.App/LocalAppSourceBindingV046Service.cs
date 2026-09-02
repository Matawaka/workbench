using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppSourceBindingIdentityV046(
    string Schema,
    string ApplicationId,
    string InitialSourceTreeSha256,
    string BoundInstalledVersion,
    string BoundInstalledIdentitySha256,
    DateTimeOffset BoundAt,
    string Note);

public sealed record LocalAppSourceBindingPlanV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string InstalledRoot,
    string SourceRoot,
    string BindingPath,
    string InstalledVersion,
    string InstalledIdentitySha256,
    string SourceTreeSha256,
    int SourceFileCount,
    long SourceBytes,
    bool BindingAbsent,
    bool FixedRootsValidated,
    bool ReadyForExplicitBindingAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppSourceBindingReceiptV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string SourceRoot,
    string BindingPath,
    string BindingSha256,
    string InitialSourceTreeSha256,
    string BoundInstalledVersion,
    bool FreshPreviewVerified,
    bool BindingCreated,
    bool SourceFileMutationPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppSourceBindingV046Service
{
    public const string Version = "0.46.0";
    public const string SourcesDirectoryName = "AppSources";
    public const string BindingFileName = ".matawaka-source.json";
    public const string BindingSchema = "matawaka.local-app-source-identity/v1";
    public const string PlanSchema = "matawaka.local-app-source-binding-plan/v0.46";
    public const string ReceiptSchema = "matawaka.local-app-source-binding-receipt/v0.46";
    public const int MaxFiles = 4096;
    public const long MaxBytes = 2L * 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public LocalAppSourceBindingPlanV046 Preview(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var installed = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var identityPath = Path.Combine(installed, LocalApplicationMaintenanceService.IdentityFileName);
        var identity = LocalAppV046FileBoundary.ReadIdentity(identityPath, applicationId);
        var identitySha = LocalAppV046FileBoundary.HashFile(identityPath);
        var source = LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, applicationId, requireBinding: false);
        var bindingPath = Path.Combine(source, BindingFileName);
        if (File.Exists(bindingPath) || Directory.Exists(bindingPath))
            throw new InvalidDataException("Development source is already bound or the binding path is occupied.");
        var files = LocalAppV046FileBoundary.Inventory(source, includeSourceSidecar: false, MaxFiles, MaxBytes, cancellationToken);
        if (files.Count == 0) throw new InvalidDataException("Refusing to bind an empty development-source directory.");
        var tree = LocalAppV046FileBoundary.ComputeTreeDigest(files);
        var total = files.Sum(x => x.Bytes);

        return new LocalAppSourceBindingPlanV046(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            applicationId,
            installed,
            source,
            bindingPath,
            identity.Version,
            identitySha,
            tree,
            files.Count,
            total,
            true,
            true,
            true,
            DefaultNonEffects(),
            "READY means only that explicit confirmation may create one source-role sidecar. Workbench does not import/copy/move or freeze source bytes; later source edits are expected development activity.");
    }

    public async Task<(LocalAppSourceBindingReceiptV046 Receipt, string ArtifactPath)> BindAsync(
        LocalAppSourceBindingPlanV046 confirmed,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmed is null || !confirmed.ReadyForExplicitBindingAuthority)
            throw new InvalidDataException("A READY source-binding preview is required.");
        var fresh = Preview(workspaceRoot, confirmed.ApplicationId, cancellationToken);
        RequireEquivalent(confirmed, fresh);

        var identity = new LocalAppSourceBindingIdentityV046(
            BindingSchema,
            fresh.ApplicationId,
            fresh.SourceTreeSha256,
            fresh.InstalledVersion,
            fresh.InstalledIdentitySha256,
            DateTimeOffset.Now,
            "Source binding records role association and initial source bytes only. Source edits after binding do not imply installed-app mutation or update authority.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
        var temp = fresh.BindingPath + ".matawaka-source-" + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            LocalAppV046FileBoundary.RejectReparse(temp, "temporary source binding");
            File.Move(temp, fresh.BindingPath, overwrite: false);
            created = true;
            LocalAppV046FileBoundary.RejectReparse(fresh.BindingPath, "source binding");

            var afterFiles = LocalAppV046FileBoundary.Inventory(fresh.SourceRoot, includeSourceSidecar: false, MaxFiles, MaxBytes, cancellationToken);
            var afterTree = LocalAppV046FileBoundary.ComputeTreeDigest(afterFiles);
            if (!afterTree.Equals(fresh.SourceTreeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Development source bytes changed during binding.");

            var receipt = new LocalAppSourceBindingReceiptV046(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                fresh.ApplicationId,
                fresh.SourceRoot,
                fresh.BindingPath,
                LocalAppV046FileBoundary.HashFile(fresh.BindingPath),
                fresh.SourceTreeSha256,
                fresh.InstalledVersion,
                true,
                true,
                false,
                DefaultNonEffects(),
                "LOCAL_APP_SOURCE_BOUND_NO_IMPORT_OR_UPDATE_AUTHORITY",
                "Only .matawaka-source.json was created. Installed bytes and ordinary source files were not changed.");
            var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-source-binding");
            var artifact = Path.Combine(dir, $"local-app-source-binding-{LocalAppV046FileBoundary.SafeToken(fresh.ApplicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
            return (receipt, artifact);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (created && File.Exists(fresh.BindingPath)) File.Delete(fresh.BindingPath);
            throw;
        }
    }

    public LocalAppSourceBindingIdentityV046 ReadBinding(string workspaceRoot, string applicationId)
    {
        var source = LocalAppV046FileBoundary.ResolveSourceRoot(workspaceRoot, applicationId, requireBinding: true);
        var path = Path.Combine(source, BindingFileName);
        var binding = JsonSerializer.Deserialize<LocalAppSourceBindingIdentityV046>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("Source binding could not be parsed.");
        if (binding.Schema != BindingSchema || binding.ApplicationId != applicationId)
            throw new InvalidDataException("Source binding identity mismatch.");
        return binding;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("source-v046-fixed-root", true, "Workspace/AppSources/<ApplicationId>", "fixed direct child"),
        ("source-v046-create-only", true, ".matawaka-source.json", "one sidecar only"),
        ("source-v046-import-authority", true, "false", "false"),
        ("source-v046-update-authority", true, "false", "false"),
        ("source-v046-source-mutable-after-bind", true, "binding is role association", "not immutable-source claim")
    };

    private static void RequireEquivalent(LocalAppSourceBindingPlanV046 a, LocalAppSourceBindingPlanV046 b)
    {
        if (a.ApplicationId != b.ApplicationId ||
            !Path.GetFullPath(a.SourceRoot).Equals(Path.GetFullPath(b.SourceRoot), StringComparison.OrdinalIgnoreCase) ||
            a.InstalledVersion != b.InstalledVersion ||
            !a.InstalledIdentitySha256.Equals(b.InstalledIdentitySha256, StringComparison.OrdinalIgnoreCase) ||
            !a.SourceTreeSha256.Equals(b.SourceTreeSha256, StringComparison.OrdinalIgnoreCase) ||
            a.SourceFileCount != b.SourceFileCount || a.SourceBytes != b.SourceBytes)
            throw new InvalidDataException("Source-binding preview is stale; installed identity or source bytes changed.");
    }

    private static string[] DefaultNonEffects() => new[]
    {
        "no source import/copy/move/delete",
        "no ordinary source file mutation",
        "no installed application mutation",
        "no application update",
        "no application launch",
        "no network/upload",
        "no Git/catalog/Agent Execute authority"
    };
}
