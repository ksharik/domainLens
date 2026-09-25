using System.Text.Json;
using System.Text.Json.Nodes;
using DomainLens.Core;

namespace DomainLens.Scanner.Tests;

public sealed class CanonicalIdentityValidationTests
{
    [Fact]
    public void Validator_accepts_a_document_whose_identifiers_are_derived_from_persisted_fields()
    {
        var document = CreateValidDocument();

        var result = AnalysisGraphValidator.Validate(document);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues));
    }

    [Fact]
    public void Validator_rejects_rehashed_documents_with_tampered_canonical_identifiers()
    {
        var valid = CreateValidDocument();
        var fakeDigest = new string('0', 64);
        var fakeEvidenceId = $"evidence:{fakeDigest}";
        var fakeLogicalId = $"logical-node:{fakeDigest}";
        var fakeNodeId = $"node:{fakeDigest}";
        var fakeEdgeId = $"edge:{fakeDigest}";

        var cases = new (AnalysisDocument Document, string ExpectedCode)[]
        {
            (
                AnalysisJson.WithCanonicalHash(valid with
                {
                    Snapshot = valid.Snapshot with { SnapshotId = $"snapshot:{fakeDigest}" }
                }),
                "snapshot.id.mismatch"),
            (
                AnalysisJson.WithCanonicalHash(valid with
                {
                    Evidence = new[] { valid.Evidence[0] with { EvidenceId = fakeEvidenceId } }
                }),
                "evidence.id.mismatch"),
            (
                AnalysisJson.WithCanonicalHash(valid with
                {
                    Nodes = valid.Nodes
                        .Select((node, index) => index == 0 ? node with { LogicalId = fakeLogicalId } : node)
                        .ToArray()
                }),
                "node.logical-id.mismatch"),
            (
                AnalysisJson.WithCanonicalHash(valid with
                {
                    Nodes = valid.Nodes
                        .Select((node, index) => index == 0 ? node with { NodeId = fakeNodeId } : node)
                        .ToArray()
                }),
                "node.id.mismatch"),
            (
                AnalysisJson.WithCanonicalHash(valid with
                {
                    Edges = new[] { valid.Edges[0] with { EdgeId = fakeEdgeId } }
                }),
                "edge.id.mismatch"),
            (valid with { CanonicalHash = null }, "document.hash.missing"),
            (
                AnalysisJson.WithCanonicalHash(valid with { SchemaVersion = "domainlens.evidence.v999" }),
                "schema.unsupported")
        };

        foreach (var testCase in cases)
        {
            var result = AnalysisGraphValidator.Validate(testCase.Document);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == testCase.ExpectedCode);
        }
    }

    [Fact]
    public void Analysis_json_rejects_unknown_wrong_case_missing_and_null_required_members()
    {
        var json = AnalysisJson.Serialize(CreateValidDocument(), indented: false);
        var missing = JsonNode.Parse(json)!.AsObject();
        Assert.True(missing.Remove("nodes"));
        var nullCollection = JsonNode.Parse(json)!.AsObject();
        nullCollection["nodes"] = null;

        var invalidDocuments = new[]
        {
            json.Insert(1, "\"unexpected\":true,"),
            json.Replace("\"schemaVersion\"", "\"SchemaVersion\"", StringComparison.Ordinal),
            missing.ToJsonString(),
            nullCollection.ToJsonString(),
        };

        foreach (var invalid in invalidDocuments)
        {
            Assert.ThrowsAny<JsonException>(() => AnalysisJson.Deserialize(invalid));
        }
    }

    private static AnalysisDocument CreateValidDocument()
    {
        var contentHash = CanonicalIdentity.Sha256Hex("x");
        var snapshot = RepositorySnapshot.Create(
            revision: null,
            new[] { new ManifestEntry("sample.cs", contentHash, 1) });
        var span = new SourceSpan(0, 1, 1, 1, 1, 2);
        var provenance = new ExtractorProvenance("test.extractor", "1.0.0", "test.rule");
        var resolution = new EvidenceResolution(ResolutionBasis.Syntax, ResolutionQuality.Exact);
        var evidenceId = CanonicalIdentity.CreateEvidenceId(
            snapshot.SnapshotId,
            "sample.cs",
            contentHash,
            span.StartOffset,
            span.Length,
            provenance.ExtractorId,
            provenance.ExtractorVersion,
            provenance.RuleId,
            resolution.Basis,
            resolution.Quality);
        var evidence = new EvidenceRecord(
            evidenceId,
            snapshot.SnapshotId,
            "sample.cs",
            contentHash,
            span,
            provenance,
            resolution);

        const string projectQualifiedName = "sample.csproj";
        var projectLogicalId = CanonicalIdentity.CreateLogicalNodeId(null, "Project", projectQualifiedName);
        var projectId = CanonicalIdentity.CreateNodeId(snapshot.SnapshotId, projectLogicalId);
        var project = new EvidenceNode(
            projectId,
            projectLogicalId,
            "Project",
            "sample",
            projectQualifiedName,
            null,
            new[] { evidenceId },
            Array.Empty<string>(),
            new Dictionary<string, string>());

        const string typeQualifiedName = "Sample.Widget";
        var typeLogicalId = CanonicalIdentity.CreateLogicalNodeId(
            projectQualifiedName,
            "Class",
            typeQualifiedName);
        var typeId = CanonicalIdentity.CreateNodeId(snapshot.SnapshotId, typeLogicalId);
        var type = new EvidenceNode(
            typeId,
            typeLogicalId,
            "Class",
            "Widget",
            typeQualifiedName,
            projectId,
            new[] { evidenceId },
            Array.Empty<string>(),
            new Dictionary<string, string>());

        var edgeId = CanonicalIdentity.CreateEdgeId(
            snapshot.SnapshotId,
            "Contains",
            projectId,
            typeId,
            null,
            ResolutionBasis.Syntax,
            ResolutionQuality.Exact,
            new[] { evidenceId });
        var edge = new EvidenceEdge(
            edgeId,
            "Contains",
            projectId,
            typeId,
            null,
            new[] { evidenceId },
            resolution);

        return AnalysisJson.WithCanonicalHash(new AnalysisDocument(
            AnalysisSchema.CurrentVersion,
            AnalysisStatus.Success,
            snapshot,
            new[] { project, type },
            new[] { edge },
            new[] { evidence },
            Array.Empty<AnalysisDiagnostic>()));
    }
}
