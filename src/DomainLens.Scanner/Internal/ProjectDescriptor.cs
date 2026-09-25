namespace DomainLens.Scanner.Internal;

internal sealed record ProjectItemReference(
    string Include,
    string? Version,
    string? HintPath,
    string? ReferenceOutputAssembly,
    string? Aliases,
    bool IsConditional,
    CapturedSpan Span);

internal sealed record ProjectDescriptor(
    string RelativePath,
    string Name,
    string AssemblyName,
    string RootNamespace,
    bool IsSdkStyle,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> SourceRelativePaths,
    bool SourceSelectionPartial,
    bool DependencySelectionPartial,
    bool ConfigurationSelectionPartial,
    IReadOnlyList<ProjectItemReference> ProjectReferences,
    IReadOnlyList<ProjectItemReference> AssemblyReferences,
    IReadOnlyList<ProjectItemReference> PackageReferences,
    CapturedSpan Span,
    bool ParsedSuccessfully,
    IReadOnlyList<ScannerIssue> Issues);
