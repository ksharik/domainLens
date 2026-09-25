namespace DomainLens.Scanner.Internal;

internal sealed record SolutionProjectEntry(
    string Name,
    string ProjectRelativePath,
    CapturedSpan Span);

internal sealed record SolutionDescriptor(
    string RelativePath,
    CapturedSpan Span,
    IReadOnlyList<SolutionProjectEntry> Projects,
    IReadOnlyList<ScannerIssue> Issues);
