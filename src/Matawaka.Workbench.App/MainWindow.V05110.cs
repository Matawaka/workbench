namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV05110Routing()
    {
        ConfigureV0519Routing();
        Title = "Matawaka Workbench v0.51.10";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV05110GenerationTransactionContract() => new[]
    {
        ("v05110-prepared-not-committed", true, "PREPARED transaction explicitly distinct from successor owner metadata COMMITTED", "true"),
        ("v05110-order", true, "reconcile -> preserve/reuse -> PREPARED -> successor owner write -> COMMITTED before owner is returned to lease flow", "true"),
        ("v05110-abandoned", true, "prior bytes still active closes PREPARED as ABANDONED_BEFORE_SUCCESSOR", "verified archive reusable"),
        ("v05110-recovered", true, "exact recorded successor metadata closes PREPARED as COMMITTED_RECOVERED", "owner materialization only"),
        ("v05110-dedupe", true, "new v0.51.9 archives are content-addressed by exact SHA-256", "retry does not duplicate prior bytes"),
        ("v05110-fail-closed", true, "transaction/archive/metadata inconsistency refuses before lease/listener authority", "true"),
        ("v05110-authority", true, "transaction evidence grants no lease/read/revoke/resume authority", "false"),
        ("v05110-ui", true, "no new top-level button or confirmation", "four-button surface preserved")
    };
}
