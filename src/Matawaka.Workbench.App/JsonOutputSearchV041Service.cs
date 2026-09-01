namespace Matawaka.Workbench.App;

public enum JsonOutputSearchDirection
{
    Next,
    Previous
}

public sealed record JsonOutputSearchMatch(
    int Start,
    int Length,
    int Ordinal,
    int Total,
    bool Wrapped);

/// <summary>
/// Pure read-only text search for Workbench JSON/text output panes.
/// It never mutates source text, files, clipboard, receipts or authority state.
/// </summary>
public static class JsonOutputSearchV041Service
{
    public static JsonOutputSearchMatch? Find(
        string? text,
        string? query,
        int selectionStart,
        int selectionLength,
        JsonOutputSearchDirection direction)
    {
        text ??= string.Empty;
        query ??= string.Empty;
        if (query.Length == 0 || text.Length == 0) return null;

        var starts = FindAll(text, query);
        if (starts.Count == 0) return null;

        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionLength = Math.Clamp(selectionLength, 0, text.Length - selectionStart);

        int index;
        bool wrapped;
        if (direction == JsonOutputSearchDirection.Next)
        {
            var anchor = selectionStart + selectionLength;
            index = starts.FindIndex(x => x >= anchor);
            wrapped = index < 0;
            if (wrapped) index = 0;
        }
        else
        {
            var anchor = selectionStart;
            index = starts.FindLastIndex(x => x < anchor);
            wrapped = index < 0;
            if (wrapped) index = starts.Count - 1;
        }

        return new JsonOutputSearchMatch(
            starts[index],
            query.Length,
            index + 1,
            starts.Count,
            wrapped);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var sample = "alpha JSON beta json gamma Привет привет";
        var first = Find(sample, "json", 0, 0, JsonOutputSearchDirection.Next);
        var second = Find(sample, "JSON", first!.Start, first.Length, JsonOutputSearchDirection.Next);
        var wrappedNext = Find(sample, "json", second!.Start, second.Length, JsonOutputSearchDirection.Next);
        var wrappedPrevious = Find(sample, "json", first.Start, 0, JsonOutputSearchDirection.Previous);
        var unicode = Find(sample, "ПРИВЕТ", 0, 0, JsonOutputSearchDirection.Next);
        var missing = Find(sample, "absent", 0, 0, JsonOutputSearchDirection.Next);
        var before = sample;
        _ = Find(sample, "beta", 0, 0, JsonOutputSearchDirection.Next);

        return new[]
        {
            ("json-search-v041-first", first is { Ordinal: 1, Total: 2, Wrapped: false }, $"{first?.Ordinal}/{first?.Total}/wrap={first?.Wrapped}", "1/2/wrap=False"),
            ("json-search-v041-next-case-insensitive", second is { Ordinal: 2, Total: 2, Wrapped: false }, $"{second?.Ordinal}/{second?.Total}/wrap={second?.Wrapped}", "2/2/wrap=False"),
            ("json-search-v041-next-wrap", wrappedNext is { Ordinal: 1, Total: 2, Wrapped: true }, $"{wrappedNext?.Ordinal}/{wrappedNext?.Total}/wrap={wrappedNext?.Wrapped}", "1/2/wrap=True"),
            ("json-search-v041-previous-wrap", wrappedPrevious is { Ordinal: 2, Total: 2, Wrapped: true }, $"{wrappedPrevious?.Ordinal}/{wrappedPrevious?.Total}/wrap={wrappedPrevious?.Wrapped}", "2/2/wrap=True"),
            ("json-search-v041-unicode-case-insensitive", unicode is { Total: 2 }, $"total={unicode?.Total}", "total=2"),
            ("json-search-v041-no-match", missing is null, missing is null ? "null" : "match", "null"),
            ("json-search-v041-input-not-mutated", sample == before, sample == before ? "unchanged" : "changed", "unchanged")
        };
    }

    private static List<int> FindAll(string text, string query)
    {
        var starts = new List<int>();
        var index = 0;
        while (index <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            starts.Add(found);
            index = found + Math.Max(1, query.Length);
        }
        return starts;
    }
}
