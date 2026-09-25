namespace DomainLens.Core;

/// <summary>
/// Deterministically adds a normalized analyzer contribution to an already valid Evidence Graph.
/// The composer is deliberately unaware of analyzer families and does not provide registration,
/// discovery, precedence, or plug-in behavior.
/// </summary>
public static class AnalysisDocumentComposer
{
    /// <summary>
    /// Composes <paramref name="contribution"/> over <paramref name="baseline"/>, preserving
    /// baseline evidence and edges, merging compatible node enrichments, recomputing the document
    /// hash, and validating the completed graph.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The baseline or completed graph is invalid, or a contribution collides with incompatible
    /// persisted data.
    /// </exception>
    public static AnalysisDocument Compose(
        AnalysisDocument baseline,
        EvidenceGraphContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(contribution);

        AnalysisGraphValidator.ValidateAndThrow(baseline);
        ValidateContributionShape(contribution);

        var evidenceById = baseline.Evidence.ToDictionary(
            evidence => evidence.EvidenceId,
            StringComparer.Ordinal);
        foreach (var evidence in contribution.Evidence.OrderBy(
                     item => item.EvidenceId,
                     StringComparer.Ordinal))
        {
            if (evidenceById.TryGetValue(evidence.EvidenceId, out var existing))
            {
                if (existing != evidence)
                {
                    throw Conflict(
                        "evidence",
                        evidence.EvidenceId,
                        "the same EvidenceId carries different persisted evidence fields");
                }

                continue;
            }

            evidenceById.Add(evidence.EvidenceId, evidence);
        }

        var nodesById = baseline.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var nodeIdsByLogicalId = baseline.Nodes.ToDictionary(
            node => node.LogicalId,
            node => node.NodeId,
            StringComparer.Ordinal);
        foreach (var contributedNode in contribution.Nodes
                     .OrderBy(node => node.NodeId, StringComparer.Ordinal))
        {
            var node = NormalizeNode(contributedNode);
            if (nodesById.TryGetValue(node.NodeId, out var existing))
            {
                EnsureCompatibleNodeIdentity(existing, node);
                nodesById[node.NodeId] = MergeNode(existing, node);
                continue;
            }

            if (nodeIdsByLogicalId.TryGetValue(node.LogicalId, out var existingNodeId))
            {
                throw Conflict(
                    "node",
                    node.NodeId,
                    $"LogicalId '{node.LogicalId}' is already assigned to node '{existingNodeId}'");
            }

            nodesById.Add(node.NodeId, node);
            nodeIdsByLogicalId.Add(node.LogicalId, node.NodeId);
        }

        var edgesById = baseline.Edges.ToDictionary(edge => edge.EdgeId, StringComparer.Ordinal);
        foreach (var contributedEdge in contribution.Edges
                     .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal))
        {
            var edge = NormalizeEdge(contributedEdge);
            if (edgesById.TryGetValue(edge.EdgeId, out var existing))
            {
                if (!EdgesEqual(existing, edge))
                {
                    throw Conflict(
                        "edge",
                        edge.EdgeId,
                        "the same EdgeId carries different persisted edge fields");
                }

                continue;
            }

            edgesById.Add(edge.EdgeId, edge);
        }

        var diagnostics = baseline.Diagnostics
            .Concat(contribution.Diagnostics)
            .Select(NormalizeDiagnostic)
            .OrderBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ThenBy(CreateDiagnosticOrderKey, StringComparer.Ordinal)
            .ToArray();

