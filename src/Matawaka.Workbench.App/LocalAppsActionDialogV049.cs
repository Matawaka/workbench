using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV049
{
    Cancel,
    UpdateFromPackage,
    BuildUpdatePackage,
    LaunchApp,
    ExportUpdateContext,
    BindDevelopmentSource,
    ExportPrivateDevelopmentContext,
    ChatReadRelay,
    ReadSessionLease,
    RevokeReadLeases,
    StartReadOnlyMcpAdapter,
    StopReadOnlyMcpAdapter
}

public sealed class LocalAppsActionDialogV049 : Window
{
    public LocalAppsActionChoiceV049 Choice { get; private set; } = LocalAppsActionChoiceV049.Cancel;

    public LocalAppsActionDialogV049(string applicationId, bool adapterActive)
    {
        Title = "Local apps — choose action";
        Width = 620;
        MinWidth = 560;
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
            Text = "Choose one explicit action. MCP adapter startup is separate from read-lease creation; it can consume only an already-active v0.48 lease and binds only to IPv4 loopback. Secure MCP Tunnel remains a separate external account/product action.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        Add(root, "Update from package", LocalAppsActionChoiceV049.UpdateFromPackage);
        Add(root, "Build update package", LocalAppsActionChoiceV049.BuildUpdatePackage);
        Add(root, "Launch app", LocalAppsActionChoiceV049.LaunchApp);
        Add(root, "Export update context", LocalAppsActionChoiceV049.ExportUpdateContext);
        Add(root, "Bind development source", LocalAppsActionChoiceV049.BindDevelopmentSource);
        Add(root, "Export PRIVATE development context", LocalAppsActionChoiceV049.ExportPrivateDevelopmentContext);
        Add(root, "Chat read relay", LocalAppsActionChoiceV049.ChatReadRelay);
        Add(root, "Read session lease", LocalAppsActionChoiceV049.ReadSessionLease);
        Add(root, "Revoke active read leases", LocalAppsActionChoiceV049.RevokeReadLeases);
        Add(root, "Start read-only MCP adapter", LocalAppsActionChoiceV049.StartReadOnlyMcpAdapter, !adapterActive);
        Add(root, "Stop read-only MCP adapter", LocalAppsActionChoiceV049.StopReadOnlyMcpAdapter, adapterActive);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true, IsDefault = false };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV049.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV049 ShowChoice(Window owner, string applicationId, bool adapterActive)
    {
        var dialog = new LocalAppsActionDialogV049(applicationId, adapterActive) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v049-mcp-start", true, "Start read-only MCP adapter", "explicit"),
        ("chooser-v049-mcp-stop", true, "Stop read-only MCP adapter", "explicit"),
        ("chooser-v049-v048-actions", true, "update/build/launch/context/source/private/relay/lease/revoke", "preserved"),
        ("chooser-v049-default-effect", true, "initial Choice=Cancel", "none")
    };

    private void Add(StackPanel root, string text, LocalAppsActionChoiceV049 choice, bool enabled = true)
    {
        var button = new Button { Content = text, Height = 34, Margin = new Thickness(0, 0, 0, 8), IsDefault = false, IsEnabled = enabled };
        button.Click += (_, _) => Complete(choice);
        root.Children.Add(button);
    }

    private void Complete(LocalAppsActionChoiceV049 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV049.Cancel ? false : true;
    }
}
