namespace DomainLens.Scanner.Internal;

internal sealed record CapturedSpan(
    int StartOffset,
    int Length,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public static CapturedSpan FromLine(
        string text,
        int oneBasedLine,
        int oneBasedColumn,
        int requestedLength)
    {
        var lineStarts = BuildLineStarts(text);
        if (oneBasedLine < 1 || oneBasedLine > lineStarts.Count)
        {
            return new CapturedSpan(0, 0, 1, 1, 1, 1);
        }

        var lineIndex = oneBasedLine - 1;
        var start = Math.Min(
            text.Length,
            lineStarts[lineIndex] + Math.Max(0, oneBasedColumn - 1));
        var length = Math.Min(Math.Max(0, requestedLength), text.Length - start);
        return new CapturedSpan(
            start,
            length,
            oneBasedLine,
            Math.Max(1, oneBasedColumn),
            oneBasedLine,
            Math.Max(1, oneBasedColumn) + length);
    }

    public static CapturedSpan FromOffsets(string text, int requestedStart, int requestedLength)
    {
        var start = Math.Clamp(requestedStart, 0, text.Length);
        var length = Math.Clamp(requestedLength, 0, text.Length - start);
        var (startLine, startColumn) = PositionAt(text, start);
        var (endLine, endColumn) = PositionAt(text, start + length);
        return new CapturedSpan(start, length, startLine, startColumn, endLine, endColumn);
    }

    private static IReadOnlyList<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts;
    }

    private static (int Line, int Column) PositionAt(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}
