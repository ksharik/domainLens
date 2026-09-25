namespace DomainLens.Core;

/// <summary>The terminal outcome of deterministic repository analysis.</summary>
public enum AnalysisStatus
{
    Success,
    PartialSuccess,
    Failure
}

/// <summary>The importance of a diagnostic produced while analyzing a repository.</summary>
public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// The deterministic mechanism used to establish an observation. Values are deliberately
/// language and framework neutral so future analyzer families can share the same model.
/// </summary>
public enum ResolutionBasis
{
    Unknown,
    Manifest,
    DeclarativeConfiguration,
    Syntax,
    Semantic,
    Metadata,
    Composite
}

/// <summary>How conclusively an analyzer resolved an observation or relationship.</summary>
public enum ResolutionQuality
{
    Exact,
    Partial,
    Ambiguous,
    Unresolved
}

/// <summary>Current schema identifier for a DomainLens deterministic analysis document.</summary>
public static class AnalysisSchema
{
    public const string CurrentVersion = "domainlens.evidence.v1";
}

/// <summary>A repository-relative file captured in the immutable analysis manifest.</summary>
public sealed record ManifestEntry(
    string Path,
    string ContentHash,
    long Length);

/// <summary>
/// Identifies the repository content analyzed by a run. The model intentionally contains no
/// absolute workspace path or run timestamp because neither is part of the source identity.
/// </summary>
public sealed record RepositorySnapshot(
    string SnapshotId,
    string? Revision,
    IReadOnlyList<ManifestEntry> Manifest)
{
    /// <summary>Creates a snapshot whose identity is derived from its revision and manifest.</summary>
    public static RepositorySnapshot Create(
        string? revision,
        IEnumerable<ManifestEntry> manifest) =>
        CanonicalIdentity.CreateSnapshot(revision, manifest);
}

/// <summary>
/// A zero-based text offset/length and one-based human-readable line/column range.
/// End coordinates are exclusive.
/// </summary>
public sealed record SourceSpan(
    int StartOffset,
    int Length,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary>Identifies the deterministic analyzer rule that produced evidence.</summary>
public sealed record ExtractorProvenance(
    string ExtractorId,
    string ExtractorVersion,
    string RuleId);

/// <summary>Records both the mechanism and certainty of deterministic resolution.</summary>
public sealed record EvidenceResolution(
    ResolutionBasis Basis,
    ResolutionQuality Quality,
    string? Details = null);

/// <summary>An exact, source-backed observation from a repository snapshot.</summary>
public sealed record EvidenceRecord(
    string EvidenceId,
    string SnapshotId,
    string RelativePath,
    string ContentHash,
    SourceSpan Span,
    ExtractorProvenance Provenance,
    EvidenceResolution Resolution);

/// <summary>
/// A language-neutral node in the deterministic Evidence Graph. Kind values are analyzer-owned
/// stable names such as Repository, Project, Namespace, Class, Method, or Property.
/// </summary>
public sealed record EvidenceNode(
    string NodeId,
    string LogicalId,
    string Kind,
    string Name,
    string QualifiedName,
    string? ProjectId,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Attributes,
    IReadOnlyDictionary<string, string> Properties);

/// <summary>
/// A deterministic relationship in the Evidence Graph. A resolved edge has a ToNodeId; an
/// unresolved edge instead retains the analyzer's stable textual target.
/// </summary>
public sealed record EvidenceEdge(
    string EdgeId,
    string Kind,
    string FromNodeId,
    string? ToNodeId,
    string? UnresolvedTarget,
    IReadOnlyList<string> EvidenceIds,
    EvidenceResolution Resolution);

/// <summary>A non-graph observation about coverage, degradation, or an analysis failure.</summary>
public sealed record AnalysisDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? RelativePath,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyDictionary<string, string> Properties)
{
    /// <summary>Creates a diagnostic with no evidence references or additional properties.</summary>
    public static AnalysisDiagnostic Create(
        string code,
        DiagnosticSeverity severity,
        string message,
        string? relativePath = null) =>
        new(
            code,
            severity,
            message,
            relativePath,
            Array.Empty<string>(),
            Empty.Properties);
}

/// <summary>The authoritative, versioned output of one deterministic repository analysis.</summary>
public sealed record AnalysisDocument(
    string SchemaVersion,
    AnalysisStatus Status,
    RepositorySnapshot Snapshot,
    IReadOnlyList<EvidenceNode> Nodes,
    IReadOnlyList<EvidenceEdge> Edges,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    string? CanonicalHash = null)
{
    /// <summary>Creates an empty graph for a snapshot, suitable for incremental population.</summary>
    public static AnalysisDocument Empty(
        RepositorySnapshot snapshot,
        AnalysisStatus status = AnalysisStatus.Success) =>
        new(
            AnalysisSchema.CurrentVersion,
            status,
            snapshot,
            Array.Empty<EvidenceNode>(),
            Array.Empty<EvidenceEdge>(),
            Array.Empty<EvidenceRecord>(),
            Array.Empty<AnalysisDiagnostic>());
}

internal static class Empty
{
    public static readonly IReadOnlyDictionary<string, string> Properties =
        new Dictionary<string, string>();
}
