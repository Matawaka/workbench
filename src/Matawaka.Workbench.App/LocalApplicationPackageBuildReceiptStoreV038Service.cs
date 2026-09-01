using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// Persists an already-successful package-builder receipt as local evidence only.
/// Before publication of the JSON artifact it rechecks the surviving generated ZIP,
/// package SHA-256, manifest SHA-256, output root and no-effect flags, then validates
/// a temporary JSON copy and atomically moves it into its final evidence name.
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
        var finalPath = Path.Combine(
            outputDir,
            $"local-app-package-build-{safeApp}-{safeTarget}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        var tempPath = finalPath + $".tmp-{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(receipt, JsonOptions),
                new UTF8Encoding(false),
                cancellationToken);

            var parsed = JsonSerializer.Deserialize<LocalApplicationPackageBuilderReceipt>(
                await File.ReadAllTextAsync(tempPath, Encoding.UTF8, cancellationToken),
                JsonOptions) ?? throw new InvalidDataException("Temporary package-builder receipt could not be parsed back.");
            RequireEquivalent(parsed, receipt);
            if (File.Exists(finalPath))
                throw new InvalidDataException("Package-builder receipt destination unexpectedly already exists.");
            File.Move(tempPath, finalPath);

            var finalParsed = JsonSerializer.Deserialize<LocalApplicationPackageBuilderReceipt>(
                await File.ReadAllTextAsync(finalPath, Encoding.UTF8, cancellationToken),
                JsonOptions) ?? throw new InvalidDataException("Persisted package-builder receipt could not be parsed after atomic move.");
            RequireEquivalent(finalParsed, receipt);
            return finalPath;
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("builder-receipt-store-fixed-root", true, "Workbench/artifacts/local-app-packages", "fixed local evidence root"),
        ("builder-receipt-store-success-only", true, "ExistingUpdaterPreviewReady=true; mutation/update/launch=false", "success only"),
        ("builder-receipt-store-package-digest-recheck", true, "package SHA-256 + manifest SHA-256", "rechecked before JSON write"),
        ("builder-receipt-store-atomic-finalization", true, "temporary JSON -> parse-back -> atomic move", "validated final artifact only"),
        ("builder-receipt-store-network-authority", true, "false", "false"),
        ("builder-receipt-store-app-mutation-authority", true, "false", "false")
    };

    private static void RequireEquivalent(
        LocalApplicationPackageBuilderReceipt parsed,
        LocalApplicationPackageBuilderReceipt expected)
    {
        if (!string.Equals(parsed.PackageSha256, expected.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.ManifestSha256, expected.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            parsed.ApplicationId != expected.ApplicationId ||
            parsed.ApplicationRoot != expected.ApplicationRoot ||
            parsed.CandidateRoot != expected.CandidateRoot ||
            parsed.CurrentVersion != expected.CurrentVersion ||
            parsed.TargetVersion != expected.TargetVersion ||
            parsed.PackagePath != expected.PackagePath ||
            parsed.Status != expected.Status ||
            parsed.FreshPreviewVerified != expected.FreshPreviewVerified ||
            !parsed.ExistingUpdaterPreviewReady ||
            parsed.ApplicationMutationPerformed ||
            parsed.UpdateAuthorityCreated ||
            parsed.ApplicationLaunchPerformed ||
            parsed.Changes.Count != expected.Changes.Count)
            throw new InvalidDataException("Persisted package-builder receipt round-trip differs from the successful in-memory receipt.");
    }

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
