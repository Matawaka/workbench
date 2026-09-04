namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV0521Routing()
    {
        ConfigureV052Routing();
        Title = "Matawaka Workbench v0.52.1";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }
}
