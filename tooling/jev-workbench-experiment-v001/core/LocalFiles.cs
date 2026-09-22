namespace Matawaka.Workbench.JevLab;

internal static class LocalFiles
{
    internal static string Resolve(string path, string? manifestDirectory = null)
    {
        StrictJson.Require(!string.IsNullOrWhiteSpace(path), "PATH_REQUIRED");
        StrictJson.Require(!path.StartsWith("\\\\", StringComparison.Ordinal) &&
            !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains("://", StringComparison.Ordinal), "NETWORK_OR_DEVICE_PATH_REJECTED");
        if (manifestDirectory is null)
            StrictJson.Require(Path.IsPathFullyQualified(path), "ABSOLUTE_MANIFEST_PATH_REQUIRED");
        else if (!Path.IsPathFullyQualified(path))
        {
            StrictJson.Require(!Path.IsPathRooted(path), "PARTIAL_ROOT_PATH_REJECTED");
            path = Path.Combine(manifestDirectory, path);
        }
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            StrictJson.Require(full.Length >= 3 && char.IsAsciiLetter(full[0]) && full[1] == ':' &&
                !full.AsSpan(2).Contains(':'), "DEVICE_OR_ALTERNATE_STREAM_REJECTED");
            StrictJson.Require(new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Network, "NETWORK_DRIVE_REJECTED");
        }
        CheckAncestry(full);
        return full;
    }

    private static void CheckAncestry(string path)
    {
        for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            StrictJson.Require((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) == 0, "LINK_OR_REPARSE_PATH_REJECTED");
    }

    internal static byte[] Read(string path)
    {
        CheckAncestry(path);
        // A bounded, single shared-read handle rejects concurrent writers on Windows. Ancestry checks
        // reject existing links; this is not an OS sandbox against a hostile process renaming parents.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        StrictJson.Require(stream.Length <= StrictJson.MaximumBytes, "FILE_TOO_LARGE");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        StrictJson.Require(stream.ReadByte() == -1, "FILE_CHANGED_DURING_READ");
        CheckAncestry(path);
        return bytes;
    }
}
