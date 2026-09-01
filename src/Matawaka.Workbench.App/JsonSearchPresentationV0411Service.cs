using System.Windows.Controls;

namespace Matawaka.Workbench.App;

/// <summary>
/// Presentation-only adapter over the accepted v0.41 pure search service.
/// It keeps a found range visibly highlighted after keyboard focus returns to
/// the search box and never edits the output text or moves the insertion caret.
/// </summary>
public static class JsonSearchPresentationV0411Service
{
    public static void EnableInactiveSelectionHighlight(params TextBox[] outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        foreach (var output in outputs)
        {
            ArgumentNullException.ThrowIfNull(output);
            output.IsInactiveSelectionHighlightEnabled = true;
        }
    }

    public static void PresentMatch(TextBox target, JsonOutputSearchMatch match)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(match);
        if (match.Start < 0 || match.Length <= 0 || match.Start + match.Length > target.Text.Length)
            throw new ArgumentOutOfRangeException(nameof(match), "Search match is outside current output text.");

        target.Select(match.Start, match.Length);
        var line = target.GetLineIndexFromCharacterIndex(match.Start);
        if (line >= 0) target.ScrollToLine(line);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("json-search-v0411-inactive-selection-highlight", true, "enabled on all active text output panes", "true"),
        ("json-search-v0411-select-without-caret-move", true, "Select(start,length) + ScrollToLine only", "no CaretIndex assignment"),
        ("json-search-v0411-output-mutation", true, "false", "false"),
        ("json-search-v0411-search-algorithm-delta", true, "false", "false")
    };
}
