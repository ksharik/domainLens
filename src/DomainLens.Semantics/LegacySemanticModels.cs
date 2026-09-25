using DomainLens.Core;
using System.Text.Json.Serialization;

namespace DomainLens.Semantics;

/// <summary>The source construct whose compiler binding was observed.</summary>
public enum SemanticObservationKind
{
    DeclaredType,
    BaseType,
    ImplementedInterface,
    AttributeType,
    Invocation,
    MemberAccess
}

/// <summary>The source scope used to construct the semantic feasibility compilation.</summary>
public enum SemanticCompilationScope
{
    RepositoryManifestCSharpSources
}

/// <summary>A stable, strongly typed category for a semantic-analysis diagnostic.</summary>
public enum SemanticDiagnosticCode
{
    InvalidWorkspaceRoot,
    DuplicateManifestPath,
    UnsafeManifestPath,
    ManifestFileMissing,
    ManifestLengthMismatch,
    ManifestHashMismatch,
    SourceReadFailed,
    TrustedReferenceDirectoryMissing,
    TrustedReferenceLoadFailed,
    ParseError,
    CompilationError,
    CompilationWarning
}

/// <summary>A compiler binding derived from a manifest-verified source span.</summary>
public sealed record SemanticBindingObservation(
    SemanticObservationKind Kind,
    string RelativePath,
    SourceSpan Span,
    string SourceExpression,
    string? ContainingSymbol,
    string? ResolvedSymbol,
    string? ResolvedAssembly,
    ResolutionQuality Quality,
    IReadOnlyList<string> CandidateSymbols,
    string? Details);

/// <summary>A trusted, tool-owned metadata reference supplied directly to Roslyn.</summary>
public sealed record TrustedMetadataReference(
    string AssemblyName,
    string RelativePath);

/// <summary>A deterministic problem or degradation discovered during semantic analysis.</summary>
public sealed record SemanticAnalysisDiagnostic(
    SemanticDiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message,
    string? RelativePath,
    SourceSpan? Span,
    string? CompilerDiagnosticId = null);

/// <summary>The non-graph result of the legacy semantic-enrichment feasibility slice.</summary>
public sealed record LegacySemanticAnalysisResult(
    IReadOnlyList<SemanticBindingObservation> Observations,
    IReadOnlyList<SemanticAnalysisDiagnostic> Diagnostics,
    IReadOnlyList<TrustedMetadataReference> MetadataReferences,
    string ReferenceSetId)
{
    /// <summary>
    /// Milestone 0 intentionally flattens every manifest-listed C# source into one synthetic
    /// compilation; it does not claim effective project or dependency selection.
    /// </summary>
    [JsonRequired]
    public SemanticCompilationScope CompilationScope { get; init; } =
        SemanticCompilationScope.RepositoryManifestCSharpSources;

    /// <summary>Repository-wide synthetic compilation scope is explicitly partial.</summary>
    [JsonRequired]
    public ResolutionQuality CompilationResolutionQuality { get; init; } = ResolutionQuality.Partial;
}
