using System.Diagnostics;

namespace Matawaka.Workbench.App;

/// <summary>
/// Local-only byte bindings for the already-qualified/human-reviewed v0.60 frontier.
/// These bindings are admission evidence only. They do not reinterpret provenance,
/// model/runtime authority, human review, or an accepted-release identity.
/// </summary>
internal static class V0601QualifiedSourceBindings
{
    internal const string ReviewedV060Head = "3063c739a29e2524a3d45d5003095b515b6ad75f";
    internal const string PublicImplementationMain = "ac083598711caa0c399cc0d2c385b980c083024a";
    internal const string InstalledAcceptedPredecessor = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    internal const string InstalledAcceptedPredecessorTag = "workbench-v0.55.2-accepted";

    private static readonly IReadOnlyDictionary<string, string> ExactGitBlobSha1 =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/Matawaka.Workbench.AgentHost/C2paProvenanceAdmissionService.cs"] = "177235cd564e4fd1b0f6ffb18c27ed3d85893a3b",
            ["src/Matawaka.Workbench.AgentHost/DevelopmentAgentHost.cs"] = "852de740c4ff94e0cffe751efb0abcf96e1a1c4c",
            ["src/Matawaka.Workbench.AgentHost/ProvenanceCapabilityEvidenceComposer.cs"] = "83286837e903d942eb0cc8b22de0f23f43ce1255",
            ["src/Matawaka.Workbench.AgentHost/SemanticProvider.cs"] = "4e8477216e1222608a7f85ff0f79d4573ca79c67",
            ["src/Matawaka.Workbench.Runtime/CommandRouter.cs"] = "08df01117ab5b657f493bcafc706fd568370b0a6",
            ["src/Matawaka.Workbench.Runtime/LiveCapabilityEvidenceAuditV058.cs"] = "e6bc8ffe7295bb56fb3749315c251816aa9633c8",
            ["src/Matawaka.Workbench.App/CapabilityEvidenceReviewV059.cs"] = "678312ffae76706617d0c06d4f5c06f8bc799ac5",
            ["src/Matawaka.Workbench.App/MainWindow.V060.cs"] = "170722c9eb9c1d62d7bf1aa16c6f5a027c4358f3",
            ["src/Matawaka.Workbench.App/MainWindow.xaml"] = "2282883b91c3d5908fd53a1eca73f20266838f4e",
            ["src/Matawaka.Workbench.App/MainWindow.xaml.cs"] = "aa56479de8d69c36af10aa1e33e91c467c246410",
            ["src/Matawaka.Workbench.App/QualifiedV0561EvidenceV0562.cs"] = "383a4c82855dbd3791ec5bf7f8f5a5dc66a195fb",
            ["src/Matawaka.Workbench.App/ProvenanceReviewV0561.cs"] = "1760173f441ae2bd4125288d968b923d063f4774",
            ["src/Matawaka.Workbench.Protocol/CapabilityEvidence.cs"] = "2e7fe730504641bcbdd06c4f71bd2829dc264689",
            ["src/Matawaka.Workbench.Protocol/ProvenanceAdmission.cs"] = "36e1b34ef02fa4c17016b3c5dc27eeec9e7ef38c",
            ["src/Matawaka.Workbench.Protocol/Contracts.cs"] = "ad9bf1de5da3525ca4ceccbfaf988569a8bc4cdf",
            ["src/Matawaka.Workbench.App/BrandingImageResourcesV060.cs"] = "71f54aa1e00942e040c1094cbe6b494e02b5f992",
            ["src/Matawaka.Workbench.App/BrandingReviewWindowV060.cs"] = "cc74d054699f77e89341a69ff36f2926d634fbf3",
            ["src/Matawaka.Workbench.App/BrandingSplashWindowV060.cs"] = "b8b2437a787ce3361bcf249ef99a2647a851f3c0",
            ["src/Matawaka.Workbench.App/MainWindow.V060.Branding.cs"] = "15b92c1318456c25293342de0051c39b305e0c7b",
            ["src/Matawaka.Workbench.App/Assets/Branding/MatawakaWorkbench.ico.b64"] = "f317110446b1188808047858f50afd0bbb58714a",
            ["src/Matawaka.Workbench.App/Assets/Branding/branding-source.v060.json"] = "08dd7de64c57150f30a474c03c932a56cb14f595",
            ["src/Matawaka.Workbench.App/Assets/Branding/splash-v060.b64.001"] = "c5a9b21b85e31a7713a3ed3157d801f1ffcf921a",
            ["src/Matawaka.Workbench.App/Assets/Branding/splash-v060.b64.002"] = "66631925452b1769d2e943d2e71c9ed96f52027e",
            ["src/Matawaka.Workbench.App/Assets/Branding/splash-v060.b64.003"] = "5e3a0e45716b6607c6cbeb16f889312627a5fe25",
            ["src/Matawaka.Workbench.App/Assets/Branding/splash-v060.b64.004"] = "e2294e891885f68e40f5fe05e88fcd9751d0d0f7",
            [".github/qualification/source-bound-model/Probe.csproj"] = "7099afc63264a0bee684788fe50448a78272e616",
            [".github/qualification/source-bound-model/Program.cs"] = "065e8d339acd4d8ee9766f8433e9f5de0c598902",
            [".github/qualification/source-bound-model/translation-fixture.json"] = "3409b9f71c0f47f286fd191994857b8e3c862825",
            ["KONTUR_INTEGRATION_BACKLOG.md"] = "5035aaa63702678f54533bbf98abf2f565e51a10",
            ["integrations/model-invocation/QUALIFICATION.md"] = "606f186a6ecd960cceed18966f28f27472f6ff2f",
            ["integrations/model-invocation/README.md"] = "b4823d0421ec663ad4bb82a49852d3022bedabc5",
            ["integrations/model-invocation/source-binding.schema.json"] = "13b2b78cea39a870546db79c1881bc271224e721",
            ["src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs"] = "14d092d47e6a699405eededad6b577bd4bf8a4da"
        };

    internal static IReadOnlyList<WorkbenchAcceptanceCheck> Run(string workspaceRoot)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var checks = new List<WorkbenchAcceptanceCheck>();

        foreach (var binding in ExactGitBlobSha1.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(repositoryRoot, binding.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                checks.Add(new WorkbenchAcceptanceCheck(
                    "v0601-source-" + Sanitize(binding.Key), false, "missing", binding.Value));
                continue;
            }

            var observed = RunGit(repositoryRoot, "hash-object", "--", binding.Key).Trim();
            checks.Add(new WorkbenchAcceptanceCheck(
                "v0601-source-" + Sanitize(binding.Key),
                string.Equals(observed, binding.Value, StringComparison.OrdinalIgnoreCase),
                observed,
                binding.Value));
        }

        try
        {
            var artwork = BrandingImageResourcesV060.LoadCurrentWorkbenchArtwork();
            checks.Add(new WorkbenchAcceptanceCheck(
                "v0601-neutral-branding-sha256",
                string.Equals(artwork.Sha256, BrandingImageResourcesV060.ExpectedNeutralArtworkSha256, StringComparison.Ordinal),
                artwork.Sha256,
                BrandingImageResourcesV060.ExpectedNeutralArtworkSha256));
            checks.Add(new WorkbenchAcceptanceCheck(
                "v0601-neutral-branding-decode",
                artwork.PixelWidth == 400 && artwork.PixelHeight == 225 && artwork.HasVisibleVariation,
                $"{artwork.PixelWidth}x{artwork.PixelHeight}/range={artwork.LuminanceRange}",
                "400x225/range>=16"));
        }
        catch (Exception ex)
        {
            checks.Add(new WorkbenchAcceptanceCheck(
                "v0601-neutral-branding-decode", false, ex.Message, "exact reviewed neutral branding decodes"));
        }

        return checks;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required for v0.60.1 source admission.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string RunGit(string repositoryRoot, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Failed to start fixed local git source verifier.");
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Fixed local git source verifier timed out.");
        }
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidDataException("Fixed local git source verifier failed: " + stderr.Trim());
        return stdout;
    }

    private static string Sanitize(string value)
        => value.Replace('/', '-').Replace('.', '-').Replace('_', '-').ToLowerInvariant();
}
