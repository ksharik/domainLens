using System.Text;

namespace DomainLens.Scanner.Internal;

internal sealed record RepositoryFile(
    string RelativePath,
    string FullPath,
    long Length,
    string ContentHash,
    byte[]? CapturedContent)
{
    public string ReadCapturedText()
    {
        if (CapturedContent is null)
        {
            throw new InvalidOperationException($"Content was not captured for '{RelativePath}'.");
        }

        using var stream = new MemoryStream(CapturedContent, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return reader.ReadToEnd();
    }
}

internal sealed record RepositoryInventory(
    string RootPath,
    IReadOnlyList<RepositoryFile> Files,
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<ScannerIssue> Issues)
{
    public IReadOnlyDictionary<string, RepositoryFile> ByRelativePath { get; } =
        Files.ToDictionary(file => file.RelativePath, PathSafety.FileSystemPathComparer);
}
