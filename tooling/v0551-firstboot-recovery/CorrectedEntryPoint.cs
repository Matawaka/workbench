using System.Text;

namespace Matawaka.Workbench.V0551Recovery;

internal static class CorrectedEntryPoint
{
    private const string Confirmation = "RESTORE-EXACT-V055";
    private const string CorrectFailedCandidateSha256 = "d836d45c823e8388cb252214af176f2a1b315f2da8bd3d9418ddd74b8c1fea7e";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                RequireCanonicalSha256(CorrectFailedCandidateSha256, nameof(CorrectFailedCandidateSha256));
                return RecoverySelfTest.Run();
            }

            var requestPath = args.Length == 0
                ? Path.Combine(AppContext.BaseDirectory, "recovery-request.json")
                : Path.GetFullPath(args[0]);
            var request = RecoveryRequestParser.ParseExact(File.ReadAllText(requestPath, Encoding.UTF8));
            var policy = ProductionPolicy();
            RequireCanonicalSha256(policy.FailedCandidateSha256, nameof(policy.FailedCandidateSha256));
            var engine = new RecoveryEngine(policy);
            var preview = engine.Preview(request);

            Console.WriteLine("Matawaka Workbench v0.55.1 fail-closed source recovery v2");
            Console.WriteLine();
            Console.WriteLine($"Status: {preview.Status}");
            Console.WriteLine($"Repository: {preview.RepositoryRoot}");
            Console.WriteLine($"HEAD/tag: {preview.Head} / {preview.AcceptedTag}");
            Console.WriteLine($"Failed lease: {preview.FailedLeaseId} / {preview.FailedLeaseSha256}");
            Console.WriteLine($"Failed candidate SHA-256: {preview.FailedCandidateSha256}");
            Console.WriteLine($"Exact dirty paths: {preview.DirtyPaths.Count}");
            Console.WriteLine($"Accepted executable: {preview.AcceptedExecutableSha256}");
            Console.WriteLine();
            Console.WriteLine("No source/Git/network/process effect has occurred during Preview.");
            Console.WriteLine("Recovery will only restore the exact seven failed-v0.55.1 source paths to accepted v0.55 and then require git status clean.");
            Console.WriteLine();
            Console.Write($"Type {Confirmation} exactly to authorize this one local recovery: ");
            var typed = Console.ReadLine();
            if (!string.Equals(typed, Confirmation, StringComparison.Ordinal))
            {
                Console.WriteLine("CANCELLED_NO_EFFECT");
                Pause();
                return 2;
            }

            var receipt = engine.Apply(request, preview);
            Console.WriteLine();
            Console.WriteLine($"COMPLETED: {receipt.Status}");
            Console.WriteLine($"HEAD: {receipt.HeadAfter}");
            Console.WriteLine($"git status clean: {receipt.WorkingTreeCleanAfterRecovery}");
            Console.WriteLine($"Receipt: {engine.LastReceiptPath}");
            Pause();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RECOVERY_REFUSED_OR_FAILED: " + ex.Message);
            Pause();
            return 1;
        }
    }

    private static void Pause()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            _ = Console.ReadLine();
        }
    }

    private static void RequireCanonicalSha256(string value, string field)
    {
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"{field} is not canonical 64-hex SHA-256 evidence.");
    }

    private static RecoveryPolicy ProductionPolicy()
    {
        var acceptedApp = Encoding.UTF8.GetBytes(
            "using System.Windows;\n\n" +
            "namespace Matawaka.Workbench.App;\n\n" +
            "public partial class App : Application\n" +
            "{\n" +
            "    protected override void OnStartup(StartupEventArgs e)\n" +
            "    {\n" +
            "        base.OnStartup(e);\n" +
            "        var window = new MainWindow();\n" +
            "        window.ConfigureV055Routing();\n" +
            "        window.ConfigureV055AcceptanceRouting();\n" +
            "        MainWindow = window;\n" +
            "        window.Show();\n" +
            "    }\n" +
            "}\n\n" +
            "internal static class V048StringCompatibilityExtensions\n" +
            "{\n" +
            "    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)\n" +
            "        => value.EndsWith(suffix.ToString(), comparisonType);\n" +
            "}\n");

        return new RecoveryPolicy(
            "matawaka.workbench-v0551-failed-firstboot-recovery-request/v0.1",
            "recover-v0551-title-harness-failure-50b6af3831c84032bbcd51d5b03dc7eb-v2",
            @"K:\Matawaka\Workbench",
            "02d81b8559bc7c9676949be0557d20ecb50a9890",
            "workbench-v0.55-accepted",
            "0.55.1",
            "workbench-v0.55.1-accepted",
            "50b6af3831c84032bbcd51d5b03dc7eb",
            @"K:\Matawaka\Workbench\artifacts\transition-bootstrap\transition-bootstrap-v0.40-50b6af3831c84032bbcd51d5b03dc7eb.json",
            CorrectFailedCandidateSha256,
            @"K:\Matawaka\Workbench\artifacts\app-v0.55-gui-update\Matawaka.Workbench.App.exe",
            "eac74afec61019095ef07649704e70e3a63cb289b3f2e86fec7a0fe4723b3872",
            acceptedApp,
            "1bdd6c83818ae5134ddbf90264c55bb3515124976ddd82c71c4e6c6681ab1655",
            new[]
            {
                new DirtyPathPolicy("PATCH-v0.55.1.md", "fd19d63ad19e1fb2849eb4e4e45b85e4ea99a61858856f33e7bb2822985ca33d", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/App.xaml.cs", "e820e987d02f50553dd964dde64f9d67e4bf525a9519f910133e4526fd9ab4d2", "RestoreAccepted"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/RealHostModelInvocationAdmissionV0551.cs", "dd794caf869a0c057881af82ab5d519c687f3b77736487d78c9c6d6f7579d8ae", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/FixedGitHubPublicationV0551Service.cs", "735541583a4f83e478e0da0abd08fb0661dc6bf3fa415fd63ec94984f55fa0e3", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/LocalCheckpointV0551Service.cs", "e5f9437ac8ce47abb6a66574d1cbd6e525fa95469840924cbbd5ae46beb26824", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/MainWindow.V0551.Acceptance.cs", "c20e20663ee49d641d875778aea14ee27ed77c3d9a7c03bd3c310752b8fa4298", "RemoveAdded"),
                new DirtyPathPolicy("src/Matawaka.Workbench.App/WorkbenchV0551AcceptanceHarness.cs", "f4d5ab6d5de618216e4fe8051df00e4206d2b80cd80c18fc77e50f85fe145a4e", "RemoveAdded")
            });
    }
}
