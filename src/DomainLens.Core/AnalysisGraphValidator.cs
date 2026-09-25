namespace DomainLens.Core;

/// <summary>A deterministic integrity problem in an analysis document.</summary>
public sealed record GraphValidationIssue(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? SubjectId = null);

/// <summary>The complete result of Evidence Graph integrity validation.</summary>
public sealed record GraphValidationResult(IReadOnlyList<GraphValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != DiagnosticSeverity.Error);

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidDataException(
                "The analysis graph is invalid: " +
                string.Join("; ", Issues
                    .Where(issue => issue.Severity == DiagnosticSeverity.Error)
                    .Select(issue => $"{issue.Code}: {issue.Message}")));
        }
    }
}

/// <summary>Validates identifiers, references, and source provenance without analyzer knowledge.</summary>
public static class AnalysisGraphValidator
{
    public static GraphValidationResult Validate(AnalysisDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<GraphValidationIssue>();
        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            Error(issues, "schema.missing", "SchemaVersion is required.");
        }
        else if (!string.Equals(document.SchemaVersion, AnalysisSchema.CurrentVersion, StringComparison.Ordinal))
        {
            Error(
                issues,
                "schema.unsupported",
                $"SchemaVersion '{document.SchemaVersion}' is not supported; expected '{AnalysisSchema.CurrentVersion}'.");
        }

        if (!Enum.IsDefined(document.Status))
        {
            Error(issues, "status.invalid", $"Analysis status '{document.Status}' is not defined.");
        }

        if (document.Snapshot is null)
        {
            Error(issues, "snapshot.missing", "Snapshot is required.");
            return Result(issues);
        }

        if (string.IsNullOrWhiteSpace(document.Snapshot.SnapshotId))
        {
            Error(issues, "snapshot.id.missing", "SnapshotId is required.");
        }

        var manifestByPath = ValidateManifest(document.Snapshot, issues);
        ValidateSnapshotIdentity(document.Snapshot, issues);
        var evidenceById = ValidateEvidence(document, manifestByPath, issues);
        var nodesById = ValidateNodes(
            document.Snapshot.SnapshotId,
            document.Nodes,
            evidenceById,
            issues);
        ValidateEdges(
            document.Snapshot.SnapshotId,
            document.Edges,
            nodesById,
            evidenceById,
            issues);
        ValidateDiagnostics(document.Diagnostics, manifestByPath, evidenceById, issues);

        if (string.IsNullOrWhiteSpace(document.CanonicalHash))
        {
            Error(issues, "document.hash.missing", "CanonicalHash is required.");
        }
        else
        {
            if (!IsSha256(document.CanonicalHash))
            {
                Error(
                    issues,
                    "document.hash.format",
                    "CanonicalHash must be a 64-character hexadecimal SHA-256 digest.");
            }
            else if (!AnalysisJson.VerifyCanonicalHash(document))
            {
                Error(
                    issues,
                    "document.hash.mismatch",
                    "CanonicalHash does not match the canonical document content.");
            }
        }

