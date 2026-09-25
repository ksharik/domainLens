using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DomainLens.Core;

/// <summary>Creates stable SHA-256 identities without delimiter-collision ambiguity.</summary>
public static class CanonicalIdentity
{
    private const byte NullMarker = 0;
    private const byte ValueMarker = 1;

    /// <summary>Returns a lowercase hexadecimal SHA-256 digest for the supplied bytes.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Returns a lowercase hexadecimal SHA-256 digest for UTF-8 text.</summary>
    public static string Sha256Hex(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Sha256Hex(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Creates an application-owned identity in the form <c>kind:sha256</c>. Each normalized
    /// component is length-prefixed, so different component boundaries cannot collide.
    /// </summary>
    public static string Create(string kind, params string?[] components) =>
        Create(kind, (IEnumerable<string?>)components);

    /// <inheritdoc cref="Create(string,string?[])" />
    public static string Create(string kind, IEnumerable<string?> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(components);

        var normalizedKind = kind.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, normalizedKind);
        foreach (var component in components)
        {
            Append(hash, component?.Normalize(NormalizationForm.FormC));
        }

        return $"{normalizedKind}:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    /// <summary>Creates a deterministic snapshot and sorts its manifest by normalized path.</summary>
    public static RepositorySnapshot CreateSnapshot(
        string? revision,
        IEnumerable<ManifestEntry> manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var entries = manifest
            .Select(entry => entry with
            {
                Path = NormalizeRepositoryPath(entry.Path),
                ContentHash = entry.ContentHash.Trim().ToLowerInvariant()
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        var identityParts = new List<string?> { revision };
        foreach (var entry in entries)
        {
            identityParts.Add(entry.Path);
            identityParts.Add(entry.ContentHash);
            identityParts.Add(entry.Length.ToString(CultureInfo.InvariantCulture));
        }

        return new RepositorySnapshot(Create("snapshot", identityParts), revision, entries);
    }

    /// <summary>Creates the stable identity for a source-backed evidence record.</summary>
    public static string CreateEvidenceId(
        string snapshotId,
        string relativePath,
        string contentHash,
        int startOffset,
        int length,
        string extractorId,
        string extractorVersion,
        string ruleId,
        ResolutionBasis basis,
        ResolutionQuality quality) =>
        Create(
            "evidence",
            snapshotId,
            relativePath,
            contentHash,
            startOffset.ToString(CultureInfo.InvariantCulture),
            length.ToString(CultureInfo.InvariantCulture),
            extractorId,
            extractorVersion,
            ruleId,
            basis.ToString(),
            quality.ToString());

    /// <summary>
    /// Creates a repository-independent logical node identity from fields persisted on the node
    /// and, for project-owned nodes, the persisted qualified name of its project node.
    /// </summary>
    public static string CreateLogicalNodeId(
        string? projectQualifiedName,
        string kind,
        string qualifiedName) =>
        Create("logical-node", projectQualifiedName, kind, qualifiedName);

    /// <summary>Creates the snapshot-specific identity for a logical node.</summary>
    public static string CreateNodeId(string snapshotId, string logicalId) =>
        Create("node", snapshotId, logicalId);

    /// <summary>Creates the stable identity for a graph edge.</summary>
    public static string CreateEdgeId(
        string snapshotId,
        string kind,
        string fromNodeId,
        string? toNodeId,
        string? unresolvedTarget,
        ResolutionBasis basis,
        ResolutionQuality quality,
        IEnumerable<string> evidenceIds)
    {
        ArgumentNullException.ThrowIfNull(evidenceIds);

        var identityParts = new List<string?>
        {
            snapshotId,
            kind,
            fromNodeId,
            toNodeId,
            unresolvedTarget,
            basis.ToString(),
            quality.ToString()
        };
        identityParts.AddRange(evidenceIds.OrderBy(value => value, StringComparer.Ordinal));
        return Create("edge", identityParts);
    }

    /// <summary>
    /// Converts repository separators to forward slashes and removes harmless dot segments.
    /// Rooted paths and parent traversal are rejected to keep provenance repository-relative.
    /// </summary>
    public static string NormalizeRepositoryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var candidate = path.Replace('\\', '/').Trim();
        if (candidate.StartsWith("/", StringComparison.Ordinal) ||
            (candidate.Length >= 2 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':') ||
            candidate.Contains('\0'))
        {
            throw new ArgumentException("Repository paths must be relative and contain no null characters.", nameof(path));
        }

        var segments = new List<string>();
        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new ArgumentException("Repository paths cannot traverse above the repository root.", nameof(path));
            }

            segments.Add(segment.Normalize(NormalizationForm.FormC));
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("Repository paths must identify a file.", nameof(path));
        }

        return string.Join('/', segments);
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            hash.AppendData(new[] { NullMarker });
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> header = stackalloc byte[5];
        header[0] = ValueMarker;
        BinaryPrimitives.WriteInt32BigEndian(header[1..], bytes.Length);
        hash.AppendData(header);
        hash.AppendData(bytes);
    }
}
