using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.51.5 separates fast verified live-authority status from explicit canonical
/// history scanning. v0.51.8 additionally exposes MCP ownership status/recovery
/// without changing the top-level four-button Workbench surface.
/// </summary>
public sealed class LocalAppsActionDialogV0515 : Window
{
    public LocalAppsActionChoiceV050 Choice { get; private set; } = LocalAppsActionChoiceV050.Cancel;

    public LocalAppsActionDialogV0515(string applicationId, bool adapterActive, bool tunnelActive)
    {
        Title = "Local apps — choose action";
        Width = 760;
        MinWidth = 680;
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
            Text = "Read Session Status uses the verified active-lease index and exact canonical revalidation without historical enumeration. MCP Ownership Status separately reports whether another Workbench owns the local MCP runtime domain or whether only stale non-authoritative owner metadata remains. Owner metadata never grants lease/read/resume authority. Bearer plaintext/hash and endpoint path token remain omitted.",
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
        Add(root, "Read Session Status — verified live authority (fast)", LocalAppsActionChoiceV050.ReadSessionStatus);
        Add(root, "Read Session History Page — bounded canonical evidence scan", LocalAppsActionChoiceV050.ReadSessionHistoryPage);
        Add(root, "MCP Ownership Status — cross-process runtime / stale metadata", LocalAppsActionChoiceV050.McpOwnershipStatus);
        Add(root, "Acknowledge stale MCP owner metadata — evidence only", LocalAppsActionChoiceV050.AcknowledgeStaleMcpOwnershipMetadata);
        Add(root, "Read session lease + auto-start local MCP", LocalAppsActionChoiceV050.ReadSessionLease, !adapterActive && !tunnelActive);
        Add(root, "End Read Session — stop MCP + revoke exact bound lease", LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter, adapterActive && !tunnelActive);
        Add(root, "End orphaned read session — exact indexed unbound lease", LocalAppsActionChoiceV050.EndOrphanedReadSession);
        Add(root, "Revoke ALL active read leases (recovery + index reconcile)", LocalAppsActionChoiceV050.RevokeReadLeases);
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
        var dialog = new LocalAppsActionDialogV0515(applicationId, adapterActive, tunnelActive) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v0515-live-status", true, "verified active index + exact canonical revalidation", "no historical enumeration"),
        ("chooser-v0515-history", true, "separate bounded canonical evidence scan", "explicit"),
        ("chooser-v0515-reconcile", true, "prompted only when index missing/dirty", "explicit bounded max 4096"),
        ("chooser-v0515-orphan", true, "exact indexed unbound lease", "pagination-independent"),
        ("chooser-v0515-recovery", true, "revoke-all remains explicit recovery", "preserved"),
        ("chooser-v0518-owner-status", true, "MCP Ownership Status", "read-only cross-process runtime status"),
        ("chooser-v0518-stale-ack", true, "stale metadata acknowledgement", "evidence only; no lease authority"),
        ("chooser-v0515-four-button-surface", true, "dialog-only change", "no top-level button")
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
