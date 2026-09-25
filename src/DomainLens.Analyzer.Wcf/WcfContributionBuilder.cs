using DomainLens.Core;

namespace DomainLens.Analyzer.Wcf;

internal sealed class WcfContributionBuilder
{
    private readonly AnalysisDocument _baseline;
    private readonly IReadOnlyDictionary<string, ManifestEntry> _manifest;
    private readonly IReadOnlyDictionary<string, EvidenceNode> _baselineNodes;
    private readonly Dictionary<string, EvidenceRecord> _evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableEdge> _edges = new(StringComparer.Ordinal);
    private readonly List<AnalysisDiagnostic> _diagnostics = [];
    private bool _isPartial;

    public WcfContributionBuilder(AnalysisDocument baseline)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _manifest = baseline.Snapshot.Manifest.ToDictionary(
            item => CanonicalIdentity.NormalizeRepositoryPath(item.Path),
            StringComparer.Ordinal);
        _baselineNodes = baseline.Nodes.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
    }

    public AnalysisDocument Baseline => _baseline;

    public EvidenceRecord GetContributionEvidence(string evidenceId) =>
        _evidence.TryGetValue(evidenceId, out var evidence)
            ? evidence
            : throw new KeyNotFoundException($"Unknown WCF contribution evidence '{evidenceId}'.");

    public string AddEvidence(
        string relativePath,
        SourceSpan span,
        string ruleId,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details = null)
    {
        relativePath = CanonicalIdentity.NormalizeRepositoryPath(relativePath);
        if (!_manifest.TryGetValue(relativePath, out var entry))
        {
            throw new InvalidOperationException(
                $"Cannot create WCF evidence for unknown snapshot file '{relativePath}'.");
        }

        if (span.StartOffset < 0 || span.Length < 0 || span.StartLine < 1 || span.StartColumn < 1 ||
            span.EndLine < span.StartLine ||
            (span.EndLine == span.StartLine && span.EndColumn < span.StartColumn))
        {
            throw new InvalidOperationException("A WCF evidence span is invalid.");
        }

        var id = CanonicalIdentity.CreateEvidenceId(
            _baseline.Snapshot.SnapshotId,
            relativePath,
            entry.ContentHash,
            span.StartOffset,
            span.Length,
            WcfVocabulary.ExtractorId,
            WcfVocabulary.ExtractorVersion,
            ruleId,
            basis,
            quality);
        var record = new EvidenceRecord(
            id,
            _baseline.Snapshot.SnapshotId,
            relativePath,
            entry.ContentHash,
            span,
            new ExtractorProvenance(
                WcfVocabulary.ExtractorId,
                WcfVocabulary.ExtractorVersion,
                ruleId),
            new EvidenceResolution(basis, quality, details));

        if (_evidence.TryGetValue(id, out var existing) && existing != record)
        {
            throw new InvalidOperationException($"Conflicting WCF evidence '{id}'.");
        }

        _evidence[id] = record;
        return id;
    }

    public void EnrichNode(
        EvidenceNode baselineNode,
        IEnumerable<string>? evidenceIds = null,
        IEnumerable<string>? attributes = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(baselineNode);
        if (!_baselineNodes.TryGetValue(baselineNode.NodeId, out var canonical) || canonical != baselineNode)
        {
            throw new InvalidOperationException("Only a canonical baseline node can be enriched.");
        }

        AddOrMergeNode(
            baselineNode.NodeId,
            baselineNode.LogicalId,
            baselineNode.Kind,
            baselineNode.Name,
            baselineNode.QualifiedName,
            baselineNode.ProjectId,
            evidenceIds,
            attributes,
            properties);
    }

    public EvidenceNode AddNode(
        string kind,
        string name,
        string qualifiedName,
        string? projectId,
        IEnumerable<string>? evidenceIds = null,
        IEnumerable<string>? attributes = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var projectQualifiedName = GetProjectQualifiedName(projectId);
        var logicalId = CanonicalIdentity.CreateLogicalNodeId(projectQualifiedName, kind, qualifiedName);
        var nodeId = CanonicalIdentity.CreateNodeId(_baseline.Snapshot.SnapshotId, logicalId);
        AddOrMergeNode(
            nodeId,
            logicalId,
            kind,
            name,
            qualifiedName,
            projectId,
            evidenceIds,
            attributes,
            properties);
        return ToNode(_nodes[nodeId]);
    }

    public void AddEdge(
        string kind,
        string fromNodeId,
        string? toNodeId,
        string? unresolvedTarget,
        IEnumerable<string>? evidenceIds,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details = null)
    {
        EnsureKnownNode(fromNodeId);
        if ((toNodeId is null) == (unresolvedTarget is null))
        {
            throw new ArgumentException("A WCF edge requires exactly one resolved or unresolved target.");
        }

        if (toNodeId is not null)
        {
            EnsureKnownNode(toNodeId);
        }

        var normalizedTarget = unresolvedTarget?.Trim();
        if (unresolvedTarget is not null && normalizedTarget!.Length == 0)
        {
            throw new ArgumentException("An unresolved WCF target cannot be blank.");
        }

        var key = CanonicalIdentity.Create(
            "wcf-edge-key",
            kind,
            fromNodeId,
            toNodeId,
            normalizedTarget,
            basis.ToString(),
            quality.ToString());
        if (!_edges.TryGetValue(key, out var edge))
        {
            edge = new MutableEdge(
                kind,
                fromNodeId,
                toNodeId,
                normalizedTarget,
                new EvidenceResolution(basis, quality, details));
            _edges.Add(key, edge);
        }
        else if (!string.Equals(edge.Resolution.Details, details, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Conflicting details for WCF edge '{key}'.");
        }

        if (evidenceIds is not null)
        {
            edge.EvidenceIds.UnionWith(evidenceIds);
        }

        if (quality is ResolutionQuality.Ambiguous or ResolutionQuality.Unresolved)
        {
            _isPartial = true;
        }
    }

    public void AddDiagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        string? relativePath = null,
        IEnumerable<string>? evidenceIds = null,
        IReadOnlyDictionary<string, string>? properties = null,
        bool affectsStatus = true)
    {
        var normalizedPath = relativePath is null
            ? null
            : CanonicalIdentity.NormalizeRepositoryPath(relativePath);
        var diagnostic = new AnalysisDiagnostic(
            code,
            severity,
            message.Replace('\r', ' ').Replace('\n', ' ').Trim(),
            normalizedPath,
            (evidenceIds ?? []).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            new SortedDictionary<string, string>(
                (properties ?? new Dictionary<string, string>()).ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal));
        _diagnostics.Add(diagnostic);
        if (affectsStatus && severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
        {
            _isPartial = true;
        }
    }

    public void MarkPartial() => _isPartial = true;

    public EvidenceGraphContribution Build()
    {
        var nodes = _nodes.Values
            .Select(ToNode)
            .OrderBy(item => item.NodeId, StringComparer.Ordinal)
            .ToArray();
        var edges = _edges.Values
            .Select(edge =>
            {
                var evidenceIds = edge.EvidenceIds.OrderBy(item => item, StringComparer.Ordinal).ToArray();
                var id = CanonicalIdentity.CreateEdgeId(
                    _baseline.Snapshot.SnapshotId,
                    edge.Kind,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.UnresolvedTarget,
                    edge.Resolution.Basis,
                    edge.Resolution.Quality,
                    evidenceIds);
                return new EvidenceEdge(
                    id,
                    edge.Kind,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.UnresolvedTarget,
                    evidenceIds,
                    edge.Resolution);
            })
            .OrderBy(item => item.EdgeId, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = _diagnostics
            .Distinct()
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ThenBy(item => string.Join('\n', item.EvidenceIds), StringComparer.Ordinal)
            .ToArray();

        return new EvidenceGraphContribution(
            _isPartial ? AnalysisStatus.PartialSuccess : AnalysisStatus.Success,
            nodes,
            edges,
            _evidence.Values.OrderBy(item => item.EvidenceId, StringComparer.Ordinal).ToArray(),
            diagnostics);
    }

    private void AddOrMergeNode(
        string nodeId,
        string logicalId,
        string kind,
        string name,
        string qualifiedName,
        string? projectId,
        IEnumerable<string>? evidenceIds,
        IEnumerable<string>? attributes,
        IReadOnlyDictionary<string, string>? properties)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            node = new MutableNode(nodeId, logicalId, kind, name, qualifiedName, projectId);
            _nodes.Add(nodeId, node);
        }
        else if (!string.Equals(node.LogicalId, logicalId, StringComparison.Ordinal) ||
                 !string.Equals(node.Kind, kind, StringComparison.Ordinal) ||
                 !string.Equals(node.Name, name, StringComparison.Ordinal) ||
                 !string.Equals(node.QualifiedName, qualifiedName, StringComparison.Ordinal) ||
                 !string.Equals(node.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"WCF node identity conflict for '{nodeId}'.");
        }

        if (evidenceIds is not null)
        {
            node.EvidenceIds.UnionWith(evidenceIds);
        }

        if (attributes is not null)
        {
            node.Attributes.UnionWith(attributes);
        }

        if (properties is not null)
        {
            foreach (var pair in properties)
            {
                if (node.Properties.TryGetValue(pair.Key, out var current) &&
                    !string.Equals(current, pair.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Conflicting WCF node property '{pair.Key}' for '{nodeId}'.");
                }

                node.Properties[pair.Key] = pair.Value;
            }
        }
    }

    private string? GetProjectQualifiedName(string? projectId)
    {
        if (projectId is null)
        {
            return null;
        }

        if (_baselineNodes.TryGetValue(projectId, out var baselineProject) &&
            string.Equals(baselineProject.Kind, "Project", StringComparison.Ordinal))
        {
            return baselineProject.QualifiedName;
        }

        if (_nodes.TryGetValue(projectId, out var contributionProject) &&
            string.Equals(contributionProject.Kind, "Project", StringComparison.Ordinal))
        {
            return contributionProject.QualifiedName;
        }

        throw new InvalidOperationException($"Unknown WCF node project '{projectId}'.");
    }

    private void EnsureKnownNode(string nodeId)
    {
        if (!_baselineNodes.ContainsKey(nodeId) && !_nodes.ContainsKey(nodeId))
        {
            throw new InvalidOperationException($"A WCF edge references unknown node '{nodeId}'.");
        }
    }

    private static EvidenceNode ToNode(MutableNode node) =>
        new(
            node.NodeId,
            node.LogicalId,
            node.Kind,
            node.Name,
            node.QualifiedName,
            node.ProjectId,
            node.EvidenceIds.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            node.Attributes.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            new SortedDictionary<string, string>(node.Properties, StringComparer.Ordinal));

    private sealed class MutableNode(
        string nodeId,
        string logicalId,
        string kind,
        string name,
        string qualifiedName,
        string? projectId)
    {
        public string NodeId { get; } = nodeId;
        public string LogicalId { get; } = logicalId;
        public string Kind { get; } = kind;
        public string Name { get; } = name;
        public string QualifiedName { get; } = qualifiedName;
        public string? ProjectId { get; } = projectId;
        public HashSet<string> EvidenceIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Attributes { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MutableEdge(
        string kind,
        string fromNodeId,
        string? toNodeId,
        string? unresolvedTarget,
        EvidenceResolution resolution)
    {
        public string Kind { get; } = kind;
        public string FromNodeId { get; } = fromNodeId;
        public string? ToNodeId { get; } = toNodeId;
        public string? UnresolvedTarget { get; } = unresolvedTarget;
        public EvidenceResolution Resolution { get; } = resolution;
        public HashSet<string> EvidenceIds { get; } = new(StringComparer.Ordinal);
    }
}
