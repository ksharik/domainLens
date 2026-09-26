namespace DomainLens.Core;

/// <summary>
/// A bounded, language-neutral set of deterministic Evidence Graph additions. A contribution
/// cannot replace the repository snapshot or schema; composition supplies those from the
/// validated baseline document.
/// </summary>
public sealed record EvidenceGraphContribution(
    AnalysisStatus Status,
    IReadOnlyList<EvidenceNode> Nodes,
    IReadOnlyList<EvidenceEdge> Edges,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics)
{
    /// <summary>A successful contribution that adds no graph records or diagnostics.</summary>
    public static EvidenceGraphContribution Empty { get; } = new(
        AnalysisStatus.Success,
        Array.Empty<EvidenceNode>(),
        Array.Empty<EvidenceEdge>(),
        Array.Empty<EvidenceRecord>(),
        Array.Empty<AnalysisDiagnostic>());
}
