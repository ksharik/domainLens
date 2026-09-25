namespace DomainLens.Scanner.Internal;

internal enum ScannerIssueSeverity
{
    Information,
    Warning,
    Error,
}

internal sealed record ScannerIssue(
    string Code,
    ScannerIssueSeverity Severity,
    string Message,
    string? RelativePath = null,
    int? Line = null,
    int? Column = null);
