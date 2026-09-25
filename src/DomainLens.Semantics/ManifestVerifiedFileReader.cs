using System.Security.Cryptography;
using DomainLens.Core;

namespace DomainLens.Semantics;

/// <summary>The deterministic outcome of reading one manifest-listed repository file.</summary>
public enum ManifestVerifiedFileReadStatus
{
    Success,
    InvalidWorkspaceRoot,
    UnsafeManifestPath,
    FileMissing,
    LengthMismatch,
    HashMismatch,
    ReadFailed
}

/// <summary>
/// Bytes read from a repository file only after its path, captured length, and SHA-256 digest
/// have been revalidated against the snapshot manifest.
/// </summary>
public sealed record ManifestVerifiedFileReadResult(
    ManifestVerifiedFileReadStatus Status,
    string RelativePath,
    ReadOnlyMemory<byte> Content)
{
    public bool IsSuccess => Status == ManifestVerifiedFileReadStatus.Success;
}

/// <summary>
/// Performs bounded, manifest-verified reads without evaluating or executing repository content.
/// The reader accepts any manifest-listed regular file; callers remain responsible for selecting
/// the file extensions and interpreting the returned bytes as inert data.
/// </summary>
public sealed class ManifestVerifiedFileReader
{
    private const int ReadChunkSize = 64 * 1024;

    public async Task<ManifestVerifiedFileReadResult> ReadAsync(
        string workspaceRepositoryRoot,
        ManifestEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeRoot(workspaceRepositoryRoot, out var repositoryRoot))
        {
            return Result(
                ManifestVerifiedFileReadStatus.InvalidWorkspaceRoot,
                entry.Path,
                null);
        }

        return await ReadFromNormalizedRootAsync(repositoryRoot, entry, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<ManifestVerifiedFileReadResult> ReadFromNormalizedRootAsync(
        string repositoryRoot,
        ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveManifestPath(
                repositoryRoot,
                entry.Path,
                out var fullPath,
                out var relativePath) ||
            ContainsReparsePoint(repositoryRoot, fullPath))
        {
            return Result(
                ManifestVerifiedFileReadStatus.UnsafeManifestPath,
                entry.Path,
                null);
        }

        if (!File.Exists(fullPath))
        {
            return Result(
                ManifestVerifiedFileReadStatus.FileMissing,
                relativePath,
                null);
        }

        try
        {
            await using var source = new FileStream(
                fullPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    // The bounded reader owns the only source-content buffer and controls each
                    // requested byte count, so FileStream read-ahead is deliberately disabled.
                    BufferSize = 1
                });
            var bounded = await ReadBoundedAsync(source, entry, cancellationToken)
                .ConfigureAwait(false);
            return Result(bounded.Status, relativePath, bounded.Bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or PathTooLongException)
        {
            return Result(
                ManifestVerifiedFileReadStatus.ReadFailed,
                relativePath,
                null);
        }
    }

    internal static async Task<BoundedManifestReadResult> ReadBoundedAsync(
        Stream source,
        ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (entry.Length < 0 || entry.Length > Array.MaxLength)
        {
            return BoundedManifestReadResult.LengthMismatch;
        }

        if (!source.CanSeek)
        {
            throw new NotSupportedException("Manifest verification requires a seekable stream.");
        }

        var initialPosition = source.Position;
        var expectedEndPosition = checked(initialPosition + entry.Length);
        if (source.Length != expectedEndPosition)
        {
            return BoundedManifestReadResult.LengthMismatch;
        }

        var expectedLength = checked((int)entry.Length);
        var bytes = GC.AllocateUninitializedArray<byte>(expectedLength);
        var totalRead = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        while (totalRead < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedCount = Math.Min(ReadChunkSize, expectedLength - totalRead);
            var count = await source
                .ReadAsync(bytes.AsMemory(totalRead, requestedCount), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return BoundedManifestReadResult.LengthMismatch;
            }

            hash.AppendData(bytes.AsSpan(totalRead, count));
            totalRead += count;
        }

        // Recheck the same open handle after the exact bounded read. This detects growth during
        // the read without requesting or consuming a byte beyond the captured manifest length.
        if (source.Position != expectedEndPosition || source.Length != expectedEndPosition)
        {
            return BoundedManifestReadResult.LengthMismatch;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var contentHash = Convert.ToHexString(hash.GetHashAndReset());
        return string.Equals(contentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase)
            ? new BoundedManifestReadResult(ManifestVerifiedFileReadStatus.Success, bytes)
            : BoundedManifestReadResult.HashMismatch;
    }

    internal static bool TryNormalizeRoot(string root, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return Directory.Exists(normalized) &&
                   (File.GetAttributes(normalized) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or UnauthorizedAccessException or NotSupportedException or
                PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static bool TryResolveManifestPath(
        string repositoryRoot,
        string? candidate,
        out string fullPath,
        out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = candidate?.Replace('\\', '/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var normalized = CanonicalIdentity.NormalizeRepositoryPath(candidate);
            var resolved = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinRoot(repositoryRoot, resolved))
            {
                return false;
            }

            fullPath = resolved;
            relativePath = normalized;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static bool ContainsReparsePoint(string repositoryRoot, string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(repositoryRoot, fullPath);
            var current = repositoryRoot;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    private static ManifestVerifiedFileReadResult Result(
        ManifestVerifiedFileReadStatus status,
        string? relativePath,
        byte[]? content) =>
        new(status, relativePath?.Replace('\\', '/') ?? string.Empty, content ?? Array.Empty<byte>());
}

internal sealed record BoundedManifestReadResult(
    ManifestVerifiedFileReadStatus Status,
    byte[]? Bytes)
{
    public static BoundedManifestReadResult LengthMismatch { get; } =
        new(ManifestVerifiedFileReadStatus.LengthMismatch, null);

    public static BoundedManifestReadResult HashMismatch { get; } =
        new(ManifestVerifiedFileReadStatus.HashMismatch, null);
}
