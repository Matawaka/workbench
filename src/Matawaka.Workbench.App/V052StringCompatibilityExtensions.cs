namespace Matawaka.Workbench.App;

internal static class V052StringCompatibilityExtensions
{
    public static bool StartsWith(this string value, char prefix, StringComparison comparisonType)
        => value.StartsWith(prefix.ToString(), comparisonType);
}
