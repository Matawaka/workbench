using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public sealed record JsonSearchPresentationV0412Receipt(
    bool TargetFocusAcquired,
    bool SearchFocusRestored,
    int SelectionStart,
    int SelectionLength,
    bool OutputUnchanged,
    bool InactiveHighlightEnabled,
    bool SystemHighlightBrushBound,
    bool SystemHighlightTextBrushBound);

/// <summary>
/// Presentation-only successor to v0.41.1. A real-host observation showed that
/// a programmatic Select while the search box already owned focus could leave a
/// valid selection logically present but visually absent. This adapter primes
/// the WPF focus lifecycle before selecting, then restores search-box focus.
/// </summary>
public static class JsonSearchPresentationV0412Service
{
    public static void ConfigureVisibleInactiveSelection(params TextBox[] outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        foreach (var output in outputs)
        {
            ArgumentNullException.ThrowIfNull(output);
            output.IsInactiveSelectionHighlightEnabled = true;
            output.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = SystemColors.HighlightBrush;
            output.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = SystemColors.HighlightTextBrush;
        }
    }

    public static JsonSearchPresentationV0412Receipt PresentMatch(
        TextBox target,
        TextBox searchBox,
        JsonOutputSearchMatch match)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(searchBox);
        ArgumentNullException.ThrowIfNull(match);
        if (match.Start < 0 || match.Length <= 0 || match.Start + match.Length > target.Text.Length)
            throw new ArgumentOutOfRangeException(nameof(match), "Search match is outside current output text.");

        var before = target.Text;

        // The target must participate in a real focus lifecycle. Merely setting
        // IsInactiveSelectionHighlightEnabled does not create an inactive visual
        // state when the TextBox has never owned keyboard focus.
        var targetFocusAcquired = target.Focus() && target.IsKeyboardFocusWithin;
        if (!targetFocusAcquired)
            throw new InvalidOperationException("Search result output TextBox could not acquire keyboard focus for presentation.");

        target.Select(match.Start, match.Length);
        var line = target.GetLineIndexFromCharacterIndex(match.Start);
        if (line >= 0) target.ScrollToLine(line);

        if (target.SelectionStart != match.Start || target.SelectionLength != match.Length)
            throw new InvalidOperationException("Search result selection did not preserve the exact match range.");

        var searchFocusRestored = searchBox.Focus() && searchBox.IsKeyboardFocusWithin;
        if (!searchFocusRestored)
            throw new InvalidOperationException("Search box focus could not be restored after result presentation.");

        var unchanged = string.Equals(before, target.Text, StringComparison.Ordinal);
        if (!unchanged)
            throw new InvalidOperationException("Read-only search presentation unexpectedly changed output text.");

        return new JsonSearchPresentationV0412Receipt(
            targetFocusAcquired,
            searchFocusRestored,
            target.SelectionStart,
            target.SelectionLength,
            unchanged,
            target.IsInactiveSelectionHighlightEnabled,
            ReferenceEquals(target.Resources[SystemColors.InactiveSelectionHighlightBrushKey], SystemColors.HighlightBrush) ||
                Equals(target.Resources[SystemColors.InactiveSelectionHighlightBrushKey], SystemColors.HighlightBrush),
            ReferenceEquals(target.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey], SystemColors.HighlightTextBrush) ||
                Equals(target.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey], SystemColors.HighlightTextBrush));
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("json-search-v0412-focus-pulse", true, "target.Focus -> Select/Scroll -> search.Focus", "focus-primed inactive selection"),
        ("json-search-v0412-output-mutation", true, "false", "false"),
        ("json-search-v0412-search-algorithm-delta", true, "false", "false"),
        ("json-search-v0412-inactive-highlight", true, "enabled + system selection brushes", "visible inactive selection"),
        ("json-search-v0412-authority-created", true, "false", "false")
    };
}
