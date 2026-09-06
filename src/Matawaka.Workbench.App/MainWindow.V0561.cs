using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        var presentation = ProvenanceReviewPresentationServiceV0561.Create(receipt);
        _provenanceReviewTabV0561 ??= BuildProvenanceReviewTabV0561(presentation);

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

    private static TabItem BuildProvenanceReviewTabV0561(ProvenanceReviewPresentationV0561 presentation)
    {
        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock
        {
            Text = presentation.Headline,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = presentation.Summary,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 920,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18)
        });

        AddProvenanceSectionV0561(content, "What was observed", presentation.ObservedFacts);
        AddProvenanceSectionV0561(content, "What was NOT established or created", presentation.Boundaries);
        AddProvenanceSectionV0561(content, "Evidence source", presentation.EvidenceSource);

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
            MaxWidth = 920
        };
        content.Children.Add(humanReview);

        return new TabItem
        {
            Header = "Provenance",
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
        IReadOnlyList<ProvenanceReviewFactV0561> facts)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 6)
        });

        foreach (var fact in facts)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock { Text = fact.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var value = new TextBlock { Text = fact.Value, FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 10, 0) };
            var explanation = new TextBlock { Text = fact.Explanation, TextWrapping = TextWrapping.Wrap };
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
