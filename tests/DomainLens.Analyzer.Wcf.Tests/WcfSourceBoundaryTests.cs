using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfSourceBoundaryTests
{
    [Fact]
    public async Task Shared_source_selected_by_two_projects_is_retained_as_ambiguous_without_guessing()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfSharedSource");
        var document = await AnalyzeAsync(fixture.Path);

        var contracts = document.Nodes
            .Where(node => node.QualifiedName == "Fixtures.Wcf.Shared.ISharedContract")
            .ToArray();
        Assert.Equal(2, contracts.Length);
        Assert.All(contracts, node =>
            Assert.Contains(WcfVocabulary.Attributes.ServiceContract, node.Attributes));
        Assert.Equal(2, contracts.Select(node => node.ProjectId).Distinct().Count());
        var operations = document.Nodes
            .Where(node => node.QualifiedName.Contains(
                "Fixtures.Wcf.Shared.ISharedContract.Ping(",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, operations.Length);
        Assert.All(operations, node =>
            Assert.DoesNotContain(WcfVocabulary.Attributes.OperationContract, node.Attributes));

        var endpoint = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.Endpoint);
        var relationship = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.EndpointContract &&
            edge.FromNodeId == endpoint.NodeId);
        Assert.Null(relationship.ToNodeId);
        Assert.Equal("Fixtures.Wcf.Shared.ISharedContract", relationship.UnresolvedTarget);
        Assert.Equal(ResolutionQuality.Ambiguous, relationship.Resolution.Quality);
        var svcRelationship = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.HostsService &&
            edge.UnresolvedTarget == "Fixtures.Wcf.Shared.SharedService");
        Assert.Null(svcRelationship.ToNodeId);
        Assert.Equal(ResolutionQuality.Ambiguous, svcRelationship.Resolution.Quality);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.AttributeIdentityUnresolved &&
            diagnostic.Properties.GetValueOrDefault("candidateCount") == "2");
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedSourcePattern &&
            diagnostic.RelativePath == "SharedContract.cs" &&
            diagnostic.Message.Contains("OperationContract", StringComparison.Ordinal));
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Framework_attributes_outside_supported_containers_are_diagnosed_not_promoted()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfAmbiguousHostile");
        var document = await AnalyzeAsync(fixture.Path);

        var dataMember = MemberNode(
            document,
            "Fixtures.Wcf.AmbiguousHostile.NotADataContract.Value");
        var header = MemberNode(
            document,
            "Fixtures.Wcf.AmbiguousHostile.NotAMessageContract.Header");
        var body = MemberNode(
            document,
            "Fixtures.Wcf.AmbiguousHostile.NotAMessageContract.Body");
        var invalidService = Assert.Single(document.Nodes, node =>
            node.QualifiedName ==
            "Fixtures.Wcf.AmbiguousHostile.InvalidServiceContractPlacement");
        var unsupportedOperation = Assert.Single(document.Nodes, node =>
            node.QualifiedName.Contains(
                "Fixtures.Wcf.AmbiguousHostile.NotAServiceContract.Execute(",
                StringComparison.Ordinal));

        Assert.DoesNotContain(WcfVocabulary.Attributes.DataMember, dataMember.Attributes);
        Assert.DoesNotContain(WcfVocabulary.Attributes.MessageHeader, header.Attributes);
        Assert.DoesNotContain(WcfVocabulary.Attributes.MessageBodyMember, body.Attributes);
        Assert.DoesNotContain(WcfVocabulary.Attributes.ServiceContract, invalidService.Attributes);
        Assert.DoesNotContain(WcfVocabulary.Attributes.OperationContract, unsupportedOperation.Attributes);
        Assert.True(document.Diagnostics.Count(diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedSourcePattern &&
            diagnostic.RelativePath == "UnsupportedPlacements.cs") >= 6);
        AssertValid(document);
    }

    [Fact]
    public async Task Unavailable_fault_detail_is_unresolved_and_forces_partial_success()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfAmbiguousHostile");
        var document = await AnalyzeAsync(fixture.Path);

        var edge = Assert.Single(document.Edges, candidate =>
            candidate.Kind == WcfVocabulary.EdgeKinds.FaultDetailType &&
            candidate.UnresolvedTarget is not null &&
            candidate.UnresolvedTarget.Contains("FaultDetail", StringComparison.Ordinal));
        Assert.Null(edge.ToNodeId);
        Assert.Equal(ResolutionQuality.Unresolved, edge.Resolution.Quality);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.GeneratedTypeUnavailable &&
            diagnostic.RelativePath == "MissingTypes.cs");
        AssertValid(document);
    }

    [Fact]
    public async Task Header_and_body_annotations_on_one_member_retain_construct_specific_metadata()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfRich");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "ContradictoryMessageMember.cs"),
            """
            using System.ServiceModel;

            namespace Fixtures.Wcf.Rich
            {
                [MessageContract]
                public sealed class ContradictoryMessage
                {
                    [MessageHeader(Name = "header-name", Namespace = "urn:header")]
                    [MessageBodyMember(Name = "body-name", Namespace = "urn:body", Order = 7)]
                    public string Value;
                }
            }
            """);
        var projectPath = System.IO.Path.Combine(fixture.Path, "WcfRich.csproj");
        var project = await File.ReadAllTextAsync(projectPath);
        project = project.Replace(
            "    <Compile Include=\"PartialContract.Attribute.cs\" />",
            "    <Compile Include=\"PartialContract.Attribute.cs\" />" + Environment.NewLine +
            "    <Compile Include=\"ContradictoryMessageMember.cs\" />",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectPath, project);

        var document = await AnalyzeAsync(fixture.Path);

        var member = MemberNode(
            document,
            "Fixtures.Wcf.Rich.ContradictoryMessage.Value");
        Assert.Contains(WcfVocabulary.Attributes.MessageHeader, member.Attributes);
        Assert.Contains(WcfVocabulary.Attributes.MessageBodyMember, member.Attributes);
        Assert.Equal("header-name", member.Properties[WcfVocabulary.Properties.MessageHeaderName]);
        Assert.Equal("urn:header", member.Properties[WcfVocabulary.Properties.MessageHeaderNamespace]);
        Assert.Equal("body-name", member.Properties[WcfVocabulary.Properties.MessageBodyMemberName]);
        Assert.Equal("urn:body", member.Properties[WcfVocabulary.Properties.MessageBodyMemberNamespace]);
        Assert.Equal("7", member.Properties[WcfVocabulary.Properties.MessageBodyMemberOrder]);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.MessageHeader &&
            edge.ToNodeId == member.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.MessageBodyMember &&
            edge.ToNodeId == member.NodeId);
        AssertValid(document);
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static EvidenceNode MemberNode(AnalysisDocument document, string qualifiedNamePrefix) =>
        Assert.Single(document.Nodes, node =>
            node.QualifiedName.StartsWith(qualifiedNamePrefix + ":", StringComparison.Ordinal));

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
