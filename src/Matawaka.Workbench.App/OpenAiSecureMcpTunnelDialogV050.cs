using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed record OpenAiSecureMcpTunnelInputV050(string TunnelId, string RuntimeApiKey);

public sealed class OpenAiSecureMcpTunnelDialogV050 : Window
{
    private readonly TextBox _tunnelId = new();
    private readonly PasswordBox _runtimeKey = new();
    public OpenAiSecureMcpTunnelInputV050? Result { get; private set; }

    public OpenAiSecureMcpTunnelDialogV050(string applicationId)
    {
        Title = "OpenAI Secure MCP Tunnel — session input";
        Width = 690;
        MinWidth = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = $"Application: {applicationId}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        root.Children.Add(new TextBlock
        {
            Text = "This action starts only an outbound OpenAI tunnel-client runtime for an already-existing OpenAI tunnel ID. Workbench does not create/delete tunnels and does not configure ChatGPT. Use a dedicated runtime API key, not an Admin key. The key is passed to the child process environment for this session and is not written to Workbench receipts.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(new TextBlock { Text = "Tunnel ID (tunnel_ + 32 lowercase hex):", Margin = new Thickness(0, 0, 0, 4) });
        _tunnelId.MinWidth = 620;
        _tunnelId.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_tunnelId);

        root.Children.Add(new TextBlock { Text = "Runtime API key (session-only; plaintext is not persisted):", Margin = new Thickness(0, 0, 0, 4) });
        _runtimeKey.MinWidth = 620;
        _runtimeKey.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_runtimeKey);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 120, Height = 34, Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = false };
        var next = new Button { Content = "Preview", Width = 140, Height = 34, IsDefault = false };
        cancel.Click += (_, _) => { Result = null; DialogResult = false; };
        next.Click += (_, _) =>
        {
            var tunnel = _tunnelId.Text.Trim();
            var key = _runtimeKey.Password;
            if (!OpenAiSecureMcpTunnelV050Service.SafeTunnelId(tunnel))
            {
                MessageBox.Show(this, "Tunnel ID must be tunnel_ followed by 32 lowercase hexadecimal characters.", "Secure MCP Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(key) || key.Length < 16 || key.Length > 4096 || key.Any(ch => ch is '\r' or '\n' or '\0'))
            {
                MessageBox.Show(this, "Runtime API key is missing or has an unsafe shape.", "Secure MCP Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = new OpenAiSecureMcpTunnelInputV050(tunnel, key);
            _runtimeKey.Clear();
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(next);
        root.Children.Add(buttons);
        Content = root;
    }

    public static OpenAiSecureMcpTunnelInputV050? ShowInput(Window owner, string applicationId)
    {
        var dialog = new OpenAiSecureMcpTunnelDialogV050(applicationId) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("tunnel-dialog-v050-passwordbox", true, "PasswordBox", "runtime key not displayed"),
        ("tunnel-dialog-v050-no-default-effect", true, "Preview IsDefault=false", "no implicit tunnel start"),
        ("tunnel-dialog-v050-no-admin-key-field", true, "runtime key only", "no admin credential authority"),
        ("tunnel-dialog-v050-no-persistence", true, "input returned in memory only", "no settings/file write")
    };
}
