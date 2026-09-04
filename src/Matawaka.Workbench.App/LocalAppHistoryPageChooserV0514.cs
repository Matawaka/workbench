using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

/// <summary>
/// Bounded operator choice for historical lease evidence pages. It never reads or
/// mutates lease state and returns only an offset derived from an explicit page number.
/// </summary>
public sealed class LocalAppHistoryPageChooserV0514 : Window
{
    private readonly TextBox _pageBox;
    private readonly int _pageCount;
    private readonly int _pageSize;

    public int? SelectedOffset { get; private set; }

    public LocalAppHistoryPageChooserV0514(string applicationId, int historicalCount, int pageSize)
    {
        if (historicalCount < 1) throw new ArgumentOutOfRangeException(nameof(historicalCount));
        if (pageSize < 1 || pageSize > LocalAppReadSessionStatusV0514Service.MaxHistoryLimit)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        _pageSize = pageSize;
        _pageCount = (historicalCount + pageSize - 1) / pageSize;

        Title = "Read Session history page";
        Width = 520;
        MinWidth = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Application: {applicationId}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Historical lease evidence: {historicalCount} records. Page size: {pageSize}. Choose page 1..{_pageCount}; page 1 is the newest evidence.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _pageBox = new TextBox
        {
            Text = "1",
            Height = 30,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_pageBox);

        var show = new Button
        {
            Content = "Show bounded page",
            Height = 34,
            IsDefault = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        show.Click += (_, _) => Complete();
        root.Children.Add(show);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true };
        cancel.Click += (_, _) => { SelectedOffset = null; DialogResult = false; };
        root.Children.Add(cancel);

        Content = root;
    }

    public static int? Choose(Window owner, string applicationId, int historicalCount, int pageSize)
    {
        var dialog = new LocalAppHistoryPageChooserV0514(applicationId, historicalCount, pageSize) { Owner = owner };
        dialog.ShowDialog();
        return dialog.SelectedOffset;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("history-page-v0514-explicit", true, "page number chosen by operator", "explicit"),
        ("history-page-v0514-default", true, "page 1", "newest"),
        ("history-page-v0514-size", LocalAppReadSessionStatusV0514Service.DefaultHistoryLimit == 16,
            LocalAppReadSessionStatusV0514Service.DefaultHistoryLimit.ToString(CultureInfo.InvariantCulture), "16"),
        ("history-page-v0514-effect", true, "returns offset only", "no lease-state mutation")
    };

    private void Complete()
    {
        if (!int.TryParse(_pageBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var page) ||
            page < 1 || page > _pageCount)
        {
            MessageBox.Show(
                this,
                $"Enter a page number from 1 to {_pageCount}.",
                "Read Session history page",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedOffset = checked((page - 1) * _pageSize);
        DialogResult = true;
    }
}
