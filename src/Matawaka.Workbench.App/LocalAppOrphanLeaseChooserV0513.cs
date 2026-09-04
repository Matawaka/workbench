using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed class LocalAppOrphanLeaseChooserV0513 : Window
{
    private readonly ListBox _list = new();
    private readonly IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> _leases;

    public string? SelectedLeaseId { get; private set; }

    public LocalAppOrphanLeaseChooserV0513(string applicationId, IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        Title = "End orphaned read session — choose exact LeaseId";
        Width = 760;
        Height = 430;
        MinWidth = 680;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(18) };
        var intro = new TextBlock
        {
            Text = $"ApplicationId: {applicationId}\nSelect exactly one live lease that is NOT bound to the active local MCP. No bearer is displayed. No sibling lease will be revoked.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        _list.SelectionMode = SelectionMode.Single;
        foreach (var lease in _leases)
        {
            var scopes = string.Join(", ", lease.Scopes.Select(x => $"{x.Role}:{x.PathPrefix}"));
            _list.Items.Add($"{lease.LeaseId}  | expires {lease.ExpiresAt:O} | calls={lease.RemainingCalls} bytes={lease.RemainingBytes} | {scopes}");
        }
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var close = new Button { Content = "Cancel", Width = 110, Height = 32, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var choose = new Button { Content = "Choose exact lease", Width = 160, Height = 32, IsDefault = false };
        choose.Click += (_, _) =>
        {
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _leases.Count)
            {
                MessageBox.Show(this, "Select one exact LeaseId.", "End orphaned read session", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedLeaseId = _leases[_list.SelectedIndex].LeaseId;
            DialogResult = true;
        };
        buttons.Children.Add(choose);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;
    }

    public static string? Choose(Window owner, string applicationId, IReadOnlyList<LocalAppReadSessionStatusLeaseV0513> leases)
    {
        if (leases.Count == 0) return null;
        if (leases.Count == 1) return leases[0].LeaseId;
        var dialog = new LocalAppOrphanLeaseChooserV0513(applicationId, leases) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedLeaseId : null;
    }
}
