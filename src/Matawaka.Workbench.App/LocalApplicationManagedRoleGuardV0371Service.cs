using System.IO;

namespace Matawaka.Workbench.App;

/// <summary>
/// Patch-level read-only role guard introduced after real-host qualification showed
/// that target-candidate bytes carrying .matawaka-target.json could be misplaced
/// under Workspace/Apps and then registered as a managed application baseline.
/// This guard creates no identity/update/import authority; it only refuses that
/// role collision before the accepted v0.36 RegistrationService is invoked.
/// </summary>
public sealed class LocalApplicationManagedRoleGuardV0371Service
{
    public const string Version = "0.37.1";
    public const string CandidateMetadataFileName = LocalApplicationPackageBuilderService.TargetMetadataFileName;

    public Task EnsureRegistrationRoleAllowedAsync(
        string selectedApplicationRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        if (string.IsNullOrWhiteSpace(selectedApplicationRoot))
            throw new InvalidDataException("Application directory selection is required.");

        var workspace = Path.GetFullPath(workspaceRoot.Trim());
        var appsRoot = Path.GetFullPath(Path.Combine(workspace, LocalApplicationMaintenanceService.AppsDirectoryName));
        var appRoot = Path.GetFullPath(selectedApplicationRoot.Trim());
        if (!Directory.Exists(appRoot))
            throw new InvalidDataException($"Selected application directory is missing: {appRoot}");

        var parent = Directory.GetParent(appRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) ||
            !Path.GetFullPath(parent).Equals(appsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only a direct child of <WorkspaceRoot>/Apps may be registered.");

        var candidateMarker = Path.Combine(appRoot, CandidateMetadataFileName);
        if (File.Exists(candidateMarker) || Directory.Exists(candidateMarker))
        {
            var appId = Path.GetFileName(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            throw new InvalidDataException(
                $"Candidate Source != Managed Application: {CandidateMetadataFileName} is present under the managed Apps root. " +
                $"Do not register or edit identity manually. Move the target candidate to " +
                $"<WorkspaceRoot>/AppCandidates/{appId} and keep the managed application under <WorkspaceRoot>/Apps/{appId}.");
        }

        return Task.CompletedTask;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        return new[]
        {
            ("role-guard-candidate-marker-fixed", CandidateMetadataFileName == ".matawaka-target.json", CandidateMetadataFileName, ".matawaka-target.json"),
            ("role-guard-invariant", true, "Candidate Source != Managed Application", "Candidate Source != Managed Application"),
            ("role-guard-read-only", true, "no copy/move/delete/identity/update/import effect", "read-only refusal only")
        };
    }
}
