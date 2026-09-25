using DomainLens.Core;

namespace DomainLens.Scanner.Tests;

public sealed class AnalysisDocumentComposerTests
{
    [Fact]
    public void Empty_contribution_preserves_the_canonical_baseline_document()
    {
        var baseline = CreateBaseline();

        var composed = AnalysisDocumentComposer.Compose(
            baseline,
            EvidenceGraphContribution.Empty);

        Assert.Equal(baseline.CanonicalHash, composed.CanonicalHash);
        Assert.Equal(
            AnalysisJson.Serialize(baseline, indented: false),
            AnalysisJson.Serialize(composed, indented: false));
    }

    [Fact]
    public void Compatible_node_enrichment_preserves_baseline_ids_and_edges()
    {
        var baseline = CreateBaseline();
        var baselineType = Assert.Single(baseline.Nodes, node => node.Kind == "Class");
        var enrichmentEvidence = CreateEvidence(
            baseline.Snapshot,
            "test.framework-contract",
            ResolutionBasis.Semantic,
            ResolutionQuality.Partial,
            "The controlled compilation has partial project fidelity.");
        var enrichment = baselineType with
        {
            EvidenceIds = new[] { baselineType.EvidenceIds[0], enrichmentEvidence.EvidenceId },
            Attributes = new[] { "Framework.Contract", "Serializable" },
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["framework.role"] = "contract",
                ["language"] = "C#",
            }
        };
        var contribution = new EvidenceGraphContribution(
            AnalysisStatus.PartialSuccess,
            new[] { enrichment },
            Array.Empty<EvidenceEdge>(),
            new[] { enrichmentEvidence },
            new[]
            {
                AnalysisDiagnostic.Create(
                    "test.partial",
                    DiagnosticSeverity.Warning,
                    "The test enrichment is deliberately partial.",
                    "sample.cs")
            });

        var composed = AnalysisDocumentComposer.Compose(baseline, contribution);

        Assert.Equal(AnalysisStatus.PartialSuccess, composed.Status);
        Assert.True(AnalysisJson.VerifyCanonicalHash(composed));
        Assert.True(AnalysisGraphValidator.Validate(composed).IsValid);

        var enrichedType = Assert.Single(composed.Nodes, node => node.NodeId == baselineType.NodeId);
        Assert.Equal(baselineType.LogicalId, enrichedType.LogicalId);
        Assert.Contains("Serializable", enrichedType.Attributes);
        Assert.Contains("Framework.Contract", enrichedType.Attributes);
        Assert.Equal("C#", enrichedType.Properties["language"]);
        Assert.Equal("contract", enrichedType.Properties["framework.role"]);
        Assert.Contains(enrichmentEvidence.EvidenceId, enrichedType.EvidenceIds);

