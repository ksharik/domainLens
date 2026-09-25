using System.Security.Cryptography;

namespace DomainLens.Scanner.Internal;

internal sealed class SafeRepositoryInventory
{
    private const int ReadBufferSize = 64 * 1024;

    private static readonly HashSet<string> ExcludedDirectoryNames = new(PathSafety.FileSystemPathComparer)
    {
        ".git",
        ".hg",
        ".svn",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "TestResults",
    };

    private static readonly HashSet<string> CapturedExtensions = new(PathSafety.FileSystemPathComparer)
    {
        ".sln",
        ".csproj",
        ".cs",
        ".props",
        ".targets",
        ".config",
    };

    public async Task<RepositoryInventory> CaptureAsync(
        ScannerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumFileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum file count must be greater than zero.");
        }

        if (options.MaximumFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum file size must be greater than zero.");
        }

        if (options.MaximumTotalBytesRead <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum total bytes read must be greater than zero.");
        }

        var rootPath = Path.GetFullPath(options.RepositoryPath);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("The repository directory does not exist.");
        }

        if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The repository root cannot be a symbolic link or reparse point.");
        }

        var files = new List<RepositoryFile>();
        var excludedPaths = new List<string>();
        var issues = new List<ScannerIssue>();
        var pendingDirectories = new Stack<string>();
        var observedFileCount = 0;
        long observedEntryCount = 0;
        long totalBytesRead = 0;
        var maximumEntryCount = Math.Max(10_000L, (long)options.MaximumFileCount * 4L);
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    observedEntryCount++;
                    if (observedEntryCount > maximumEntryCount)
                    {
                        throw new InvalidOperationException(
                            $"The repository exceeds the configured traversal limit of {maximumEntryCount} filesystem entries.");
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (
                        exception is UnauthorizedAccessException or IOException or PathTooLongException)
                    {
                        issues.Add(new ScannerIssue(
                            "DL1002",
                            ScannerIssueSeverity.Warning,
                            "A filesystem entry could not be inspected and was excluded from the snapshot.",
                            ToRelative(rootPath, entry)));
                        continue;
                    }

                    var relativePath = ToRelative(rootPath, entry);
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (!isDirectory)
                    {
                        observedFileCount++;
                        if (observedFileCount > options.MaximumFileCount)
                        {
                            throw new InvalidOperationException(
                                $"The repository exceeds the configured {options.MaximumFileCount} file limit.");
                        }
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        excludedPaths.Add(relativePath + (isDirectory ? "/" : string.Empty));
                        issues.Add(new ScannerIssue(
                            "DL1003",
                            ScannerIssueSeverity.Warning,
                            "A symbolic link or reparse point was excluded from analysis.",
                            relativePath));
                        continue;
                    }

                    if (!isDirectory && (attributes & FileAttributes.Device) != 0)
                    {
                        excludedPaths.Add(relativePath);
                        issues.Add(new ScannerIssue(
                            "DL1006",
                            ScannerIssueSeverity.Warning,
                            "A device or non-regular filesystem entry was excluded from analysis.",
                            relativePath));
                        continue;
                    }

                    if (isDirectory)
                    {
                        if (ExcludedDirectoryNames.Contains(Path.GetFileName(entry)))
                        {
                            excludedPaths.Add(relativePath + "/");
                            continue;
                        }

                        pendingDirectories.Push(entry);
                        continue;
                    }

                    try
                    {
                        var fileInfo = new FileInfo(entry);
                        if (fileInfo.Length > options.MaximumFileSizeBytes)
                        {
                            AddOversizedFileIssue(options, excludedPaths, issues, relativePath);
                            continue;
                        }

                        var remainingByteBudget = options.MaximumTotalBytesRead - totalBytesRead;
                        if (fileInfo.Length > remainingByteBudget)
                        {
                            throw CreateTotalByteLimitException(options.MaximumTotalBytesRead);
                        }

                        var captureContent = CapturedExtensions.Contains(fileInfo.Extension);
                        BoundedFileRead read;
                        try
                        {
                            read = await ReadFileBoundedAsync(
                                    entry,
                                    captureContent,
                                    Math.Min(options.MaximumFileSizeBytes, remainingByteBudget),
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (FileSizeLimitExceededException) when (
                            remainingByteBudget < options.MaximumFileSizeBytes)
                        {
                            throw CreateTotalByteLimitException(options.MaximumTotalBytesRead);
                        }

                        totalBytesRead += read.Length;
                        files.Add(new RepositoryFile(
                            relativePath,
                            entry,
                            read.Length,
                            read.ContentHash,
                            read.CapturedContent));
                    }
                    catch (FileSizeLimitExceededException)
                    {
                        AddOversizedFileIssue(options, excludedPaths, issues, relativePath);
                    }
                    catch (Exception exception) when (
                        exception is UnauthorizedAccessException or IOException or PathTooLongException)
                    {
                        issues.Add(new ScannerIssue(
                            "DL1005",
                            ScannerIssueSeverity.Warning,
                            "A file could not be read and was excluded from the snapshot.",
                            relativePath));
                    }
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or PathTooLongException)
            {
                issues.Add(new ScannerIssue(
                    "DL1001",
                    ScannerIssueSeverity.Warning,
                    "A directory could not be read completely and its remaining entries were excluded from the snapshot.",
                    ToRelative(rootPath, directory)));
            }
        }

        return new RepositoryInventory(
            rootPath,
            files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray(),
            excludedPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            issues.OrderBy(issue => issue.RelativePath, StringComparer.Ordinal)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static string ToRelative(string rootPath, string path) =>
        PathSafety.NormalizeRelativePath(Path.GetRelativePath(rootPath, path));

    private static void AddOversizedFileIssue(
        ScannerOptions options,
        ICollection<string> excludedPaths,
        ICollection<ScannerIssue> issues,
        string relativePath)
    {
        excludedPaths.Add(relativePath);
        issues.Add(new ScannerIssue(
            "DL1004",
            ScannerIssueSeverity.Warning,
            $"A file larger than the configured {options.MaximumFileSizeBytes} byte limit was excluded.",
            relativePath));
    }

    private static InvalidOperationException CreateTotalByteLimitException(long maximumTotalBytesRead) =>
        new($"The repository exceeds the configured {maximumTotalBytesRead} total-byte read limit.");

    private static async Task<BoundedFileRead> ReadFileBoundedAsync(
        string path,
        bool captureContent,
        long maximumFileSizeBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ReadBufferSize,
            useAsync: true);

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A filesystem entry became a reparse point while it was being captured.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var captured = captureContent ? new MemoryStream() : null;
        var buffer = new byte[ReadBufferSize];
        long length = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            if (length > maximumFileSizeBytes - bytesRead)
            {
                throw new FileSizeLimitExceededException();
            }

            length += bytesRead;
            hash.AppendData(buffer, 0, bytesRead);
            if (captured is not null)
            {
                await captured.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }

        return new BoundedFileRead(
            length,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            captured?.ToArray());
    }

    private sealed record BoundedFileRead(long Length, string ContentHash, byte[]? CapturedContent);

    private sealed class FileSizeLimitExceededException : IOException;
}
