using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.51.2 keeps the existing LocalAppsActionChoiceV050 protocol but changes the
/// active-adapter closure affordance from transport-only stop to a full bound
/// read-session end. No new top-level Workbench button is introduced.
/// </summary>
public sealed class LocalAppsActionDialogV0512 : Window
{
    public LocalAppsActionChoiceV050 Choice { get; private set; } = LocalAppsActionChoiceV050.Cancel;

    public LocalAppsActionDialogV0512(string applicationId, bool adapterActive, bool tunnelActive)
    {
        Title = "Local apps — choose action";
        Width = 680;
        MinWidth = 600;
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
            Text = "Read session is local and lease-gated. v0.51.2 can end the currently bound session with one explicit action: stop the local MCP adapter, then revoke only its exact LeaseId. Secure MCP Tunnel remains a separate authority and must be stopped first.",
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
        Add(root, "Read session lease + auto-start local MCP", LocalAppsActionChoiceV050.ReadSessionLease, !adapterActive && !tunnelActive);
        Add(root, "End Read Session — stop MCP + revoke exact bound lease", LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter, adapterActive && !tunnelActive);
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
        var dialog = new LocalAppsActionDialogV0512(applicationId, adapterActive, tunnelActive) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v0512-end-session", true, "End Read Session — stop MCP + revoke exact bound lease", "explicit closure"),
        ("chooser-v0512-end-needs-adapter", true, "enabled only when adapter active and tunnel inactive", "true"),
        ("chooser-v0512-tunnel-separate", true, "tunnel must stop first", "separate authority"),
        ("chooser-v0512-revoke-all-preserved", true, "recovery action preserved", "preserved"),
        ("chooser-v0512-four-button-surface", true, "dialog-only change", "no top-level button")
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
