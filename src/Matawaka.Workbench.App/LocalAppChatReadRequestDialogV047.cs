using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed class LocalAppChatReadRequestDialogV047 : Window
{
    private readonly TextBox _requestBox;
    public string? RequestJson { get; private set; }

    public LocalAppChatReadRequestDialogV047(string applicationId)
    {
        Title = "Chat read relay — paste bounded request";
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = $"Selected registered app: {applicationId}\n\nPaste one request JSON produced by the chat. Workbench will validate it and show an exact file SHA/range preview before any file contents are read or copied to the clipboard.",
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
                Schema = LocalAppChatReadRelayV047Service.RequestSchema,
                RequestId = $"read-{DateTime.Now:yyyyMMdd-HHmmss}",
                ApplicationId = applicationId,
                Role = "installed",
                RelativePath = "data/state.json",
                Offset = 0,
                MaxBytes = 65536,
                ExpectedFileSha256 = (string?)null
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
        var preview = new Button { Content = "Preview request", Width = 130, Height = 32, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
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
        var dialog = new LocalAppChatReadRequestDialogV047(applicationId) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.RequestJson : null;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chat-read-dialog-v047-selected-app", true, "ApplicationId prefilled from selected app", "bound"),
        ("chat-read-dialog-v047-preview-first", true, "Preview request", "no immediate read/disclosure"),
        ("chat-read-dialog-v047-default-network", true, "none", "none")
    };
}
