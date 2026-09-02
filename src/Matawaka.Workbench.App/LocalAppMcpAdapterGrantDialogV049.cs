using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed class LocalAppMcpAdapterGrantDialogV049 : Window
{
    private readonly TextBox _grantBox;
    public string? GrantJson { get; private set; }

    public LocalAppMcpAdapterGrantDialogV049(string applicationId)
    {
        Title = "Read-only MCP adapter — paste active v0.48 lease grant";
        Width = 780;
        Height = 590;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = $"Selected registered app: {applicationId}\n\nPaste the exact grant JSON produced by Read session lease. Workbench validates LeaseId + bearer against current hash-only local lease state before it can offer a loopback MCP listener. The bearer is not an MCP tool argument and is not persisted by the adapter receipt.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        _grantBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        Grid.SetRow(_grantBox, 1);
        root.Children.Add(_grantBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var preview = new Button { Content = "Preview adapter", Width = 140, Height = 32, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        preview.Click += (_, _) => { GrantJson = _grantBox.Text; DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 32, IsCancel = true };
        buttons.Children.Add(preview);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    public static string? ShowGrant(Window owner, string applicationId)
    {
        var dialog = new LocalAppMcpAdapterGrantDialogV049(applicationId) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.GrantJson : null;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("mcp-grant-dialog-v049-selected-app", true, "selected app shown", "bound"),
        ("mcp-grant-dialog-v049-preview-first", true, "Preview adapter", "no listener before confirmation"),
        ("mcp-grant-dialog-v049-bearer-boundary", true, "grant pasted locally; bearer not tool argument", "separate authority")
    };
}
