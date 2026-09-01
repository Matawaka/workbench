using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// Persists an already-successful package-builder receipt as local evidence only.
/// Before writing JSON it rechecks the surviving generated ZIP, package SHA-256,
/// manifest SHA-256, output root, and the no-mutation/no-update/no-launch flags.
/// </summary>
public sealed class LocalApplicationPackageBuildReceiptStoreV038Service
{
    public const string Version = "0.38.0";
    public const string ReceiptDirectoryName = "local-app-packages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> WriteAsync(
        string workspaceRoot,
        LocalApplicationPackageBuilderReceipt receipt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (receipt is null)
            throw new InvalidDataException("Package-builder receipt is required.");
        if (!string.Equals(receipt.Status, "LOCAL_APPLICATION_UPDATE_PACKAGE_BUILT_EXISTING_UPDATER_PREVIEW_READY", StringComparison.Ordinal) ||
            !receipt.FreshPreviewVerified ||
            !receipt.ExistingUpdaterPreviewReady ||
            receipt.ApplicationMutationPerformed ||
            receipt.UpdateAuthorityCreated ||
            receipt.ApplicationLaunchPerformed)
            throw new InvalidDataException("Only a successful no-mutation/no-update/no-launch package-builder receipt may be persisted.");

        var workspace = RequireDirectory(workspaceRoot, "Workspace root");
        var workbenchRoot = RequireDirectory(Path.Combine(workspace, "Workbench"), "Workbench root");
        var outputDir = Path.GetFullPath(Path.Combine(workbenchRoot, "artifacts", ReceiptDirectoryName));
        Directory.CreateDirectory(outputDir);

        var packagePath = Path.GetFullPath(receipt.PackagePath);
        var outputPrefix = outputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!packagePath.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(packagePath))
            throw new InvalidDataException("Package-builder receipt does not reference a surviving ZIP under Workbench/artifacts/local-app-packages.");
        if (!string.Equals(HashFile(packagePath), receipt.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Generated package SHA-256 no longer matches the package-builder receipt.");

        using (var zip = ZipFile.OpenRead(packagePath))
        {
            var manifestEntry = zip.Entries.SingleOrDefault(entry =>
                entry.FullName.Equals(LocalApplicationMaintenanceService.ManifestFileName, StringComparison.Ordinal));
            if (manifestEntry is null)
                throw new InvalidDataException("Generated package manifest is missing while persisting builder evidence.");
            using var stream = manifestEntry.Open();
            var manifestSha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(manifestSha, receipt.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Generated package manifest SHA-256 no longer matches the package-builder receipt.");
        }

        var safeApp = SafeToken(receipt.ApplicationId);
        var safeTarget = SafeToken(receipt.TargetVersion);
        var receiptPath = Path.Combine(
            outputDir,
            $"local-app-package-build-{safeApp}-{safeTarget}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            receiptPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        var parsed = JsonSerializer.Deserialize<LocalApplicationPackageBuilderReceipt>(
            await File.ReadAllTextAsync(receiptPath, Encoding.UTF8, cancellationToken),
            JsonOptions) ?? throw new InvalidDataException("Persisted package-builder receipt could not be parsed back.");
        if (!string.Equals(parsed.PackageSha256, receipt.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.ManifestSha256, receipt.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            parsed.ApplicationId != receipt.ApplicationId ||
            parsed.CurrentVersion != receipt.CurrentVersion ||
            parsed.TargetVersion != receipt.TargetVersion ||
            parsed.Status != receipt.Status ||
            !parsed.ExistingUpdaterPreviewReady || parsed.ApplicationMutationPerformed || parsed.UpdateAuthorityCreated || parsed.ApplicationLaunchPerformed)
            throw new InvalidDataException("Persisted package-builder receipt round-trip differs from the successful in-memory receipt.");

        return receiptPath;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("builder-receipt-store-fixed-root", true, "Workbench/artifacts/local-app-packages", "fixed local evidence root"),
        ("builder-receipt-store-success-only", true, "ExistingUpdaterPreviewReady=true; mutation/update/launch=false", "success only"),
        ("builder-receipt-store-package-digest-recheck", true, "package SHA-256 + manifest SHA-256", "rechecked before JSON write"),
        ("builder-receipt-store-network-authority", false, "false", "false"),
        ("builder-receipt-store-app-mutation-authority", false, "false", "false")
    };

    private static string SafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Receipt artifact token is required.");
        var safe = new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray());
        if (safe.Length is < 1 or > 80) throw new InvalidDataException("Receipt artifact token is outside bounded length.");
        return safe;
    }

    private static string RequireDirectory(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"{role} is required.");
        var full = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(full)) throw new InvalidDataException($"{role} does not exist: {full}");
        return full;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
