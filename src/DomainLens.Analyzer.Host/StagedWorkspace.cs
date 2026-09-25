using System.Security.Cryptography;
using DomainLens.Core;

namespace DomainLens.Analyzer.Host;

internal static class StagedWorkspace
{
    private static readonly HashSet<string> AnalyzerExcludedDirectoryNames = new(PathComparer)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "TestResults",
    };

    public static async Task<StagedRepositoryState> CopyRepositoryAsync(
        string sourcePath,
        string destinationPath,
        AnalyzerProcessLimits limits,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(sourcePath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        if (!Directory.Exists(sourceRoot))
        {
            throw new StagingRejectedException("The source repository directory does not exist.");
        }

        if (HasReparsePoint(sourceRoot))
        {
            throw new StagingRejectedException(
                "The source repository root is a reparse point and cannot be staged safely.");
        }

        if (IsWithin(destinationRoot, sourceRoot))
        {
            throw new StagingRejectedException(
                "The job workspace must not be created inside the source repository.");
        }

        Directory.CreateDirectory(destinationRoot);
        var pending = new Stack<PendingDirectory>();
        pending.Push(new PendingDirectory(sourceRoot, destinationRoot, 0));

        var integrityEntries = new List<StagedIntegrityEntry>();
        var analysisManifest = new List<ManifestEntry>();
        var entryCount = 0;
        var fileCount = 0;
        long totalBytes = 0;
        var buffer = new byte[64 * 1024];

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in EnumerateEntries(
                         current.Source,
                         ref entryCount,
                         limits.MaximumStagedEntryCount,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StagingRejectedException(
                        $"Repository reparse points are not staged: '{entry.Name}'.");
                }

                var relativeDepth = current.RelativeDepth + 1;
                EnsureDepth(relativeDepth, limits.MaximumStagedRelativeDepth);
                var destination = Path.Combine(current.Destination, entry.Name);
                var relativePath = NormalizeRelativePath(
                    Path.GetRelativePath(sourceRoot, entry.FullName));

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (AnalyzerExcludedDirectoryNames.Contains(entry.Name))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(destination);
                    integrityEntries.Add(new StagedIntegrityEntry(
                        relativePath, StagedEntryKind.Directory, 0, string.Empty));
                    pending.Push(new PendingDirectory(
                        entry.FullName,
                        destination,
                        relativeDepth));
                    continue;
                }

                CountFile(ref fileCount, limits.MaximumStagedFileCount);
                var file = new FileInfo(entry.FullName);
                if (file.Length > limits.MaximumStagedFileSizeBytes)
                {
                    throw new StagingRejectedException(
                        $"A repository file exceeds the {limits.MaximumStagedFileSizeBytes} byte staging limit.");
                }

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var source = new FileStream(
                    entry.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    buffer.Length,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if ((File.GetAttributes(entry.FullName) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StagingRejectedException(
                        $"A repository entry became a reparse point while it was staged: '{entry.Name}'.");
                }

                await using var target = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    buffer.Length,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                long copiedFileBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (copiedFileBytes > limits.MaximumStagedFileSizeBytes - read)
                    {
                        throw new StagingRejectedException(
                            $"A repository file exceeds the {limits.MaximumStagedFileSizeBytes} byte staging limit.");
                    }

                    if (totalBytes > limits.MaximumStagedTotalBytes - read)
                    {
                        throw new StagingRejectedException(
                            $"The repository exceeds the {limits.MaximumStagedTotalBytes} byte staging limit.");
                    }

                    copiedFileBytes += read;
                    totalBytes += read;
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                }

                var contentHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                integrityEntries.Add(new StagedIntegrityEntry(
                    relativePath, StagedEntryKind.File, copiedFileBytes, contentHash));
                analysisManifest.Add(new ManifestEntry(relativePath, contentHash, copiedFileBytes));
            }
        }

        return CreateState(integrityEntries, analysisManifest);
    }

    public static async Task<bool> IsRepositoryUnchangedAsync(
        string repositoryPath,
        StagedRepositoryState expected,
        AnalyzerProcessLimits limits,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureRepositoryStateAsync(repositoryPath, limits, cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(
            actual.IntegrityFingerprint,
            expected.IntegrityFingerprint,
            StringComparison.Ordinal);
    }

    public static (AnalyzerWorkspaceCleanupStatus Status, string? Message) TryDelete(
        string workspacePath,
        int maximumEntryCount)
    {
        if (!Directory.Exists(workspacePath) && !File.Exists(workspacePath))
        {
            return (AnalyzerWorkspaceCleanupStatus.Succeeded, null);
        }

        try
        {
            DeleteTreeIteratively(workspacePath, maximumEntryCount);
            return Directory.Exists(workspacePath) || File.Exists(workspacePath)
                ? (AnalyzerWorkspaceCleanupStatus.Failed, "The job workspace still exists after cleanup.")
                : (AnalyzerWorkspaceCleanupStatus.Succeeded, null);
        }
        catch (Exception exception)
        {
            return (
                AnalyzerWorkspaceCleanupStatus.Failed,
                $"The job workspace could not be removed: {Sanitize(exception.Message)}");
        }
    }

    private static async Task<StagedRepositoryState> CaptureRepositoryStateAsync(
        string repositoryPath,
        AnalyzerProcessLimits limits,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root) || HasReparsePoint(root))
        {
            throw new StagingRejectedException(
                "The staged repository root was removed or replaced with a reparse point.");
        }

        var pending = new Stack<CaptureDirectory>();
        pending.Push(new CaptureDirectory(root, 0, true));
        var integrityEntries = new List<StagedIntegrityEntry>();
        var analysisManifest = new List<ManifestEntry>();
        var entryCount = 0;
        var fileCount = 0;
        long totalBytes = 0;
        var buffer = new byte[64 * 1024];

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in EnumerateEntries(
                         current.Path,
                         ref entryCount,
                         limits.MaximumStagedEntryCount,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StagingRejectedException(
                        $"The staged repository contains a reparse point: '{entry.Name}'.");
                }

                var relativeDepth = current.RelativeDepth + 1;
                EnsureDepth(relativeDepth, limits.MaximumStagedRelativeDepth);
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, entry.FullName));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    integrityEntries.Add(new StagedIntegrityEntry(
                        relativePath, StagedEntryKind.Directory, 0, string.Empty));
                    pending.Push(new CaptureDirectory(
                        entry.FullName,
                        relativeDepth,
                        current.AnalyzerVisible &&
                        !AnalyzerExcludedDirectoryNames.Contains(entry.Name)));
                    continue;
                }

                CountFile(ref fileCount, limits.MaximumStagedFileCount);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var stream = new FileStream(
                    entry.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    buffer.Length,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if ((File.GetAttributes(entry.FullName) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StagingRejectedException(
                        $"A staged repository entry became a reparse point: '{entry.Name}'.");
                }

                long length = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (length > limits.MaximumStagedFileSizeBytes - read ||
                        totalBytes > limits.MaximumStagedTotalBytes - read)
                    {
                        throw new StagingRejectedException(
                            "The staged repository exceeded its configured byte bounds after worker execution.");
                    }

                    length += read;
                    totalBytes += read;
                    hash.AppendData(buffer, 0, read);
                }

                var contentHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                integrityEntries.Add(new StagedIntegrityEntry(
                    relativePath, StagedEntryKind.File, length, contentHash));
                if (current.AnalyzerVisible)
                {
                    analysisManifest.Add(new ManifestEntry(relativePath, contentHash, length));
                }
            }
        }

        return CreateState(integrityEntries, analysisManifest);
    }

    private static StagedRepositoryState CreateState(
        IEnumerable<StagedIntegrityEntry> integrityEntries,
        IEnumerable<ManifestEntry> analysisManifest)
    {
        var orderedEntries = integrityEntries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Kind)
            .ToArray();
        var identityParts = new List<string?>();
        foreach (var entry in orderedEntries)
        {
            identityParts.Add(entry.Kind.ToString());
            identityParts.Add(entry.RelativePath);
            identityParts.Add(entry.ContentHash);
            identityParts.Add(entry.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return new StagedRepositoryState(
            RepositorySnapshot.Create(null, analysisManifest),
            CanonicalIdentity.Create("staged-repository", identityParts),
            orderedEntries.Length);
    }

    private static FileSystemInfo[] EnumerateEntries(
        string directory,
        ref int entryCount,
        int maximumEntryCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = new List<FileSystemInfo>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0,
            };
            foreach (var entry in new DirectoryInfo(directory)
                         .EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CountEntry(ref entryCount, maximumEntryCount);
                entries.Add(entry);
            }

            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            return entries.ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            throw new StagingRejectedException(
                $"A repository directory could not be enumerated: {Sanitize(exception.Message)}",
                exception);
        }
    }

    private static FileAttributes GetAttributes(FileSystemInfo entry)
    {
        try
        {
            return entry.Attributes;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new StagingRejectedException(
                $"A repository entry could not be inspected: {Sanitize(exception.Message)}",
                exception);
        }
    }

    private static void CountEntry(ref int count, int maximum)
    {
        if (count >= maximum)
        {
            throw new StagingRejectedException(
                $"The repository exceeds the {maximum} filesystem-entry staging limit.");
        }

        count++;
    }

    private static void CountFile(ref int count, int maximum)
    {
        if (count >= maximum)
        {
            throw new StagingRejectedException(
                $"The repository exceeds the {maximum} file staging limit.");
        }

        count++;
    }

    private static void EnsureDepth(int relativeDepth, int maximum)
    {
        if (relativeDepth > maximum)
        {
            throw new StagingRejectedException(
                $"The repository exceeds the {maximum} relative-depth staging limit.");
        }
    }

    private static void DeleteTreeIteratively(string path, int maximumEntryCount)
    {
        if (maximumEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
        }

        var pending = new Stack<FileSystemInfo>();
        var discovered = new List<FileSystemInfo>();
        pending.Push(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path));
        var scheduledEntryCount = 1;

        while (pending.Count > 0)
        {
            var entry = pending.Pop();
            entry.Refresh();
            discovered.Add(entry);
            if (discovered.Count > maximumEntryCount)
            {
                throw new IOException(
                    $"Cleanup exceeded the bounded {maximumEntryCount} filesystem-entry limit.");
            }

            if (entry is not DirectoryInfo directory ||
                (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var child in directory.EnumerateFileSystemInfos(
                         "*",
                         new EnumerationOptions
                         {
                             RecurseSubdirectories = false,
                             IgnoreInaccessible = false,
                             ReturnSpecialDirectories = false,
                             AttributesToSkip = 0,
                         }))
            {
                if (scheduledEntryCount >= maximumEntryCount)
                {
                    throw new IOException(
                        $"Cleanup exceeded the bounded {maximumEntryCount} filesystem-entry limit.");
                }

                scheduledEntryCount++;
                pending.Push(child);
            }
        }

        for (var index = discovered.Count - 1; index >= 0; index--)
        {
            var entry = discovered[index];
            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes = attributes & ~FileAttributes.ReadOnly;
            }

            if (entry is DirectoryInfo directory)
            {
                directory.Delete(recursive: false);
            }
            else
            {
                entry.Delete();
            }
        }
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal static bool IsWithin(string candidatePath, string rootPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string NormalizeRelativePath(string path) =>
        CanonicalIdentity.NormalizeRepositoryPath(path);

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record PendingDirectory(
        string Source,
        string Destination,
        int RelativeDepth);

    private sealed record CaptureDirectory(string Path, int RelativeDepth, bool AnalyzerVisible);

    private sealed record StagedIntegrityEntry(
        string RelativePath,
        StagedEntryKind Kind,
        long Length,
        string ContentHash);

    private enum StagedEntryKind
    {
        Directory,
        File,
    }
}

internal sealed record StagedRepositoryState(
    RepositorySnapshot ExpectedAnalysisSnapshot,
    string IntegrityFingerprint,
    int EntryCount);

internal sealed class StagingRejectedException : Exception
{
    public StagingRejectedException(string message)
        : base(message)
    {
    }

    public StagingRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
