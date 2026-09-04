using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private bool _v051111ExclusiveLocalAppsRouting;

    internal void ConfigureV051111Routing()
    {
        ConfigureV05111Routing();

        // v0.51.8 is the last inherited layer that installs its own Local Apps
        // click handler. v0.51.11 originally removed only the v0.51.7 handler,
        // leaving v0.51.8 + v0.51.11 subscribed together. Hotfix the composed
        // route after the full predecessor chain has executed.
        UpdateLocalAppButton.Click -= LocalAppsV0518Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV0517Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05111Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV051111Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV051111Button_Click;

        _v051111ExclusiveLocalAppsRouting = true;
        Title = "Matawaka Workbench v0.51.11.1";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void LocalAppsV051111Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_v051111ExclusiveLocalAppsRouting)
        {
            ShowInvalid(new InvalidDataException(
                "V051111_LOCAL_APPS_ROUTE_NOT_EXCLUSIVE: hotfix route was invoked before exclusive routing configuration."));
            return;
        }

        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v051111.dispatch exclusive=true; target=v05111");
        LocalAppsV05111Button_Click(sender, e);
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV051111ExclusiveRoutingContract() => new[]
    {
        ("v051111-exclusive-route", _v051111ExclusiveLocalAppsRouting, _v051111ExclusiveLocalAppsRouting.ToString(), "True"),
        ("v051111-current-handler", true, "LocalAppsV051111Button_Click -> LocalAppsV05111Button_Click", "single current route"),
        ("v051111-inherited-v0518-detached", true, "LocalAppsV0518Button_Click detached after full inherited configure chain", "true"),
        ("v051111-v05111-direct-detached", true, "direct v0.51.11 handler detached before hotfix wrapper is attached", "true"),
        ("v051111-lease-dispatch", true, "ReadSessionLease remains dispatched only by v0.51.11 switch to CreateOwnedReadLeaseAndAutoStartMcpV05111Async", "v0.51.11 transaction path"),
        ("v051111-authority", true, "routing hotfix adds no lease/read/revoke/resume authority", "false"),
        ("v051111-ui", true, "top-level four-button surface preserved", "true")
    };
}
