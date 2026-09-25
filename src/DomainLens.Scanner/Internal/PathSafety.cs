namespace DomainLens.Scanner.Internal;

internal static class PathSafety
{
    public static StringComparer FileSystemPathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison FileSystemPathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    public static bool IsPortableRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized[0] == '/' || Path.IsPathRooted(path))
        {
            return false;
        }

        // Path.IsPathRooted follows the host OS's rules. Explicitly reject Windows drive-qualified
        // paths as well so a solution captured on Linux cannot smuggle an absolute Windows path.
        return normalized.Length < 2 ||
               normalized[1] != ':' ||
               !char.IsAsciiLetter(normalized[0]);
    }

    public static bool TryResolveWithinRoot(
        string rootPath,
        string baseDirectory,
        string candidate,
        out string fullPath,
        out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = string.Empty;

        if (!IsPortableRelativePath(candidate))
        {
            return false;
        }

        try
        {
            var rootWithoutSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var root = EnsureTrailingSeparator(rootWithoutSeparator);
            var normalizedBaseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
            if (!string.Equals(normalizedBaseDirectory, rootWithoutSeparator, FileSystemPathComparison) &&
                !normalizedBaseDirectory.StartsWith(root, FileSystemPathComparison))
            {
                return false;
            }

            fullPath = Path.GetFullPath(Path.Combine(normalizedBaseDirectory, candidate));
            if (!fullPath.StartsWith(root, FileSystemPathComparison))
            {
                fullPath = string.Empty;
                return false;
            }

            relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, fullPath));
            return relativePath.Length > 0 &&
                   relativePath != ".." &&
                   !relativePath.StartsWith("../", StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            fullPath = string.Empty;
            relativePath = string.Empty;
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
