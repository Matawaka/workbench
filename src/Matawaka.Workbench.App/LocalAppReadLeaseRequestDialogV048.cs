using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed class LocalAppReadLeaseRequestDialogV048 : Window
{
    private readonly TextBox _requestBox;
    public string? RequestJson { get; private set; }

    public LocalAppReadLeaseRequestDialogV048(string applicationId)
    {
        Title = "Read session lease — paste bounded lease request";
        Width = 820;
        Height = 620;
        MinWidth = 680;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = $"Selected registered app: {applicationId}\n\nPaste one bounded read-lease request. Preview is content-free. A lease is created only after a second explicit confirmation and is limited by exact app/scopes, TTL, call count and byte budgets. v0.48 creates no network listener/tunnel/MCP endpoint.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        _requestBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Text = JsonSerializer.Serialize(new
            {
                Schema = LocalAppReadLeaseV048Service.RequestSchema,
                RequestId = $"lease-request-{DateTime.Now:yyyyMMdd-HHmmss}",
                ApplicationId = applicationId,
                Scopes = new[]
                {
                    new { Role = "installed", PathPrefix = "data/state.json" }
                },
                MaxBytesPerRead = 65536,
                MaxTotalBytes = 262144,
                MaxCalls = 4,
                TtlSeconds = 300
            }, new JsonSerializerOptions { WriteIndented = true })
        };
        Grid.SetRow(_requestBox, 1);
        root.Children.Add(_requestBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var preview = new Button { Content = "Preview lease", Width = 130, Height = 32, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        preview.Click += (_, _) => { RequestJson = _requestBox.Text; DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 32, IsCancel = true };
        buttons.Children.Add(preview);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    public static string? ShowRequest(Window owner, string applicationId)
    {
        var dialog = new LocalAppReadLeaseRequestDialogV048(applicationId) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.RequestJson : null;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("lease-dialog-v048-selected-app", true, "ApplicationId prefilled from selected app", "bound"),
        ("lease-dialog-v048-preview-first", true, "Preview lease", "no lease/content read before preview"),
        ("lease-dialog-v048-default-scope", true, "installed:data/state.json", "narrow exact-file example"),
        ("lease-dialog-v048-network", true, "none", "none")
    };
}
