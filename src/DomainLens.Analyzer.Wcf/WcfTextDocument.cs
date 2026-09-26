using System.Text;
using System.Xml;
using System.Xml.Linq;
using DomainLens.Core;

namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// A bounded in-memory view of manifest-verified text. Offsets are UTF-16 offsets, matching
/// Roslyn and the Evidence Graph source-span contract.
/// </summary>
internal sealed class WcfTextDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(
        bigEndian: true,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private readonly int[] _lineStarts;

    private WcfTextDocument(string text, WcfTextEncodingKind encoding)
    {
        Text = text;
        Encoding = encoding;
        _lineStarts = GetLineStarts(text);
    }

    public string Text { get; }

    public WcfTextEncodingKind Encoding { get; }

    public static WcfTextDecodeResult TryDecode(ReadOnlyMemory<byte> content)
    {
        var bytes = content.Span;
        if (HasPrefix(bytes, 0x00, 0x00, 0xfe, 0xff) ||
            HasPrefix(bytes, 0xff, 0xfe, 0x00, 0x00) ||
            HasPrefix(bytes, 0x00, 0x00, 0xff, 0xfe) ||
            HasPrefix(bytes, 0xfe, 0xff, 0x00, 0x00))
        {
            return WcfTextDecodeResult.Unsupported("utf-32-bom");
        }

        if (HasPrefix(bytes, 0xef, 0xbb, 0xbf))
        {
            return Decode(
                bytes[3..],
                StrictUtf8,
                WcfTextEncodingKind.Utf8Bom,
                "utf-8-bom");
        }

        if (HasPrefix(bytes, 0xff, 0xfe))
        {
            return Decode(
                bytes[2..],
                StrictUtf16LittleEndian,
                WcfTextEncodingKind.Utf16LittleEndianBom,
                "utf-16le-bom");
        }

        if (HasPrefix(bytes, 0xfe, 0xff))
        {
            return Decode(
                bytes[2..],
                StrictUtf16BigEndian,
                WcfTextEncodingKind.Utf16BigEndianBom,
                "utf-16be-bom");
        }

        if (HasBomlessUnicodeSignature(bytes))
        {
            return WcfTextDecodeResult.Unsupported("bomless-unicode");
        }

        return Decode(bytes, StrictUtf8, WcfTextEncodingKind.Utf8, "utf-8");
    }

    public WcfXmlEncodingDeclarationStatus ValidateXmlEncodingDeclaration(
        string? declaredEncoding)
    {
        if (string.IsNullOrWhiteSpace(declaredEncoding))
        {
            return WcfXmlEncodingDeclarationStatus.Compatible;
        }

        var normalized = declaredEncoding.Trim();
        var supported = normalized.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("utf8", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("utf-16", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("utf-16le", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals("utf-16be", StringComparison.OrdinalIgnoreCase);
        if (!supported)
        {
            return WcfXmlEncodingDeclarationStatus.Unsupported;
        }

        return Encoding switch
        {
            WcfTextEncodingKind.Utf8 or WcfTextEncodingKind.Utf8Bom
                when normalized.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("utf8", StringComparison.OrdinalIgnoreCase) =>
                WcfXmlEncodingDeclarationStatus.Compatible,
            WcfTextEncodingKind.Utf16LittleEndianBom
                when normalized.Equals("utf-16", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("utf-16le", StringComparison.OrdinalIgnoreCase) =>
                WcfXmlEncodingDeclarationStatus.Compatible,
            WcfTextEncodingKind.Utf16BigEndianBom
                when normalized.Equals("utf-16", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("utf-16be", StringComparison.OrdinalIgnoreCase) =>
                WcfXmlEncodingDeclarationStatus.Compatible,
            _ => WcfXmlEncodingDeclarationStatus.Incompatible,
        };
    }

    public SourceSpan Span(int startOffset, int length)
    {
        var boundedStart = Math.Clamp(startOffset, 0, Text.Length);
        var boundedLength = Math.Clamp(length, 0, Text.Length - boundedStart);
        var endOffset = boundedStart + boundedLength;
        var start = GetLinePosition(boundedStart);
        var end = GetLinePosition(endOffset);
        return new SourceSpan(
            boundedStart,
            boundedLength,
            start.Line,
            start.Column,
            end.Line,
            end.Column);
    }

    public SourceSpan OpeningTagSpan(XElement element)
    {
        var start = OffsetFor(element);
        var end = FindMarkupEnd(start, '>');
        return Span(start, end < 0 ? Math.Max(1, element.Name.LocalName.Length) : end - start + 1);
    }

    public SourceSpan AttributeSpan(XAttribute attribute)
    {
        var start = OffsetFor(attribute);
        var index = start;
        char? quote = null;
        while (index < Text.Length)
        {
            var character = Text[index];
            if (quote is null)
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                }
                else if (character is '>' or '<')
                {
                    break;
                }
            }
            else if (character == quote)
            {
                index++;
                return Span(start, index - start);
            }

            index++;
        }

        return Span(start, Math.Max(1, attribute.Name.LocalName.Length));
    }

    private int OffsetFor(XObject value)
    {
        if (value is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            throw new InvalidDataException("Parsed WCF XML did not retain source line information.");
        }

        var lineIndex = Math.Clamp(lineInfo.LineNumber - 1, 0, _lineStarts.Length - 1);
        return Math.Clamp(
            _lineStarts[lineIndex] + Math.Max(0, lineInfo.LinePosition - 1),
            0,
            Text.Length);
    }

    private int FindMarkupEnd(int start, char terminator)
    {
        char? quote = null;
        for (var index = start; index < Text.Length; index++)
        {
            var character = Text[index];
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == terminator)
            {
                return index;
            }
        }

        return -1;
    }

    private LinePosition GetLinePosition(int offset)
    {
        var index = Array.BinarySearch(_lineStarts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Max(0, index);
        return new LinePosition(index + 1, offset - _lineStarts[index] + 1);
    }

    private static int[] GetLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                starts.Add(index + 1);
            }
            else if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private static WcfTextDecodeResult Decode(
        ReadOnlySpan<byte> content,
        Encoding decoder,
        WcfTextEncodingKind encoding,
        string encodingLabel)
    {
        try
        {
            return WcfTextDecodeResult.Success(
                new WcfTextDocument(decoder.GetString(content), encoding),
                encodingLabel);
        }
        catch (DecoderFallbackException)
        {
            return WcfTextDecodeResult.Invalid(encodingLabel);
        }
    }

    private static bool HasPrefix(ReadOnlySpan<byte> content, params byte[] prefix) =>
        content.StartsWith(prefix);

    private static bool HasBomlessUnicodeSignature(ReadOnlySpan<byte> content)
    {
        // XML/configuration and ASP.NET directives begin with '<'. Recognize the supported
        // artifact marker in BOM-less UTF-16/UTF-32 forms so it cannot be misinterpreted as UTF-8
        // containing NUL characters. Manifest-verified reads are already bounded, so inspect the
        // complete artifact rather than allowing a long leading prefix to hide the signature. No
        // arbitrary code-page or content guessing occurs.
        for (var index = 0; index < content.Length - 1; index++)
        {
            if ((content[index] == 0x3c && content[index + 1] == 0x00) ||
                (content[index] == 0x00 && content[index + 1] == 0x3c) ||
                (index < content.Length - 3 &&
                 ((content[index] == 0x3c && content[index + 1] == 0x00 &&
                   content[index + 2] == 0x00 && content[index + 3] == 0x00) ||
                  (content[index] == 0x00 && content[index + 1] == 0x00 &&
                   content[index + 2] == 0x00 && content[index + 3] == 0x3c) ||
                  (content[index] == 0x00 && content[index + 1] == 0x00 &&
                   content[index + 2] == 0x3c && content[index + 3] == 0x00) ||
                  (content[index] == 0x00 && content[index + 1] == 0x3c &&
                   content[index + 2] == 0x00 && content[index + 3] == 0x00))))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct LinePosition(int Line, int Column);
}

internal enum WcfTextEncodingKind
{
    Utf8,
    Utf8Bom,
    Utf16LittleEndianBom,
    Utf16BigEndianBom,
}

internal enum WcfTextDecodeStatus
{
    Success,
    Invalid,
    Unsupported,
}

internal enum WcfXmlEncodingDeclarationStatus
{
    Compatible,
    Unsupported,
    Incompatible,
}

internal sealed record WcfTextDecodeResult(
    WcfTextDecodeStatus Status,
    WcfTextDocument? Document,
    string EncodingLabel)
{
    public bool IsSuccess => Status == WcfTextDecodeStatus.Success;

    public static WcfTextDecodeResult Success(WcfTextDocument document, string encodingLabel) =>
        new(WcfTextDecodeStatus.Success, document, encodingLabel);

    public static WcfTextDecodeResult Invalid(string encodingLabel) =>
        new(WcfTextDecodeStatus.Invalid, null, encodingLabel);

    public static WcfTextDecodeResult Unsupported(string encodingLabel) =>
        new(WcfTextDecodeStatus.Unsupported, null, encodingLabel);
}