        return Result(issues);
    }

    public static void ValidateAndThrow(AnalysisDocument document) =>
        Validate(document).ThrowIfInvalid();

    private static Dictionary<string, ManifestEntry> ValidateManifest(
        RepositorySnapshot snapshot,
        ICollection<GraphValidationIssue> issues)
    {
        var manifestByPath = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        if (snapshot.Manifest is null)
        {
            Error(issues, "manifest.missing", "Snapshot manifest is required.", snapshot.SnapshotId);
            return manifestByPath;
        }

        foreach (var entry in snapshot.Manifest)
        {
            if (entry is null)
            {
                Error(issues, "manifest.entry.null", "Manifest cannot contain null entries.", snapshot.SnapshotId);
                continue;
            }

            if (!TryGetCanonicalPath(entry.Path, out var path))
            {
                Error(
                    issues,
                    "manifest.path.invalid",
                    $"Manifest path '{entry.Path}' must be a canonical repository-relative path.",
                    entry.Path);
                continue;
            }

            if (!string.Equals(entry.Path, path, StringComparison.Ordinal))
            {
                Error(
                    issues,
                    "manifest.path.noncanonical",
                    $"Manifest path '{entry.Path}' must be stored as '{path}'.",
                    entry.Path);
            }

            if (!manifestByPath.TryAdd(path, entry))
            {
                Error(
                    issues,
                    "manifest.path.duplicate",
                    $"Manifest path '{path}' appears more than once.",
                    path);
            }

            if (!IsSha256(entry.ContentHash))
            {
                Error(
                    issues,
                    "manifest.hash.invalid",
                    $"Manifest entry '{path}' must have a hexadecimal SHA-256 content hash.",
                    path);
            }

            if (entry.Length < 0)
            {
                Error(
                    issues,
                    "manifest.length.invalid",
                    $"Manifest entry '{path}' cannot have a negative length.",
                    path);
            }
        }

        return manifestByPath;
    }

    private static void ValidateSnapshotIdentity(
        RepositorySnapshot snapshot,
        ICollection<GraphValidationIssue> issues)
    {
        if (snapshot.Manifest is null ||
            snapshot.Manifest.Any(entry =>
                entry is null ||
                string.IsNullOrWhiteSpace(entry.Path) ||
                entry.ContentHash is null))
        {
            return;
        }

        try
        {
            var expectedSnapshotId = RepositorySnapshot.Create(snapshot.Revision, snapshot.Manifest).SnapshotId;
            if (!string.Equals(snapshot.SnapshotId, expectedSnapshotId, StringComparison.Ordinal))
            {
                Error(
                    issues,
                    "snapshot.id.mismatch",
                    $"SnapshotId '{snapshot.SnapshotId}' does not match the revision and manifest-derived identity '{expectedSnapshotId}'.",
                    snapshot.SnapshotId);
            }
        }
        catch (ArgumentException)
        {
            // Manifest validation reports invalid paths. Avoid duplicating that problem as an
            // identity error when no canonical identity can be recomputed.
        }
    }

    private static Dictionary<string, EvidenceRecord> ValidateEvidence(
        AnalysisDocument document,
        IReadOnlyDictionary<string, ManifestEntry> manifestByPath,
        ICollection<GraphValidationIssue> issues)
    {
        var evidenceById = new Dictionary<string, EvidenceRecord>(StringComparer.Ordinal);
        if (document.Evidence is null)
        {
            Error(issues, "evidence.missing", "Evidence collection is required.");
            return evidenceById;
        }

        foreach (var evidence in document.Evidence)
        {
            if (evidence is null)
            {
                Error(issues, "evidence.null", "Evidence collection cannot contain null records.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
            {
                Error(issues, "evidence.id.missing", "Every evidence record requires an EvidenceId.");
            }
            else if (!evidenceById.TryAdd(evidence.EvidenceId, evidence))
            {
                Error(
                    issues,
                    "evidence.id.duplicate",
                    $"EvidenceId '{evidence.EvidenceId}' appears more than once.",
                    evidence.EvidenceId);
            }

            if (!string.Equals(
                    evidence.SnapshotId,
                    document.Snapshot.SnapshotId,
                    StringComparison.Ordinal))
            {
                Error(
                    issues,
                    "evidence.snapshot.mismatch",
                    $"Evidence '{evidence.EvidenceId}' references snapshot '{evidence.SnapshotId}' instead of '{document.Snapshot.SnapshotId}'.",
                    evidence.EvidenceId);
            }

            ValidateEvidenceLocation(evidence, manifestByPath, issues);
            ValidateSpan(evidence, issues);
            ValidateProvenance(evidence, issues);
            ValidateEvidenceIdentity(evidence, issues);
        }

        return evidenceById;
    }

    private static void ValidateEvidenceIdentity(
        EvidenceRecord evidence,
        ICollection<GraphValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidence.SnapshotId) ||
            string.IsNullOrWhiteSpace(evidence.RelativePath) ||
            evidence.ContentHash is null ||
            evidence.Span is null ||
            evidence.Provenance is null ||
            evidence.Resolution is null ||
            string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorId) ||
            string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorVersion) ||
            string.IsNullOrWhiteSpace(evidence.Provenance.RuleId))
        {
            return;
        }

        var expectedEvidenceId = CanonicalIdentity.CreateEvidenceId(
            evidence.SnapshotId,
            evidence.RelativePath,
            evidence.ContentHash,
            evidence.Span.StartOffset,
            evidence.Span.Length,
            evidence.Provenance.ExtractorId,
            evidence.Provenance.ExtractorVersion,
            evidence.Provenance.RuleId,
            evidence.Resolution.Basis,
            evidence.Resolution.Quality);
        if (!string.Equals(evidence.EvidenceId, expectedEvidenceId, StringComparison.Ordinal))
        {
            Error(
                issues,
                "evidence.id.mismatch",
                $"EvidenceId '{evidence.EvidenceId}' does not match the evidence-derived identity '{expectedEvidenceId}'.",
                evidence.EvidenceId);
        }
    }

    private static void ValidateEvidenceLocation(
        EvidenceRecord evidence,
        IReadOnlyDictionary<string, ManifestEntry> manifestByPath,
        ICollection<GraphValidationIssue> issues)
    {
        if (!TryGetCanonicalPath(evidence.RelativePath, out var path))
        {
            Error(
                issues,
                "evidence.path.invalid",
                $"Evidence '{evidence.EvidenceId}' has an invalid repository-relative path.",
                evidence.EvidenceId);
            return;
        }

        if (!string.Equals(evidence.RelativePath, path, StringComparison.Ordinal))
        {
            Error(
                issues,
                "evidence.path.noncanonical",
                $"Evidence '{evidence.EvidenceId}' path must be stored as '{path}'.",
                evidence.EvidenceId);
        }

        if (!manifestByPath.TryGetValue(path, out var entry))
        {
            Error(
                issues,
                "evidence.path.unresolved",
                $"Evidence '{evidence.EvidenceId}' path '{path}' is absent from the snapshot manifest.",
                evidence.EvidenceId);
            return;
        }

        if (!string.Equals(evidence.ContentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            Error(
                issues,
                "evidence.hash.mismatch",
                $"Evidence '{evidence.EvidenceId}' content hash does not match manifest entry '{path}'.",
                evidence.EvidenceId);
        }

        if (!IsSha256(evidence.ContentHash))
        {
            Error(
                issues,
                "evidence.hash.invalid",
                $"Evidence '{evidence.EvidenceId}' must have a hexadecimal SHA-256 content hash.",
                evidence.EvidenceId);
        }
    }

    private static void ValidateSpan(
        EvidenceRecord evidence,
        ICollection<GraphValidationIssue> issues)
    {
        if (evidence.Span is null)
        {
            Error(issues, "evidence.span.missing", "Source-backed evidence requires a SourceSpan.", evidence.EvidenceId);
            return;
        }

        var span = evidence.Span;
        var coordinatesValid = span.StartOffset >= 0 &&
                               span.Length >= 0 &&
                               span.StartLine >= 1 &&
                               span.StartColumn >= 1 &&
                               span.EndLine >= span.StartLine &&
                               span.EndColumn >= 1 &&
                               (span.EndLine != span.StartLine || span.EndColumn >= span.StartColumn);
        if (!coordinatesValid)
        {
            Error(
                issues,
                "evidence.span.invalid",
                $"Evidence '{evidence.EvidenceId}' has an invalid source span.",
                evidence.EvidenceId);
        }
    }

    private static void ValidateProvenance(
        EvidenceRecord evidence,
        ICollection<GraphValidationIssue> issues)
    {
        if (evidence.Provenance is null)
        {
            Error(
                issues,
                "evidence.provenance.missing",
                $"Evidence '{evidence.EvidenceId}' requires extractor provenance.",
                evidence.EvidenceId);
            return;
        }

        if (string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorId) ||
            string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorVersion) ||
            string.IsNullOrWhiteSpace(evidence.Provenance.RuleId))
        {
            Error(
                issues,
                "evidence.provenance.invalid",
                $"Evidence '{evidence.EvidenceId}' provenance requires extractor, version, and rule identifiers.",
                evidence.EvidenceId);
        }

        if (evidence.Resolution is null)
        {
            Error(
                issues,
                "evidence.resolution.missing",
                $"Evidence '{evidence.EvidenceId}' requires resolution information.",
                evidence.EvidenceId);
        }
        else
        {
            ValidateResolution(evidence.Resolution, "evidence", evidence.EvidenceId, issues);
        }
    }

    private static Dictionary<string, EvidenceNode> ValidateNodes(
        string snapshotId,
        IReadOnlyList<EvidenceNode>? nodes,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        ICollection<GraphValidationIssue> issues)
    {
        var nodesById = new Dictionary<string, EvidenceNode>(StringComparer.Ordinal);
        if (nodes is null)
        {
            Error(issues, "nodes.missing", "Node collection is required.");
            return nodesById;
        }

        foreach (var node in nodes)
        {
            if (node is null)
            {
                Error(issues, "node.null", "Node collection cannot contain null records.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                Error(issues, "node.id.missing", "Every node requires a NodeId.");
            }
            else if (!nodesById.TryAdd(node.NodeId, node))
            {
                Error(issues, "node.id.duplicate", $"NodeId '{node.NodeId}' appears more than once.", node.NodeId);
            }

            if (string.IsNullOrWhiteSpace(node.LogicalId) ||
                string.IsNullOrWhiteSpace(node.Kind) ||
                string.IsNullOrWhiteSpace(node.Name) ||
                string.IsNullOrWhiteSpace(node.QualifiedName))
            {
                Error(
                    issues,
                    "node.identity.invalid",
                    $"Node '{node.NodeId}' requires LogicalId, Kind, Name, and QualifiedName.",
                    node.NodeId);
            }

            ValidateEvidenceReferences("node", node.NodeId, node.EvidenceIds, evidenceById, issues);
        }

        foreach (var node in nodesById.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.ProjectId) && !nodesById.ContainsKey(node.ProjectId))
            {
                Error(
                    issues,
                    "node.project.unresolved",
                    $"Node '{node.NodeId}' references missing project node '{node.ProjectId}'.",
                    node.NodeId);
            }

            ValidateNodeIdentity(snapshotId, node, nodesById, issues);
        }

        return nodesById;
    }

    private static void ValidateNodeIdentity(
        string snapshotId,
        EvidenceNode node,
        IReadOnlyDictionary<string, EvidenceNode> nodesById,
        ICollection<GraphValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(snapshotId) ||
            string.IsNullOrWhiteSpace(node.Kind) ||
            string.IsNullOrWhiteSpace(node.QualifiedName))
        {
            return;
        }

        string? projectQualifiedName = null;
        if (!string.IsNullOrWhiteSpace(node.ProjectId))
        {
            if (!nodesById.TryGetValue(node.ProjectId, out var projectNode) ||
                string.IsNullOrWhiteSpace(projectNode.QualifiedName))
            {
                return;
            }

            projectQualifiedName = projectNode.QualifiedName;
        }

        var expectedLogicalId = CanonicalIdentity.CreateLogicalNodeId(
            projectQualifiedName,
            node.Kind,
            node.QualifiedName);
        if (!string.Equals(node.LogicalId, expectedLogicalId, StringComparison.Ordinal))
        {
            Error(
                issues,
                "node.logical-id.mismatch",
                $"Node '{node.NodeId}' LogicalId does not match its persisted identity fields.",
                node.NodeId);
        }

        var expectedNodeId = CanonicalIdentity.CreateNodeId(snapshotId, expectedLogicalId);
        if (!string.Equals(node.NodeId, expectedNodeId, StringComparison.Ordinal))
        {
            Error(
                issues,
                "node.id.mismatch",
                $"NodeId '{node.NodeId}' does not match the snapshot and logical identity-derived value '{expectedNodeId}'.",
                node.NodeId);
        }
    }

    private static void ValidateEdges(
        string snapshotId,
        IReadOnlyList<EvidenceEdge>? edges,
        IReadOnlyDictionary<string, EvidenceNode> nodesById,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        ICollection<GraphValidationIssue> issues)
    {
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        if (edges is null)
        {
            Error(issues, "edges.missing", "Edge collection is required.");
            return;
        }

        foreach (var edge in edges)
        {
            if (edge is null)
            {
                Error(issues, "edge.null", "Edge collection cannot contain null records.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(edge.EdgeId))
            {
                Error(issues, "edge.id.missing", "Every edge requires an EdgeId.");
            }
            else if (!edgeIds.Add(edge.EdgeId))
            {
                Error(issues, "edge.id.duplicate", $"EdgeId '{edge.EdgeId}' appears more than once.", edge.EdgeId);
            }

            if (string.IsNullOrWhiteSpace(edge.Kind))
            {
                Error(issues, "edge.kind.missing", $"Edge '{edge.EdgeId}' requires a Kind.", edge.EdgeId);
            }

            if (!nodesById.ContainsKey(edge.FromNodeId))
            {
                Error(
                    issues,
                    "edge.from.unresolved",
                    $"Edge '{edge.EdgeId}' references missing source node '{edge.FromNodeId}'.",
                    edge.EdgeId);
            }

            var hasResolvedTarget = !string.IsNullOrWhiteSpace(edge.ToNodeId);
            var hasUnresolvedTarget = !string.IsNullOrWhiteSpace(edge.UnresolvedTarget);
            if (hasResolvedTarget == hasUnresolvedTarget)
            {
                Error(
                    issues,
                    "edge.target.invalid",
                    $"Edge '{edge.EdgeId}' must have exactly one resolved or unresolved target.",
                    edge.EdgeId);
            }
            else if (hasResolvedTarget && !nodesById.ContainsKey(edge.ToNodeId!))
            {
                Error(
                    issues,
                    "edge.to.unresolved",
                    $"Edge '{edge.EdgeId}' references missing target node '{edge.ToNodeId}'.",
                    edge.EdgeId);
            }

            if (edge.Resolution is null)
            {
                Error(issues, "edge.resolution.missing", $"Edge '{edge.EdgeId}' requires resolution information.", edge.EdgeId);
            }
            else
            {
                ValidateResolution(edge.Resolution, "edge", edge.EdgeId, issues);
            }

            ValidateEvidenceReferences("edge", edge.EdgeId, edge.EvidenceIds, evidenceById, issues);
            ValidateEdgeIdentity(snapshotId, edge, issues);
        }
    }

    private static void ValidateEdgeIdentity(
        string snapshotId,
        EvidenceEdge edge,
        ICollection<GraphValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(snapshotId) ||
            string.IsNullOrWhiteSpace(edge.Kind) ||
            string.IsNullOrWhiteSpace(edge.FromNodeId) ||
            edge.Resolution is null ||
            edge.EvidenceIds is null)
        {
            return;
        }

        var expectedEdgeId = CanonicalIdentity.CreateEdgeId(
            snapshotId,
            edge.Kind,
            edge.FromNodeId,
            edge.ToNodeId,
            edge.UnresolvedTarget,
            edge.Resolution.Basis,
            edge.Resolution.Quality,
            edge.EvidenceIds);
        if (!string.Equals(edge.EdgeId, expectedEdgeId, StringComparison.Ordinal))
        {
            Error(
                issues,
                "edge.id.mismatch",
                $"EdgeId '{edge.EdgeId}' does not match its persisted identity fields.",
                edge.EdgeId);
        }
    }

    private static void ValidateDiagnostics(
        IReadOnlyList<AnalysisDiagnostic>? diagnostics,
        IReadOnlyDictionary<string, ManifestEntry> manifestByPath,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        ICollection<GraphValidationIssue> issues)
    {
        if (diagnostics is null)
        {
            Error(issues, "diagnostics.missing", "Diagnostics collection is required.");
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                Error(issues, "diagnostic.null", "Diagnostics collection cannot contain null records.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Message))
            {
                Error(issues, "diagnostic.invalid", "Diagnostics require both Code and Message.");
            }

            if (!Enum.IsDefined(diagnostic.Severity))
            {
                Error(
                    issues,
                    "diagnostic.severity.invalid",
                    $"Diagnostic '{diagnostic.Code}' has an undefined severity.",
                    diagnostic.Code);
            }

            if (diagnostic.RelativePath is not null)
            {
                if (!TryGetCanonicalPath(diagnostic.RelativePath, out var path) ||
                    !string.Equals(diagnostic.RelativePath, path, StringComparison.Ordinal) ||
                    !manifestByPath.ContainsKey(path))
                {
                    Error(
                        issues,
                        "diagnostic.path.unresolved",
                        $"Diagnostic '{diagnostic.Code}' path does not resolve to the snapshot manifest.",
                        diagnostic.Code);
                }
            }

            ValidateEvidenceReferences(
                "diagnostic",
                diagnostic.Code,
                diagnostic.EvidenceIds,
                evidenceById,
                issues);
        }
    }

    private static void ValidateEvidenceReferences(
        string ownerKind,
        string ownerId,
        IReadOnlyList<string>? evidenceIds,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        ICollection<GraphValidationIssue> issues)
    {
        if (evidenceIds is null)
        {
            Error(
                issues,
                $"{ownerKind}.evidence.missing",
                $"{ownerKind} '{ownerId}' requires an evidence-reference collection.",
                ownerId);
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceId in evidenceIds)
        {
            if (string.IsNullOrWhiteSpace(evidenceId) || !evidenceById.ContainsKey(evidenceId))
            {
                Error(
                    issues,
                    $"{ownerKind}.evidence.unresolved",
                    $"{ownerKind} '{ownerId}' references missing evidence '{evidenceId}'.",
                    ownerId);
            }
            else if (!seen.Add(evidenceId))
            {
                Error(
                    issues,
                    $"{ownerKind}.evidence.duplicate",
                    $"{ownerKind} '{ownerId}' repeats evidence reference '{evidenceId}'.",
                    ownerId);
            }
        }
    }

    private static bool TryGetCanonicalPath(string? path, out string canonicalPath)
    {
        try
        {
            canonicalPath = CanonicalIdentity.NormalizeRepositoryPath(path!);
            return true;
        }
        catch (ArgumentException)
        {
            canonicalPath = string.Empty;
            return false;
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateResolution(
        EvidenceResolution resolution,
        string ownerKind,
        string ownerId,
        ICollection<GraphValidationIssue> issues)
    {
        if (!Enum.IsDefined(resolution.Basis) || !Enum.IsDefined(resolution.Quality))
        {
            Error(
                issues,
                $"{ownerKind}.resolution.invalid",
                $"{ownerKind} '{ownerId}' has an undefined resolution basis or quality.",
                ownerId);
        }
    }

    private static GraphValidationResult Result(IEnumerable<GraphValidationIssue> issues) =>
        new(issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.SubjectId, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray());

    private static void Error(
        ICollection<GraphValidationIssue> issues,
        string code,
        string message,
        string? subjectId = null) =>
        issues.Add(new GraphValidationIssue(code, DiagnosticSeverity.Error, message, subjectId));
}
