namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV0532Routing()
    {
        ConfigureV053Routing();
        Title = "Matawaka Workbench v0.53.2";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }
}
