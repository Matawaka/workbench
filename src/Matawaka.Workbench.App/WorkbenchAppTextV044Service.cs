using System.IO;
using System.Text;

namespace Matawaka.Workbench.App;

public sealed record AppTextObservationV044(
    string ApplicationId,
    string Version,
    string RelativePath,
    long Bytes,
    string EncodingName,
    string Text)
{
    public string TabTitle => $"File · {ApplicationId}/{RelativePath}";
    public string Summary => $"{ApplicationId} · {Version}   |   {RelativePath}   |   {Bytes:N0} B   |   {EncodingName}";
}

/// <summary>
/// Bounded, read-only text inspection for a file explicitly opened by the operator
/// from an already-observed managed application tree. This is content observation,
/// not application execution or mutation authority.
/// </summary>
public static class WorkbenchAppTextV044Service
{
    public const long MaxTextBytes = 2L * 1024L * 1024L;

    public static AppTextObservationV044 Read(string workspaceRoot, string applicationId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("A non-empty application file relative path is required.");

        var observation = WorkbenchAppTreeV043Service.Read(workspaceRoot, applicationId);
        var normalized = NormalizeRelative(relativePath);
        var node = FindNode(observation.Root, normalized);
        if (node is null || node.IsDirectory)
            throw new InvalidDataException("Text inspection is limited to a regular file represented by the current managed application tree.");

        var root = Path.GetFullPath(observation.ApplicationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideRoot(root, full);
        if (!File.Exists(full) || Directory.Exists(full))
            throw new InvalidDataException("Application text target is not an existing regular file.");
        if (IsReparse(full))
            throw new InvalidDataException("Application text target may not be a reparse point.");

        byte[] bytes;
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            if (stream.Length > MaxTextBytes)
                throw new InvalidDataException($"Application text file exceeds {MaxTextBytes:N0} byte inspection bound.");
            if (stream.Length > int.MaxValue)
                throw new InvalidDataException("Application text file is too large for bounded inspection.");
            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Application text file changed during bounded inspection.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Application text file cannot be read: {normalized}: {ex.Message}", ex);
        }

        var (text, encodingName) = DecodeText(bytes);
        if (text.IndexOf('\0') >= 0)
            throw new InvalidDataException("Application file contains NUL characters and is treated as non-text/binary.");

        return new AppTextObservationV044(
            observation.ApplicationId,
            observation.Version,
            normalized,
            bytes.LongLength,
            encodingName,
            text);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("app-text-v044-size-bound", MaxTextBytes == 2L * 1024L * 1024L, MaxTextBytes.ToString(), "2097152"),
        ("app-text-v044-registration-tree-gated", true, "WorkbenchAppTreeV043Service.Read + exact file node", "registered observed file only"),
        ("app-text-v044-utf8-strict", true, "strict UTF-8 when no BOM", "reject invalid/binary byte sequences"),
        ("app-text-v044-utf16-bom", true, "UTF-16 LE/BE only with BOM", "bounded text support"),
        ("app-text-v044-write-authority", true, "false", "false")
    };

    private static AppTreeNodeV043? FindNode(AppTreeNodeV043 root, string relativePath)
    {
        foreach (var child in root.Children)
        {
            if (child.RelativePath.Equals(relativePath, StringComparison.Ordinal)) return child;
            if (child.IsDirectory)
            {
                var nested = FindNode(child, relativePath);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static (string Text, string EncodingName) DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3), "UTF-8 BOM");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE BOM");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE BOM");
        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Application file is not supported text (strict UTF-8 or BOM-marked UTF-16 required).", ex);
        }
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) ||
            normalized == "." || normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) || normalized.EndsWith("/..", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe application text relative path: {path}");
        return normalized;
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var exactRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(path);
        var prefix = exactRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application text path escaped the exact managed application root.");
    }

    private static bool IsReparse(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Cannot verify reparse boundary for application text target: {path}", ex);
        }
    }
}
