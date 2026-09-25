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
    private readonly int[] _lineStarts;

    private WcfTextDocument(string text)
    {
        Text = text;
        _lineStarts = GetLineStarts(text);
    }

    public string Text { get; }

    public static WcfTextDocument Decode(ReadOnlyMemory<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return new WcfTextDocument(reader.ReadToEnd());
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

    private readonly record struct LinePosition(int Line, int Column);
}
