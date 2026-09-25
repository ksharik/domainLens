using DomainLens.Core;

namespace DomainLens.Scanner.Internal;

internal sealed class EvidenceGraphBuilder
{
    private readonly RepositorySnapshot _snapshot;
    private readonly IReadOnlyDictionary<string, RepositoryFile> _files;
    private readonly Dictionary<string, MutableNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EvidenceRecord> _evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableEdge> _edges = new(StringComparer.Ordinal);

    public EvidenceGraphBuilder(RepositorySnapshot snapshot, RepositoryInventory inventory)
    {
        _snapshot = snapshot;
        _files = inventory.ByRelativePath;
    }

    public string AddEvidence(
        string relativePath,
        CapturedSpan span,
        string extractorId,
        string extractorVersion,
        string ruleId,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details = null)
    {
        if (!_files.TryGetValue(relativePath, out var file))
        {
            throw new InvalidOperationException($"Cannot create evidence for unknown snapshot file '{relativePath}'.");
        }

        var evidenceId = CanonicalIdentity.CreateEvidenceId(
            _snapshot.SnapshotId,
            relativePath,
            file.ContentHash,
            span.StartOffset,
            span.Length,
            extractorId,
            extractorVersion,
            ruleId,
            basis,
            quality);

        _evidence.TryAdd(
            evidenceId,
            new EvidenceRecord(
                evidenceId,
                _snapshot.SnapshotId,
                relativePath,
                file.ContentHash,
                new SourceSpan(
                    span.StartOffset,
                    span.Length,
                    span.StartLine,
                    span.StartColumn,
                    span.EndLine,
                    span.EndColumn),
                new ExtractorProvenance(extractorId, extractorVersion, ruleId),
                new EvidenceResolution(basis, quality, details)));

        return evidenceId;
    }

    public string AddNode(
        string key,
        string kind,
        string name,
        string qualifiedName,
        string? projectKey,
        IEnumerable<string>? evidenceIds = null,
        IEnumerable<string>? attributes = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!_nodes.TryGetValue(key, out var node))
        {
            node = new MutableNode(key, kind, name, qualifiedName, projectKey);
            _nodes.Add(key, node);
        }
        else if (!string.Equals(node.Kind, kind, StringComparison.Ordinal) ||
                 !string.Equals(node.QualifiedName, qualifiedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Node key '{key}' was reused for a different observation.");
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
            foreach (var property in properties)
            {
                node.Properties[property.Key] = property.Value;
            }
        }

        return key;
    }

    public void AddEdge(
        string kind,
        string fromKey,
        string? toKey,
        string? unresolvedTarget,
        IEnumerable<string>? evidenceIds,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details = null)
    {
        if (!_nodes.ContainsKey(fromKey))
        {
            throw new InvalidOperationException($"Edge source key '{fromKey}' has not been registered.");
        }

        if ((toKey is null) == (unresolvedTarget is null))
        {
            throw new ArgumentException("An edge requires exactly one resolved or unresolved target.");
        }

        if (toKey is not null && !_nodes.ContainsKey(toKey))
        {
            throw new InvalidOperationException($"Edge target key '{toKey}' has not been registered.");
        }

        var edgeKey = CanonicalIdentity.Create(
            "edge-key",
            kind,
            fromKey,
            toKey,
            unresolvedTarget,
            basis.ToString(),
            quality.ToString());
        if (!_edges.TryGetValue(edgeKey, out var edge))
        {
            edge = new MutableEdge(
                edgeKey,
                kind,
                fromKey,
                toKey,
                unresolvedTarget,
                new EvidenceResolution(basis, quality, details));
            _edges.Add(edgeKey, edge);
        }

        if (evidenceIds is not null)
        {
            edge.EvidenceIds.UnionWith(evidenceIds);
        }
    }

    public (IReadOnlyList<EvidenceNode> Nodes, IReadOnlyList<EvidenceEdge> Edges, IReadOnlyList<EvidenceRecord> Evidence) Build()
    {
        var logicalIds = _nodes.Values.ToDictionary(
            node => node.Key,
            node => CanonicalIdentity.CreateLogicalNodeId(
                GetProjectQualifiedName(node),
                node.Kind,
                node.QualifiedName),
            StringComparer.Ordinal);

        var nodeIds = _nodes.Values.ToDictionary(
            node => node.Key,
            node => CanonicalIdentity.CreateNodeId(_snapshot.SnapshotId, logicalIds[node.Key]),
            StringComparer.Ordinal);

        var nodes = _nodes.Values
            .Select(node =>
            {
                var logicalId = logicalIds[node.Key];
                return new EvidenceNode(
                    nodeIds[node.Key],
                    logicalId,
                    node.Kind,
                    node.Name,
                    node.QualifiedName,
                    node.ProjectKey is null ? null : nodeIds[node.ProjectKey],
                    node.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    node.Attributes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    new SortedDictionary<string, string>(node.Properties, StringComparer.Ordinal));
            })
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

        var edges = _edges.Values
            .Select(edge =>
            {
                var fromNodeId = nodeIds[edge.FromKey];
                var toNodeId = edge.ToKey is null ? null : nodeIds[edge.ToKey];
                var evidenceIds = edge.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
                var edgeId = CanonicalIdentity.CreateEdgeId(
                    _snapshot.SnapshotId,
                    edge.Kind,
                    fromNodeId,
                    toNodeId,
                    edge.UnresolvedTarget,
                    edge.Resolution.Basis,
                    edge.Resolution.Quality,
                    evidenceIds);
                return new EvidenceEdge(
                    edgeId,
                    edge.Kind,
                    fromNodeId,
                    toNodeId,
                    edge.UnresolvedTarget,
                    evidenceIds,
                    edge.Resolution);
            })
            .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .ToArray();

        return (
            nodes,
            edges,
            _evidence.Values.OrderBy(item => item.EvidenceId, StringComparer.Ordinal).ToArray());
    }

    private string? GetProjectQualifiedName(MutableNode node)
    {
        if (node.ProjectKey is null)
        {
            return null;
        }

        if (!_nodes.TryGetValue(node.ProjectKey, out var projectNode))
        {
            throw new InvalidOperationException(
                $"Node '{node.Key}' references unknown project key '{node.ProjectKey}'.");
        }

        return projectNode.QualifiedName;
    }

    private sealed class MutableNode(
        string key,
        string kind,
        string name,
        string qualifiedName,
        string? projectKey)
    {
        public string Key { get; } = key;
        public string Kind { get; } = kind;
        public string Name { get; } = name;
        public string QualifiedName { get; } = qualifiedName;
        public string? ProjectKey { get; } = projectKey;
        public HashSet<string> EvidenceIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Attributes { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MutableEdge(
        string key,
        string kind,
        string fromKey,
        string? toKey,
        string? unresolvedTarget,
        EvidenceResolution resolution)
    {
        public string Key { get; } = key;
        public string Kind { get; } = kind;
        public string FromKey { get; } = fromKey;
        public string? ToKey { get; } = toKey;
        public string? UnresolvedTarget { get; } = unresolvedTarget;
        public EvidenceResolution Resolution { get; } = resolution;
        public HashSet<string> EvidenceIds { get; } = new(StringComparer.Ordinal);
    }
}