        var composed = new AnalysisDocument(
            baseline.SchemaVersion,
            CombineStatus(baseline.Status, contribution.Status),
            baseline.Snapshot,
            nodesById.Values.OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray(),
            edgesById.Values.OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray(),
            evidenceById.Values.OrderBy(evidence => evidence.EvidenceId, StringComparer.Ordinal).ToArray(),
            diagnostics);
        composed = AnalysisJson.WithCanonicalHash(composed);
        AnalysisGraphValidator.ValidateAndThrow(composed);
        return composed;
    }

    private static void ValidateContributionShape(EvidenceGraphContribution contribution)
    {
        if (!Enum.IsDefined(contribution.Status))
        {
            throw new InvalidDataException(
                $"The contribution status '{contribution.Status}' is not defined.");
        }

        if (contribution.Nodes is null ||
            contribution.Edges is null ||
            contribution.Evidence is null ||
            contribution.Diagnostics is null)
        {
            throw new InvalidDataException("Evidence Graph contribution collections cannot be null.");
        }

        if (contribution.Nodes.Any(node => node is null) ||
            contribution.Edges.Any(edge => edge is null) ||
            contribution.Evidence.Any(evidence => evidence is null) ||
            contribution.Diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new InvalidDataException("Evidence Graph contribution collections cannot contain null records.");
        }
    }

    private static EvidenceNode NormalizeNode(EvidenceNode node)
    {
        if (node.EvidenceIds is null || node.Attributes is null || node.Properties is null)
        {
            throw new InvalidDataException(
                $"Contributed node '{node.NodeId}' has a null required collection.");
        }

        EnsureNoDuplicateReferences("node", node.NodeId, node.EvidenceIds);
        return node with
        {
            EvidenceIds = node.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Attributes = node.Attributes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Properties = CopyProperties("node", node.NodeId, node.Properties)
        };
    }

    private static EvidenceNode MergeNode(EvidenceNode baseline, EvidenceNode contribution)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in baseline.Properties)
        {
            properties.Add(property.Key, property.Value);
        }

        foreach (var property in contribution.Properties)
        {
            if (properties.TryGetValue(property.Key, out var existingValue) &&
                !string.Equals(existingValue, property.Value, StringComparison.Ordinal))
            {
                throw Conflict(
                    "node",
                    baseline.NodeId,
                    $"property '{property.Key}' has both '{existingValue}' and '{property.Value}'");
            }

            properties[property.Key] = property.Value;
        }

        return baseline with
        {
            EvidenceIds = baseline.EvidenceIds
                .Concat(contribution.EvidenceIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Attributes = baseline.Attributes
                .Concat(contribution.Attributes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Properties = properties
        };
    }

    private static void EnsureCompatibleNodeIdentity(EvidenceNode baseline, EvidenceNode contribution)
    {
        if (!string.Equals(baseline.LogicalId, contribution.LogicalId, StringComparison.Ordinal) ||
            !string.Equals(baseline.Kind, contribution.Kind, StringComparison.Ordinal) ||
            !string.Equals(baseline.Name, contribution.Name, StringComparison.Ordinal) ||
            !string.Equals(baseline.QualifiedName, contribution.QualifiedName, StringComparison.Ordinal) ||
            !string.Equals(baseline.ProjectId, contribution.ProjectId, StringComparison.Ordinal))
        {
            throw Conflict(
                "node",
                contribution.NodeId,
                "a baseline node and its enrichment have different identity fields");
        }
    }

    private static EvidenceEdge NormalizeEdge(EvidenceEdge edge)
    {
        if (edge.EvidenceIds is null || edge.Resolution is null)
        {
            throw new InvalidDataException(
                $"Contributed edge '{edge.EdgeId}' has a null required member.");
        }

        EnsureNoDuplicateReferences("edge", edge.EdgeId, edge.EvidenceIds);
        return edge with
        {
            EvidenceIds = edge.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
    }

    private static bool EdgesEqual(EvidenceEdge left, EvidenceEdge right) =>
        string.Equals(left.EdgeId, right.EdgeId, StringComparison.Ordinal) &&
        string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) &&
        string.Equals(left.FromNodeId, right.FromNodeId, StringComparison.Ordinal) &&
        string.Equals(left.ToNodeId, right.ToNodeId, StringComparison.Ordinal) &&
        string.Equals(left.UnresolvedTarget, right.UnresolvedTarget, StringComparison.Ordinal) &&
        left.Resolution == right.Resolution &&
        left.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static AnalysisDiagnostic NormalizeDiagnostic(AnalysisDiagnostic diagnostic)
    {
        if (diagnostic.EvidenceIds is null || diagnostic.Properties is null)
        {
            throw new InvalidDataException(
                $"Contributed diagnostic '{diagnostic.Code}' has a null required collection.");
        }

        EnsureNoDuplicateReferences("diagnostic", diagnostic.Code, diagnostic.EvidenceIds);
        return diagnostic with
        {
            EvidenceIds = diagnostic.EvidenceIds
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Properties = CopyProperties("diagnostic", diagnostic.Code, diagnostic.Properties)
        };
    }

    private static IReadOnlyDictionary<string, string> CopyProperties(
        string ownerKind,
        string ownerId,
        IReadOnlyDictionary<string, string> properties)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (string.IsNullOrWhiteSpace(property.Key) || property.Value is null)
            {
                throw new InvalidDataException(
                    $"Contributed {ownerKind} '{ownerId}' has an invalid property.");
            }

            result.Add(property.Key, property.Value);
        }

        return result;
    }

    private static void EnsureNoDuplicateReferences(
        string ownerKind,
        string ownerId,
        IReadOnlyList<string> references)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (!unique.Add(reference))
            {
                throw new InvalidDataException(
                    $"Contributed {ownerKind} '{ownerId}' repeats reference '{reference}'.");
            }
        }
    }

    private static string CreateDiagnosticOrderKey(AnalysisDiagnostic diagnostic)
    {
        var components = new List<string?>
        {
            diagnostic.Severity.ToString(),
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.RelativePath,
            "evidence",
            diagnostic.EvidenceIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        components.AddRange(diagnostic.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal));
        components.Add("properties");
        components.Add(diagnostic.Properties.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var property in diagnostic.Properties.OrderBy(
                     property => property.Key,
                     StringComparer.Ordinal))
        {
            components.Add(property.Key);
            components.Add(property.Value);
        }

        return CanonicalIdentity.Create("diagnostic-order", components);
    }

    private static AnalysisStatus CombineStatus(AnalysisStatus baseline, AnalysisStatus contribution) =>
        baseline == AnalysisStatus.Failure || contribution == AnalysisStatus.Failure
            ? AnalysisStatus.Failure
            : baseline == AnalysisStatus.PartialSuccess || contribution == AnalysisStatus.PartialSuccess
                ? AnalysisStatus.PartialSuccess
                : AnalysisStatus.Success;

    private static InvalidDataException Conflict(string kind, string id, string reason) =>
        new($"Evidence Graph {kind} collision for '{id}': {reason}.");
}
