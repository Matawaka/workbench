namespace Matawaka.Workbench.App;

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
