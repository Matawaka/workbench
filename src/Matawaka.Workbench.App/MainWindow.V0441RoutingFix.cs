using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private bool _v0441RoutingInstalled;

    /// <summary>
    /// Installs only the v0.44.1 real-host routing stabilization on top of the
    /// otherwise-qualified v0.44 inspection surface. Release/checkpoint routing
    /// remains intentionally separate until the exact local v0.44 predecessor
    /// commit is bound for the final stabilization package.
    /// </summary>
    internal void InstallV0441TreeDoubleClickRouting()
    {
        if (_v0441RoutingInstalled) return;
        _v0441RoutingInstalled = true;

        OutputTabs.SelectionChanged += OutputTabsV0441_SelectionChanged;
        InstalledAppsList.AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(InstalledAppsV0441_RoutedClick),
            handledEventsToo: true);

        RewireSelectedAppTreeV0441();
    }

    private void OutputTabsV0441_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, OutputTabs)) return;
        RewireSelectedAppTreeV0441();
    }

    private void InstalledAppsV0441_RoutedClick(object sender, RoutedEventArgs e)
    {
        // v0.44's button handler creates/refreshes the tree synchronously before
        // this routed parent handler runs. Queue one context-idle rewire so a
        // refresh of an already-selected app tab is covered as well.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(RewireSelectedAppTreeV0441));
    }

    private void RewireSelectedAppTreeV0441()
    {
        if (OutputTabs.SelectedItem is not TabItem { Content: Grid grid }) return;
        var tree = grid.Children.OfType<TreeView>().FirstOrDefault();
        if (tree is null || tree.Tag is not string) return;

        tree.MouseDoubleClick -= AppTreeV044_MouseDoubleClick;
        tree.MouseDoubleClick -= AppTreeV0441_MouseDoubleClick;
        tree.MouseDoubleClick += AppTreeV0441_MouseDoubleClick;
    }

    private void AppTreeV0441_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView { Tag: string applicationId } tree) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (!TreeViewItemRoutingV0441Service.TryResolveFileNode(tree, source, out var node) || node is null)
            return;

        // Directory double-click remains untouched. A valid file node consumes
        // the interaction only after exact nested-item resolution succeeds.
        e.Handled = true;
        try
        {
            OpenOrRefreshAppTextTabV044(applicationId, node.RelativePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"WARNING: text file unavailable: {applicationId}/{node.RelativePath}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-text.v0441.warning        app={applicationId}; path={node.RelativePath}; {ex.Message}");
        }
    }
}
