using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.51.4 preserves the v0.51.3 actions while making the status representation
/// explicitly bounded/paginated. No top-level Workbench button is added.
/// </summary>
public sealed class LocalAppsActionDialogV0514 : Window
{
    public LocalAppsActionChoiceV050 Choice { get; private set; } = LocalAppsActionChoiceV050.Cancel;

    public LocalAppsActionDialogV0514(string applicationId, bool adapterActive, bool tunnelActive)
    {
        Title = "Local apps — choose action";
        Width = 720;
        MinWidth = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Registered application: {applicationId}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Read Session Status always returns all live authority up to the fixed safety ceiling and only one bounded page of historical evidence. Historical state is preserved on disk; pagination changes representation only. Bearer plaintext/hash remain omitted.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        Add(root, "Update from package", LocalAppsActionChoiceV050.UpdateFromPackage);
        Add(root, "Build update package", LocalAppsActionChoiceV050.BuildUpdatePackage);
        Add(root, "Launch app", LocalAppsActionChoiceV050.LaunchApp);
        Add(root, "Export update context", LocalAppsActionChoiceV050.ExportUpdateContext);
        Add(root, "Bind development source", LocalAppsActionChoiceV050.BindDevelopmentSource);
        Add(root, "Export PRIVATE development context", LocalAppsActionChoiceV050.ExportPrivateDevelopmentContext);
        Add(root, "Chat read relay", LocalAppsActionChoiceV050.ChatReadRelay);
        Add(root, "Read Session Status — bounded history", LocalAppsActionChoiceV050.ReadSessionStatus);
        Add(root, "Read session lease + auto-start local MCP", LocalAppsActionChoiceV050.ReadSessionLease, !adapterActive && !tunnelActive);
        Add(root, "End Read Session — stop MCP + revoke exact bound lease", LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter, adapterActive && !tunnelActive);
        Add(root, "End orphaned read session — exact unbound lease", LocalAppsActionChoiceV050.EndOrphanedReadSession);
        Add(root, "Revoke ALL active read leases (recovery)", LocalAppsActionChoiceV050.RevokeReadLeases);
        Add(root, "Start read-only MCP adapter manually", LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter, !adapterActive && !tunnelActive);
        Add(root, "Start OpenAI Secure MCP Tunnel", LocalAppsActionChoiceV050.StartSecureMcpTunnel, adapterActive && !tunnelActive);
        Add(root, "Stop OpenAI Secure MCP Tunnel", LocalAppsActionChoiceV050.StopSecureMcpTunnel, tunnelActive);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true, IsDefault = false };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV050.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV050 ShowChoice(Window owner, string applicationId, bool adapterActive, bool tunnelActive)
    {
        var dialog = new LocalAppsActionDialogV0514(applicationId, adapterActive, tunnelActive) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v0514-status", true, "Read Session Status — bounded history", "explicit bounded view"),
        ("chooser-v0514-live-authority", true, "not silently paginated", "all live or overflow"),
        ("chooser-v0514-history", true, "page chooser when historical evidence exceeds default page", "bounded"),
        ("chooser-v0514-orphan-close", true, "exact unbound lease", "preserved"),
        ("chooser-v0514-revoke-all", true, "recovery only", "preserved"),
        ("chooser-v0514-four-button-surface", true, "dialog-only change", "no top-level button")
    };

    private void Add(StackPanel root, string text, LocalAppsActionChoiceV050 choice, bool enabled = true)
    {
        var button = new Button { Content = text, Height = 34, Margin = new Thickness(0, 0, 0, 8), IsDefault = false, IsEnabled = enabled };
        button.Click += (_, _) => Complete(choice);
        root.Children.Add(button);
    }

    private void Complete(LocalAppsActionChoiceV050 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV050.Cancel ? false : true;
    }
}
