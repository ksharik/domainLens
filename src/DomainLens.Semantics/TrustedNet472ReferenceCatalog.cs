using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DomainLens.Semantics;

/// <summary>
/// The versioned catalog of compiler metadata that DomainLens owns and ships for the net472
/// feasibility profile. Repository files can never extend this catalog.
/// </summary>
public static class TrustedNet472ReferenceCatalog
{
    public const string ReferenceSetId =
        "microsoft.netframework.referenceassemblies.net472/1.0.3";

    private const string ReferenceSubtree = "trusted-reference-assemblies";
    private const string Net472Subtree = "net472";

    // Generated from package 1.0.3 by hashing each sorted DLL relative path, byte length, and
    // SHA-256 content hash. This pins both the expected file set and every compiler input byte.
    private const string ExpectedCatalogContentSha256 =
        "b008b37e7d813d60d2f3a062987097a16ab971d72ab110685726dd4ac14bc300";

    private static readonly HashSet<string> KnownNonMetadataFiles = new(StringComparer.Ordinal)
    {
        "System.EnterpriseServices.Thunk.dll",
        "System.EnterpriseServices.Wrapper.dll"
    };

    private static readonly Lazy<TrustedReferenceCatalogSnapshot> Catalog =
        new(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns a defensive copy of the exact metadata-reference descriptors shipped beside this
    /// DomainLens binary. It throws if that tool-owned catalog is missing or corrupt.
    /// </summary>
    public static IReadOnlyList<TrustedMetadataReference> GetReferenceDescriptors()
    {
        var catalog = Catalog.Value;
        if (!catalog.IsComplete)
        {
            throw new InvalidOperationException(
                "The tool-owned .NET Framework 4.7.2 reference catalog is unavailable or corrupt.");
        }

        return catalog.Entries
            .Select(entry => entry.Descriptor)
            .ToArray();
    }

    internal static TrustedReferenceCatalogSnapshot GetSnapshot() => Catalog.Value;

    private static TrustedReferenceCatalogSnapshot LoadCatalog()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(TrustedNet472ReferenceCatalog).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(assemblyDirectory, ReferenceSubtree, Net472Subtree));
        return LoadCatalogFromRoot(root);
    }

    internal static TrustedReferenceCatalogSnapshot LoadCatalogFromRoot(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            return new TrustedReferenceCatalogSnapshot(root, false, Array.Empty<TrustedReferenceCatalogEntry>(), 0);
        }

        IEnumerable<string> paths;
        try
        {
            var facades = Path.Combine(root, "Facades");
            paths = Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
                .Concat(Directory.Exists(facades)
                    ? Directory.EnumerateFiles(facades, "*.dll", SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>())
                .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or PathTooLongException)
        {
            return new TrustedReferenceCatalogSnapshot(root, true, Array.Empty<TrustedReferenceCatalogEntry>(), 1);
        }

        var files = new List<TrustedCatalogFile>();
        try
        {
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (!IsWithinRoot(root, fullPath))
                {
                    throw new InvalidOperationException("A trusted reference escaped the tool-owned catalog root.");
                }

                var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
                if (!IsCatalogPath(relativePath))
                {
                    throw new InvalidOperationException("A trusted reference has an unsupported catalog path.");
                }

                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);
                var length = stream.Length;
                var contentHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                files.Add(new TrustedCatalogFile(relativePath, fullPath, length, contentHash));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            return new TrustedReferenceCatalogSnapshot(root, true, Array.Empty<TrustedReferenceCatalogEntry>(), 1);
        }

        var actualCatalogHash = ComputeCatalogContentHash(files);
        if (!string.Equals(
                actualCatalogHash,
                ExpectedCatalogContentSha256,
                StringComparison.Ordinal))
        {
            return new TrustedReferenceCatalogSnapshot(root, true, Array.Empty<TrustedReferenceCatalogEntry>(), 1);
        }

        var entries = new List<TrustedReferenceCatalogEntry>();
        foreach (var file in files)
        {
            if (KnownNonMetadataFiles.Contains(file.RelativePath))
            {
                continue;
            }

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(file.FullPath).Name;
                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    throw new BadImageFormatException("A trusted reference has no assembly identity.");
                }

                _ = MetadataReference.CreateFromFile(file.FullPath).GetMetadata();
                entries.Add(new TrustedReferenceCatalogEntry(
                    new TrustedMetadataReference(assemblyName, file.RelativePath),
                    file.FullPath));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or BadImageFormatException or
                    ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return new TrustedReferenceCatalogSnapshot(
                    root,
                    true,
                    Array.Empty<TrustedReferenceCatalogEntry>(),
                    1);
            }
        }

        var duplicateDescriptors = entries
            .GroupBy(entry => entry.Descriptor.RelativePath, StringComparer.Ordinal)
            .Any(group => group.Count() != 1) ||
            entries.GroupBy(entry => entry.Descriptor.AssemblyName, StringComparer.Ordinal)
                .Any(group => group.Count() != 1);
        if (duplicateDescriptors)
        {
            return new TrustedReferenceCatalogSnapshot(
                root,
                true,
                Array.Empty<TrustedReferenceCatalogEntry>(),
                1);
        }

        return new TrustedReferenceCatalogSnapshot(
            root,
            true,
            entries
                .OrderBy(entry => entry.Descriptor.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            0);
    }

    private static string ComputeCatalogContentHash(IEnumerable<TrustedCatalogFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            AppendCatalogField(hash, file.RelativePath);
            AppendCatalogField(hash, file.Length.ToString(CultureInfo.InvariantCulture));
            AppendCatalogField(hash, file.ContentHash);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendCatalogField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsCatalogPath(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length switch
        {
            1 => segments[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            2 => string.Equals(segments[0], "Facades", StringComparison.Ordinal) &&
                 segments[1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            comparison);
    }
}

internal sealed record TrustedReferenceCatalogEntry(
    TrustedMetadataReference Descriptor,
    string FullPath);

internal sealed record TrustedCatalogFile(
    string RelativePath,
    string FullPath,
    long Length,
    string ContentHash);

internal sealed record TrustedReferenceCatalogSnapshot(
    string RootPath,
    bool DirectoryExists,
    IReadOnlyList<TrustedReferenceCatalogEntry> Entries,
    int FailureCount)
{
    public bool IsComplete => DirectoryExists && FailureCount == 0 && Entries.Count > 0;
}
