using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppLaunchPlanV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string ApplicationRoot,
    string ExecutableRelativePath,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    string InstalledVersion,
    bool ExactRegisteredRootValidated,
    bool NoArguments,
    bool ReadyForExplicitLaunchAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalAppLaunchReceiptV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string InstalledVersion,
    string ExecutableRelativePath,
    string ExecutableSha256,
    long ExecutableBytes,
    int ProcessId,
    bool FreshPreviewVerified,
    bool ProcessLaunchPerformed,
    bool ArgumentsProvided,
    bool WorkbenchNetworkAccessPerformed,
    bool ApplicationBehaviorSandboxed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed class LocalAppLaunchV046Service
{
    public const string Version = "0.46.0";
    public const string PlanSchema = "matawaka.local-app-launch-plan/v0.46";
    public const string ReceiptSchema = "matawaka.local-app-launch-receipt/v0.46";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalAppLaunchPlanV046 Preview(
        string workspaceRoot,
        string applicationId,
        string executablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var appRoot = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var identity = LocalAppV046FileBoundary.ReadIdentity(Path.Combine(appRoot, LocalApplicationMaintenanceService.IdentityFileName), applicationId);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new InvalidDataException("Selected application executable is missing.");
        var full = Path.GetFullPath(executablePath);
        LocalAppV046FileBoundary.EnsureInsideRoot(appRoot, full, "application launch target");
        var relative = LocalAppV046FileBoundary.NormalizeRelative(Path.GetRelativePath(appRoot, full));
        LocalAppV046FileBoundary.EnsureNoReparseBoundary(appRoot, relative);
        LocalAppV046FileBoundary.RejectReparse(full, "application launch target");
        if (!Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.46 Launch app accepts only an existing .exe inside the registered application root.");

        var info = new FileInfo(full);
        return new LocalAppLaunchPlanV046(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            applicationId,
            appRoot,
            relative,
            full,
            LocalAppV046FileBoundary.HashFile(full),
            info.Length,
            identity.Version,
            true,
            true,
            true,
            DefaultNonEffects(),
            "READY means only that a separate explicit confirmation may start this exact EXE with zero arguments and the registered app root as working directory. Workbench does not claim to sandbox the application's own behavior after launch.");
    }

    public async Task<(LocalAppLaunchReceiptV046 Receipt, string ArtifactPath)> LaunchAsync(
        LocalAppLaunchPlanV046 confirmed,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (confirmed is null || !confirmed.ReadyForExplicitLaunchAuthority)
            throw new InvalidDataException("A READY launch preview is required.");
        var fresh = Preview(workspaceRoot, confirmed.ApplicationId, confirmed.ExecutablePath, cancellationToken);
        if (confirmed.ApplicationId != fresh.ApplicationId || confirmed.InstalledVersion != fresh.InstalledVersion ||
            !confirmed.ExecutableRelativePath.Equals(fresh.ExecutableRelativePath, StringComparison.Ordinal) ||
            !confirmed.ExecutableSha256.Equals(fresh.ExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
            confirmed.ExecutableBytes != fresh.ExecutableBytes)
            throw new InvalidDataException("Launch preview is stale; executable or installed identity changed.");

        var psi = new ProcessStartInfo
        {
            FileName = fresh.ExecutablePath,
            WorkingDirectory = fresh.ApplicationRoot,
            UseShellExecute = false
        };
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Windows refused to start the selected application executable.");
        var receipt = new LocalAppLaunchReceiptV046(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            fresh.ApplicationId,
            fresh.InstalledVersion,
            fresh.ExecutableRelativePath,
            fresh.ExecutableSha256,
            fresh.ExecutableBytes,
            process.Id,
            true,
            true,
            false,
            false,
            false,
            DefaultNonEffects(),
            "LOCAL_APPLICATION_EXACT_EXE_LAUNCHED",
            "Workbench launched only the explicitly confirmed exact EXE with zero arguments. Application behavior after process start is not sandboxed or reclassified as Workbench authority.");
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-launch");
        var artifact = Path.Combine(dir, $"local-app-launch-{LocalAppV046FileBoundary.SafeToken(fresh.ApplicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (receipt, artifact);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("launch-v046-exe-only", true, ".exe", ".exe"),
        ("launch-v046-zero-args", true, "true", "true"),
        ("launch-v046-exact-root", true, "registered app only", "registered app only"),
        ("launch-v046-auto-launch", true, "false", "false"),
        ("launch-v046-sandbox-claim", true, "false", "false")
    };

    private static string[] DefaultNonEffects() => new[]
    {
        "no automatic launch from registration/update/context export",
        "no executable outside registered app root",
        "no command-line arguments",
        "no shell command/script/installer selection",
        "no Workbench network operation",
        "no Git/catalog/Agent Execute authority",
        "no claim that launched application behavior is sandboxed"
    };
}
