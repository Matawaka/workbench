using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    public const string ProvenanceReviewOnlyArgumentV0561 = "--provenance-review-only";
    public const string ProvenanceReviewResourceNameV0561 = "Matawaka.Workbench.App.ProvenanceReviewV0561.Qualification";

    private TabItem? _provenanceReviewTabV0561;

    internal void ConfigureV0561ProvenanceReviewRouting(bool reviewOnly)
    {
        var receipt = AdmitEmbeddedProvenanceEvidenceV0561();
        var russian = ProvenanceReviewPresentationServiceV0561.Create(receipt, ProvenanceReviewLanguageV0561.Russian);
        var english = ProvenanceReviewPresentationServiceV0561.Create(receipt, ProvenanceReviewLanguageV0561.English);
        _provenanceReviewTabV0561 ??= BuildProvenanceReviewTabV0561(russian, english);

        if (!OutputTabs.Items.Contains(_provenanceReviewTabV0561))
        {
            var lifecycleIndex = OutputTabs.Items.IndexOf(LifecycleTab);
            OutputTabs.Items.Insert(lifecycleIndex >= 0 ? lifecycleIndex + 1 : OutputTabs.Items.Count, _provenanceReviewTabV0561);
        }

        if (!reviewOnly) return;

        // Review-only mode is deliberately detached from all boot/acceptance persistence.
        // It is a human semantic inspection surface, not an authority ceremony.
        Loaded -= Window_LoadedV040;
        Loaded -= Window_LoadedV055;
        Loaded -= Window_LoadedV0552;
        Closing -= Window_Closing;

        PrimaryMaintenanceSurface.IsEnabled = false;
        PrimaryMaintenanceSurface.IsHitTestVisible = false;
        InstalledAppsList.IsEnabled = false;
        InstalledAppsList.IsHitTestVisible = false;
        HistoricalCompatibilityBindings.IsEnabled = false;
        HistoricalCompatibilityBindings.IsHitTestVisible = false;

        Title = "Matawaka Workbench — provenance review candidate (read-only)";
        StatusText.Text = "READ-ONLY REVIEW — PROVENANCE OBSERVED / NO AUTHORITY";
        ProgressBar.Value = 0;
        OutputTabs.SelectedItem = _provenanceReviewTabV0561;
    }

    public static ProvenanceAdmissionReceipt AdmitEmbeddedProvenanceEvidenceV0561()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(ProvenanceReviewResourceNameV0561)
            ?? throw new InvalidDataException($"Embedded provenance qualification resource is missing: {ProvenanceReviewResourceNameV0561}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return C2paProvenanceAdmissionService.AdmitAcceptedWorkbenchV0552(buffer.ToArray());
    }

    private static TabItem BuildProvenanceReviewTabV0561(
        ProvenanceReviewPresentationV0561 russian,
        ProvenanceReviewPresentationV0561 english)
    {
        var languageTabs = new TabControl
        {
            SelectedIndex = 0,
            Margin = new Thickness(0)
        };
        languageTabs.Items.Add(BuildProvenanceLanguageTabV0561(russian));
        languageTabs.Items.Add(BuildProvenanceLanguageTabV0561(english));

        return new TabItem
        {
            Header = "Provenance",
            Content = languageTabs
        };
    }

    private static TabItem BuildProvenanceLanguageTabV0561(ProvenanceReviewPresentationV0561 presentation)
    {
        var content = new StackPanel { Margin = new Thickness(18) };

        var bannerContent = new StackPanel();
        bannerContent.Children.Add(new TextBlock
        {
            Text = presentation.Headline,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7)
        });
        bannerContent.Children.Add(new TextBlock
        {
            Text = presentation.Summary,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        content.Children.Add(new Border
        {
            Background = Brushes.LightGoldenrodYellow,
            BorderBrush = Brushes.Goldenrod,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 18),
            Child = bannerContent
        });

        AddProvenanceSectionV0561(content, presentation.ObservedSectionTitle, presentation.ObservedFacts, false, null);
        AddProvenanceSectionV0561(content, presentation.BoundariesSectionTitle, presentation.Boundaries, false, null);
        AddProvenanceSectionV0561(content, presentation.EvidenceSectionTitle, presentation.EvidenceSource, true, presentation.CopyHint);

        var humanReview = new Border
        {
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 0)
        };
        humanReview.Child = new TextBlock
        {
            Text = presentation.HumanReviewPrompt,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1120
        };
        content.Children.Add(humanReview);

        return new TabItem
        {
            Header = presentation.TabHeader,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            }
        };
    }

    private static void AddProvenanceSectionV0561(
        Panel parent,
        string title,
        IReadOnlyList<ProvenanceReviewFactV0561> facts,
        bool copyableValues,
        string? copyHint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 6)
        });

        if (copyableValues && !string.IsNullOrWhiteSpace(copyHint))
        {
            parent.Children.Add(new TextBlock
            {
                Text = copyHint,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7)
            });
        }

        foreach (var fact in facts)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 7),
                MaxWidth = 1160,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(copyableValues ? 620 : 330) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = fact.Label,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            FrameworkElement value;
            if (copyableValues)
            {
                value = new TextBox
                {
                    Text = fact.Value,
                    IsReadOnly = true,
                    IsReadOnlyCaretVisible = true,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(10, 0, 10, 0),
                    ToolTip = copyHint
                };
            }
            else
            {
                value = new TextBlock
                {
                    Text = fact.Value,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 0, 10, 0)
                };
            }

            var explanation = new TextBlock
            {
                Text = fact.Explanation,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(value, 1);
            Grid.SetColumn(explanation, 2);
            row.Children.Add(label);
            row.Children.Add(value);
            row.Children.Add(explanation);
            parent.Children.Add(row);
        }
    }
}
