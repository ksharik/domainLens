using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// Produces a deterministic, collision-resistant persisted representation for repository-controlled
/// WCF text. Analysis may use the complete manifest-bounded value in memory; only graph and
/// diagnostic persistence is abbreviated.
/// </summary>
internal static class WcfPersistedTextPolicy
{
    public const int MaximumPersistedCharacters = 1024;
    public const string TruncatedMarker = "domainlens:truncated=true";

    public static WcfPersistedTextProjection Project(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safeValue = EscapeUnpairedSurrogates(value, out var hadInvalidUtf16);
        var containedReservedMarker = value.Contains(TruncatedMarker, StringComparison.Ordinal);
        if (value.Length <= MaximumPersistedCharacters &&
            !hadInvalidUtf16 &&
            !containedReservedMarker)
        {
            return new WcfPersistedTextProjection(
                value,
                IsAbbreviated: false,
                value.Length,
                Sha256: null,
                HadInvalidUtf16: false,
                ContainedReservedMarker: false);
        }

        var sha256 = Sha256Utf16(value);
        var safetyMetadata = string.Concat(
            hadInvalidUtf16 ? ";invalidUtf16=true" : string.Empty,
            containedReservedMarker ? ";reservedMarkerEscaped=true" : string.Empty);
        var suffix = string.Create(
            CultureInfo.InvariantCulture,
            $"…[{TruncatedMarker};originalLengthUtf16={value.Length};sha256Utf16={sha256}{safetyMetadata}]");
        var prefixLength = MaximumPersistedCharacters - suffix.Length;
        if (prefixLength <= 0)
        {
            throw new InvalidOperationException("The WCF persisted-text metadata exceeds its configured bound.");
        }

        var retainedLength = Math.Min(prefixLength, safeValue.Length);
        // Do not split a valid surrogate pair at the prefix boundary. Lengths and the limit are
        // deliberately expressed in UTF-16 code units, matching SourceSpan offset semantics.
        if (retainedLength < safeValue.Length &&
            char.IsHighSurrogate(safeValue[retainedLength - 1]) &&
            char.IsLowSurrogate(safeValue[retainedLength]))
        {
            retainedLength--;
        }

        return new WcfPersistedTextProjection(
            safeValue[..retainedLength] + suffix,
            IsAbbreviated: true,
            value.Length,
            sha256,
            hadInvalidUtf16,
            containedReservedMarker);
    }

    private static string EscapeUnpairedSurrogates(string value, out bool hadInvalidUtf16)
    {
        hadInvalidUtf16 = false;
        StringBuilder? escaped = null;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (!char.IsSurrogate(current))
            {
                escaped?.Append(current);
                continue;
            }

            if (char.IsHighSurrogate(current) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                if (escaped is not null)
                {
                    escaped.Append(current);
                    escaped.Append(value[++index]);
                }
                else
                {
                    index++;
                }

                continue;
            }

            if (escaped is null)
            {
                escaped = new StringBuilder(value.Length + 16);
                escaped.Append(value, 0, index);
            }

            escaped.Append("\\u");
            escaped.Append(((int)current).ToString("X4", CultureInfo.InvariantCulture));
            hadInvalidUtf16 = true;
        }

        return escaped?.ToString() ?? value;
    }

    private static string Sha256Utf16(string value)
    {
        // Hash the exact UTF-16 code-unit sequence rather than using a replacement fallback for
        // unpaired surrogates that can legally be produced by a C# string constant. Big-endian
        // code units make the digest independent of machine endianness.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[512];
        for (var start = 0; start < value.Length; start += buffer.Length / sizeof(char))
        {
            var count = Math.Min(buffer.Length / sizeof(char), value.Length - start);
            for (var index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    buffer[(index * sizeof(char))..],
                    value[start + index]);
            }

            hash.AppendData(buffer[..(count * sizeof(char))]);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

internal sealed record WcfPersistedTextProjection(
    string Value,
    bool IsAbbreviated,
    int OriginalLengthUtf16,
    string? Sha256,
    bool HadInvalidUtf16,
    bool ContainedReservedMarker);
