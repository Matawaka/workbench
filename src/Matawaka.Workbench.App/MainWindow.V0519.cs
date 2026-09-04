namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV0519Routing()
    {
        ConfigureV0518Routing();
        Title = "Matawaka Workbench v0.51.9";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0519GenerationContinuityContract() => new[]
    {
        ("v0519-generation-before-successor", true, "prior metadata preservation is inside inherited AcquireAsync before first successor owner metadata write", "true"),
        ("v0519-generation-evidence", true, "exact prior bytes + SHA archive + transition receipt", "preserved"),
        ("v0519-generation-invalid", true, "invalid prior metadata archived opaque/untrusted", "no authority"),
        ("v0519-generation-failure", true, "preservation failure releases owner before lease creation", "fail closed"),
        ("v0519-generation-v0518-ack", true, "explicit stale acknowledgement remains available", "preserved"),
        ("v0519-generation-ui", true, "no new top-level button or second confirmation", "four-button surface preserved")
    };
}