        var baselineEdge = Assert.Single(baseline.Edges);
        var preservedEdge = Assert.Single(composed.Edges, edge => edge.EdgeId == baselineEdge.EdgeId);
        Assert.Equal(baselineEdge.Kind, preservedEdge.Kind);
        Assert.Equal(baselineEdge.FromNodeId, preservedEdge.FromNodeId);
        Assert.Equal(baselineEdge.ToNodeId, preservedEdge.ToNodeId);
        Assert.Equal(baselineEdge.EvidenceIds, preservedEdge.EvidenceIds);
        Assert.Contains(composed.Evidence, evidence => evidence.EvidenceId == baseline.Evidence[0].EvidenceId);
    }

    [Fact]
    public void Composition_is_independent_of_contribution_record_order()
    {
        var baseline = CreateBaseline();
        var firstEvidence = CreateEvidence(
            baseline.Snapshot,
            "test.first",
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Exact,
            spanStart: 1);
        var secondEvidence = CreateEvidence(
            baseline.Snapshot,
            "test.second",
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Exact,
            spanStart: 2);
        var firstNode = CreateUnscopedNode(
            baseline.Snapshot,
            "ConfiguredThing",
            "sample.cs#first",
            firstEvidence.EvidenceId);
        var secondNode = CreateUnscopedNode(
            baseline.Snapshot,
            "ConfiguredThing",
            "sample.cs#second",
            secondEvidence.EvidenceId);
        var edge = CreateEdge(
            baseline.Snapshot,
            "ConfiguredRelationship",
            firstNode.NodeId,
            secondNode.NodeId,
            firstEvidence.EvidenceId);
        var firstDiagnostic = CreateDiagnostic("first", firstEvidence.EvidenceId);
        var secondDiagnostic = CreateDiagnostic("second", secondEvidence.EvidenceId);

        var forward = new EvidenceGraphContribution(
            AnalysisStatus.Success,
            new[] { firstNode, secondNode },
            new[] { edge },
            new[] { firstEvidence, secondEvidence },
            new[] { firstDiagnostic, secondDiagnostic });
        var reverse = new EvidenceGraphContribution(
            AnalysisStatus.Success,
            new[] { secondNode, firstNode },
            new[] { edge },
            new[] { secondEvidence, firstEvidence },
            new[] { secondDiagnostic, firstDiagnostic });

        var first = AnalysisDocumentComposer.Compose(baseline, forward);
        var second = AnalysisDocumentComposer.Compose(baseline, reverse);

        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
        Assert.Equal(
            AnalysisJson.Serialize(first, indented: false),
            AnalysisJson.Serialize(second, indented: false));
    }

    [Theory]
    [InlineData(AnalysisStatus.Success, AnalysisStatus.Success, AnalysisStatus.Success)]
    [InlineData(AnalysisStatus.Success, AnalysisStatus.PartialSuccess, AnalysisStatus.PartialSuccess)]
    [InlineData(AnalysisStatus.PartialSuccess, AnalysisStatus.Success, AnalysisStatus.PartialSuccess)]
    [InlineData(AnalysisStatus.Success, AnalysisStatus.Failure, AnalysisStatus.Failure)]
    [InlineData(AnalysisStatus.Failure, AnalysisStatus.PartialSuccess, AnalysisStatus.Failure)]
    public void Composition_status_uses_the_most_severe_input_status(
        AnalysisStatus baselineStatus,
        AnalysisStatus contributionStatus,
        AnalysisStatus expectedStatus)
    {
        var baseline = AnalysisJson.WithCanonicalHash(
            CreateBaseline() with { Status = baselineStatus });
        var contribution = EvidenceGraphContribution.Empty with { Status = contributionStatus };

        var composed = AnalysisDocumentComposer.Compose(baseline, contribution);

        Assert.Equal(expectedStatus, composed.Status);
    }

    [Fact]
    public void Composer_rejects_incompatible_identity_and_nonidentity_collisions()
    {
        var baseline = CreateBaseline();
        var baselineType = Assert.Single(baseline.Nodes, node => node.Kind == "Class");
        var baselineEvidence = Assert.Single(baseline.Evidence);
        var baselineEdge = Assert.Single(baseline.Edges);

        Assert.Throws<InvalidDataException>(() => AnalysisDocumentComposer.Compose(
            baseline,
            Contribution(nodes: new[] { baselineType with { Name = "DifferentName" } })));

        Assert.Throws<InvalidDataException>(() => AnalysisDocumentComposer.Compose(
            baseline,
            Contribution(nodes: new[]
            {
                baselineType with
                {
                    Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["language"] = "DifferentLanguage",
                    }
                }
            })));

        Assert.Throws<InvalidDataException>(() => AnalysisDocumentComposer.Compose(
            baseline,
            Contribution(nodes: new[]
            {
                baselineType with { NodeId = $"node:{new string('0', 64)}" }
            })));

        Assert.Throws<InvalidDataException>(() => AnalysisDocumentComposer.Compose(
            baseline,
            Contribution(evidence: new[]
            {
                baselineEvidence with
                {
                    Span = baselineEvidence.Span with
                    {
                        EndColumn = baselineEvidence.Span.EndColumn + 1,
                    }
                }
            })));

        Assert.Throws<InvalidDataException>(() => AnalysisDocumentComposer.Compose(
            baseline,
            Contribution(edges: new[]
            {
                baselineEdge with
                {
                    Resolution = baselineEdge.Resolution with { Details = "Conflicting details" }
                }
            })));
    }

    private static EvidenceGraphContribution Contribution(
        IReadOnlyList<EvidenceNode>? nodes = null,
        IReadOnlyList<EvidenceEdge>? edges = null,
        IReadOnlyList<EvidenceRecord>? evidence = null) =>
        new(
            AnalysisStatus.Success,
            nodes ?? Array.Empty<EvidenceNode>(),
            edges ?? Array.Empty<EvidenceEdge>(),
            evidence ?? Array.Empty<EvidenceRecord>(),
            Array.Empty<AnalysisDiagnostic>());

    private static AnalysisDocument CreateBaseline()
    {
        const string source = "public sealed class Widget { }";
        var contentHash = CanonicalIdentity.Sha256Hex(source);
        var snapshot = RepositorySnapshot.Create(
            null,
            new[] { new ManifestEntry("sample.cs", contentHash, source.Length) });
        var evidence = CreateEvidence(
            snapshot,
            "test.baseline",
            ResolutionBasis.Syntax,
            ResolutionQuality.Exact);

        const string projectQualifiedName = "sample.csproj";
        var projectLogicalId = CanonicalIdentity.CreateLogicalNodeId(
            null,
            "Project",
            projectQualifiedName);
        var projectNodeId = CanonicalIdentity.CreateNodeId(snapshot.SnapshotId, projectLogicalId);
        var project = new EvidenceNode(
            projectNodeId,
            projectLogicalId,
            "Project",
            "sample",
            projectQualifiedName,
            null,
            new[] { evidence.EvidenceId },
            Array.Empty<string>(),
            new Dictionary<string, string>(StringComparer.Ordinal));

        const string typeQualifiedName = "Sample.Widget";
        var typeLogicalId = CanonicalIdentity.CreateLogicalNodeId(
            projectQualifiedName,
            "Class",
            typeQualifiedName);
        var typeNodeId = CanonicalIdentity.CreateNodeId(snapshot.SnapshotId, typeLogicalId);
        var type = new EvidenceNode(
            typeNodeId,
            typeLogicalId,
            "Class",
            "Widget",
            typeQualifiedName,
            projectNodeId,
            new[] { evidence.EvidenceId },
            new[] { "Serializable" },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["language"] = "C#",
            });

        var edge = CreateEdge(
            snapshot,
            "Contains",
            projectNodeId,
            typeNodeId,
            evidence.EvidenceId);
        return AnalysisJson.WithCanonicalHash(new AnalysisDocument(
            AnalysisSchema.CurrentVersion,
            AnalysisStatus.Success,
            snapshot,
            new[] { project, type },
            new[] { edge },
            new[] { evidence },
            Array.Empty<AnalysisDiagnostic>()));
    }

    private static EvidenceRecord CreateEvidence(
        RepositorySnapshot snapshot,
        string ruleId,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details = null,
        int spanStart = 0)
    {
        var entry = Assert.Single(snapshot.Manifest);
        var span = new SourceSpan(spanStart, 1, 1, spanStart + 1, 1, spanStart + 2);
        var provenance = new ExtractorProvenance("test.composer", "1.0.0", ruleId);
        var resolution = new EvidenceResolution(basis, quality, details);
        var evidenceId = CanonicalIdentity.CreateEvidenceId(
            snapshot.SnapshotId,
            entry.Path,
            entry.ContentHash,
            span.StartOffset,
            span.Length,
            provenance.ExtractorId,
            provenance.ExtractorVersion,
            provenance.RuleId,
            resolution.Basis,
            resolution.Quality);
        return new EvidenceRecord(
            evidenceId,
            snapshot.SnapshotId,
            entry.Path,
            entry.ContentHash,
            span,
            provenance,
            resolution);
    }

    private static EvidenceNode CreateUnscopedNode(
        RepositorySnapshot snapshot,
        string kind,
        string qualifiedName,
        string evidenceId)
    {
        var logicalId = CanonicalIdentity.CreateLogicalNodeId(null, kind, qualifiedName);
        return new EvidenceNode(
            CanonicalIdentity.CreateNodeId(snapshot.SnapshotId, logicalId),
            logicalId,
            kind,
            qualifiedName[(qualifiedName.LastIndexOf('#') + 1)..],
            qualifiedName,
            null,
            new[] { evidenceId },
            Array.Empty<string>(),
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static EvidenceEdge CreateEdge(
        RepositorySnapshot snapshot,
        string kind,
        string fromNodeId,
        string toNodeId,
        string evidenceId)
    {
        var resolution = new EvidenceResolution(ResolutionBasis.Syntax, ResolutionQuality.Exact);
        var evidenceIds = new[] { evidenceId };
        return new EvidenceEdge(
            CanonicalIdentity.CreateEdgeId(
                snapshot.SnapshotId,
                kind,
                fromNodeId,
                toNodeId,
                null,
                resolution.Basis,
                resolution.Quality,
                evidenceIds),
            kind,
            fromNodeId,
            toNodeId,
            null,
            evidenceIds,
            resolution);
    }

    private static AnalysisDiagnostic CreateDiagnostic(string value, string evidenceId) =>
        new(
            "test.same-sort-prefix",
            DiagnosticSeverity.Warning,
            "Same message",
            "sample.cs",
            new[] { evidenceId },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = value,
            });
}
