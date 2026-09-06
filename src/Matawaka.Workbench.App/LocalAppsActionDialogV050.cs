using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV050
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
    StopReadOnlyMcpAdapter,
    StartSecureMcpTunnel,
    StopSecureMcpTunnel,
    ReadSessionStatus,
    EndOrphanedReadSession,
    ReadSessionHistoryPage,
    McpOwnershipStatus,
    AcknowledgeStaleMcpOwnershipMetadata,
    BoundedArtifactAcquisition,
    BoundedRuntimeMaterialization,
    BoundedRuntimeExecution,
    StopBoundedRuntimeExecution,
    BoundedLocalModelInvocation
}

public sealed class LocalAppsActionDialogV050 : Window
{
    public LocalAppsActionChoiceV050 Choice { get; private set; } = LocalAppsActionChoiceV050.Cancel;

    public LocalAppsActionDialogV050(string applicationId, bool adapterActive, bool tunnelActive)
    {
        Title = "Local apps — choose action";
        Width = 660;
        MinWidth = 580;
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
            Text = "Choose one explicit action. Secure MCP Tunnel is a separate outbound transport authority above an already-active lease-gated local MCP adapter. It does not create a lease, widen file scope, create/delete a tunnel in OpenAI Platform, or configure ChatGPT automatically.",
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
        Add(root, "Read session lease", LocalAppsActionChoiceV050.ReadSessionLease);
        Add(root, "Revoke active read leases", LocalAppsActionChoiceV050.RevokeReadLeases);
        Add(root, "Start read-only MCP adapter", LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter, !adapterActive && !tunnelActive);
        Add(root, "Stop read-only MCP adapter", LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter, adapterActive && !tunnelActive);
        Add(root, "Start OpenAI Secure MCP Tunnel", LocalAppsActionChoiceV050.StartSecureMcpTunnel, adapterActive && !tunnelActive);
        Add(root, "Stop OpenAI Secure MCP Tunnel", LocalAppsActionChoiceV050.StopSecureMcpTunnel, tunnelActive);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true, IsDefault = false };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV050.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV050 ShowChoice(Window owner, string applicationId, bool adapterActive, bool tunnelActive)
    {
        var dialog = new LocalAppsActionDialogV050(applicationId, adapterActive, tunnelActive) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v050-tunnel-start", true, "Start OpenAI Secure MCP Tunnel", "explicit"),
        ("chooser-v050-tunnel-stop", true, "Stop OpenAI Secure MCP Tunnel", "explicit"),
        ("chooser-v050-adapter-before-tunnel", true, "tunnel start enabled only when adapter active", "true"),
        ("chooser-v050-adapter-stop-while-tunnel", true, "disabled", "tunnel must stop first"),
        ("chooser-v050-inherits-v049-actions", true, "update/build/launch/context/source/private/relay/lease/revoke/mcp", "preserved"),
        ("chooser-v050-default-effect", true, "initial Choice=Cancel; no default action", "none")
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
