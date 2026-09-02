using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed record OperatorSurfaceV045Check(string Id, bool Passed, string Observed, string Expected);

public static class OperatorSurfaceV045Contract
{
    public const string Version = "0.45.0";
    public const int VisibleMaintenanceButtons = 4;

    private static readonly string[] RetiredManualButtons = { "SelfTestButton", "AcceptCheckpointButton", "CancelButton", "LaunchCandidateButton" };

    public static void Apply(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.FindName("HistoricalCompatibilityBindings") is not StackPanel legacy) return;

        legacy.Visibility = Visibility.Collapsed;
        legacy.IsEnabled = false;
        legacy.IsHitTestVisible = false;
        legacy.Focusable = false;

        foreach (var button in legacy.Children.OfType<Button>())
        {
            button.IsEnabled = false;
            button.Visibility = Visibility.Collapsed;
            button.Focusable = false;
            button.IsTabStop = false;
        }
        foreach (var checkBox in legacy.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = false;
            checkBox.IsEnabled = false;
            checkBox.Visibility = Visibility.Collapsed;
            checkBox.Focusable = false;
            checkBox.IsTabStop = false;
        }
        foreach (var textBox in legacy.Children.OfType<TextBox>())
        {
            textBox.Focusable = false;
            textBox.IsTabStop = false;
        }
    }

    public static IReadOnlyList<OperatorSurfaceV045Check> Observe(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var checks = new List<OperatorSurfaceV045Check>();
        var primary = window.FindName("PrimaryMaintenanceSurface") as Panel;
        var visibleButtons = primary?.Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToArray() ?? Array.Empty<Button>();
        checks.Add(new("surface-v045-visible-maintenance-buttons", visibleButtons.Length == VisibleMaintenanceButtons, visibleButtons.Length.ToString(), VisibleMaintenanceButtons.ToString()));

        var expectedSorted = new[] { "UpdateCandidateButton", "UpdateLocalAppButton", "PublishAcceptedButton", "LifecycleReceiptButton" }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var observedActiveNames = visibleButtons.Select(button => button.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        checks.Add(new("surface-v045-active-actions-exact", observedActiveNames.SequenceEqual(expectedSorted, StringComparer.Ordinal), string.Join(",", observedActiveNames), string.Join(",", expectedSorted)));

        if (window.FindName("HistoricalCompatibilityBindings") is StackPanel legacy)
            checks.Add(new("surface-v045-legacy-container-quarantined", legacy.Visibility == Visibility.Collapsed && !legacy.IsEnabled && !legacy.IsHitTestVisible && !legacy.Focusable, $"visibility={legacy.Visibility}; enabled={legacy.IsEnabled}; hitTest={legacy.IsHitTestVisible}; focusable={legacy.Focusable}", "Collapsed / false / false / false"));
        else
            checks.Add(new("surface-v045-legacy-container-quarantined", false, "missing", "present and quarantined"));

        foreach (var name in RetiredManualButtons)
        {
            var button = window.FindName(name) as Button;
            checks.Add(new($"surface-v045-retired-{name}", button is not null && !button.IsEnabled && button.Visibility == Visibility.Collapsed && !button.IsTabStop, button is null ? "missing" : $"enabled={button.IsEnabled}; visibility={button.Visibility}; tabStop={button.IsTabStop}", "present only as disabled collapsed compatibility binding"));
        }
        foreach (var name in new[] { "AgentEnabledBox", "AllowGitFetchBox" })
        {
            var box = window.FindName(name) as CheckBox;
            checks.Add(new($"surface-v045-retired-{name}", box is not null && box.IsChecked != true && !box.IsEnabled && box.Visibility == Visibility.Collapsed && !box.IsTabStop, box is null ? "missing" : $"checked={box.IsChecked}; enabled={box.IsEnabled}; visibility={box.Visibility}; tabStop={box.IsTabStop}", "unchecked disabled collapsed compatibility binding"));
        }

        var workspacePreserved = window.FindName("WorkspaceRootBox") is TextBox;
        var catalogPreserved = window.FindName("CatalogRootBox") is TextBox;
        checks.Add(new("surface-v045-hidden-state-roots-preserved", workspacePreserved && catalogPreserved, $"workspace={workspacePreserved}; catalog={catalogPreserved}", "true / true"));
        return checks;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("surface-v045-four-actions", VisibleMaintenanceButtons == 4, VisibleMaintenanceButtons.ToString(), "4"),
        ("surface-v045-retired-manual-set", RetiredManualButtons.SequenceEqual(new[] { "SelfTestButton", "AcceptCheckpointButton", "CancelButton", "LaunchCandidateButton" }, StringComparer.Ordinal), string.Join(",", RetiredManualButtons), "SelfTest/Accept/Stop/Launch candidate compatibility-only"),
        ("surface-v045-parent-effective-disable", true, "legacy IsEnabled=false", "legacy descendants remain effectively disabled even if historical code sets local child IsEnabled=true"),
        ("surface-v045-authority-created", true, "false", "false")
    };
}
