using System.Text.RegularExpressions;

namespace DomainLens.Scanner.Internal;

internal sealed partial class SolutionFileReader
{
    [GeneratedRegex(
        "^Project\\(\"[^\"]+\"\\)\\s*=\\s*\"(?<name>[^\"]+)\",\\s*\"(?<path>[^\"]+\\.csproj)\",",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ProjectLinePattern();

    public SolutionDescriptor Read(RepositoryFile file)
    {
        var issues = new List<ScannerIssue>();
        var text = file.ReadCapturedText();
        var projects = new List<SolutionProjectEntry>();
        var solutionDirectory = GetRelativeDirectory(file.RelativePath);

        var lineNumber = 1;
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;
            var contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r'
                ? lineEnd - 1
                : lineEnd;
            var line = text[lineStart..contentEnd];
            var match = ProjectLinePattern().Match(line);
            if (match.Success)
            {
                var rawProjectPath = match.Groups["path"].Value;
                var portableProjectPath = rawProjectPath.Replace('\\', '/');
                var combined = string.IsNullOrEmpty(solutionDirectory)
                    ? portableProjectPath
                    : $"{solutionDirectory}/{portableProjectPath}";
                var normalized = PathSafety.IsPortableRelativePath(rawProjectPath)
                    ? NormalizeLogicalPath(combined)
                    : null;
                if (normalized is null)
                {
                    issues.Add(new ScannerIssue(
                        "DL2001",
                        ScannerIssueSeverity.Warning,
                        "An absolute or repository-escaping solution project entry was ignored.",
                        file.RelativePath,
                        lineNumber,
                        match.Groups["path"].Index + 1));
                }
                else
                {
                    var pathGroup = match.Groups["path"];
                    projects.Add(new SolutionProjectEntry(
                        match.Groups["name"].Value,
                        normalized,
                        new CapturedSpan(
                            lineStart + pathGroup.Index,
                            pathGroup.Length,
                            lineNumber,
                            pathGroup.Index + 1,
                            lineNumber,
                            pathGroup.Index + pathGroup.Length + 1)));
                }
            }

            if (newline < 0)
            {
                break;
            }

            lineStart = newline + 1;
            lineNumber++;
        }

        return new SolutionDescriptor(
            file.RelativePath,
            CreateWholeFileSpan(text),
            projects.OrderBy(project => project.ProjectRelativePath, StringComparer.Ordinal).ToArray(),
            issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Line)
                .ToArray());
    }

    private static string GetRelativeDirectory(string relativePath)
    {
        var lastSlash = relativePath.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : relativePath[..lastSlash];
    }

    private static string? NormalizeLogicalPath(string path)
    {
        if (!PathSafety.IsPortableRelativePath(path))
        {
            return null;
        }

        var stack = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    return null;
                }

                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        return stack.Count == 0 ? null : string.Join('/', stack);
    }

    private static CapturedSpan CreateWholeFileSpan(string text)
    {
        var endLine = 1;
        var endColumn = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                endLine++;
                endColumn = 1;
            }
            else
            {
                endColumn++;
            }
        }

        return new CapturedSpan(0, text.Length, 1, 1, endLine, endColumn);
    }
}
