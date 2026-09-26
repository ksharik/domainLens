using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfSourceAmbiguityRegressionTests
{
    [Fact]
    public async Task Shared_service_implementation_candidates_retain_ambiguous_contract_relationships()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfSharedSource");
        var document = await AnalyzeAsync(fixture.Path);

        var implementations = document.Nodes
            .Where(node => node.QualifiedName == "Fixtures.Wcf.Shared.SharedService")
            .ToArray();
        Assert.Equal(2, implementations.Length);

        var relationships = document.Edges
            .Where(edge =>
                edge.Kind == WcfVocabulary.EdgeKinds.ImplementsContract &&
                edge.UnresolvedTarget == "Fixtures.Wcf.Shared.ISharedContract")
            .ToArray();
        Assert.Equal(2, relationships.Length);
        Assert.Equal(
            implementations.Select(node => node.NodeId).Order(StringComparer.Ordinal),
            relationships.Select(edge => edge.FromNodeId).Order(StringComparer.Ordinal));
        Assert.All(relationships, edge =>
        {
            Assert.Null(edge.ToNodeId);
            Assert.Equal(ResolutionQuality.Ambiguous, edge.Resolution.Quality);
        });
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.ImplementationAmbiguous &&
            diagnostic.RelativePath == "SharedContract.cs" &&
            diagnostic.Properties.GetValueOrDefault("implementation") ==
            "Fixtures.Wcf.Shared.SharedService" &&
            diagnostic.Properties.GetValueOrDefault("contract") ==
            "Fixtures.Wcf.Shared.ISharedContract");
        AssertValid(document);
    }

    [Fact]
    public async Task Shared_source_targets_produce_ambiguous_source_relationships_not_unresolved_targets()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfSharedSource");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "SharedTargets.cs"),
            """
            using System.Runtime.Serialization;
            using System.ServiceModel;

            namespace Fixtures.Wcf.Shared
            {
                public interface ISharedCallback
                {
                    [OperationContract]
                    void Notify();
                }

                [DataContract]
                public sealed class SharedData
                {
                    [DataMember]
                    public string Value { get; set; }
                }

                [DataContract]
                public sealed class SharedFault
                {
                    [DataMember]
                    public string Reason { get; set; }
                }

                public sealed class SharedHostedService
                {
                }

                [ServiceContract]
                public interface ISharedClientContract
                {
                    [OperationContract]
                    void Send();
                }
            }
            """);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "UniqueRelationships.cs"),
            """
            using System.ServiceModel;

            namespace Fixtures.Wcf.Shared
            {
                [ServiceContract(CallbackContract = typeof(ISharedCallback))]
                public interface IUniqueDuplexContract
                {
                    [OperationContract]
                    [FaultContract(typeof(SharedFault))]
                    SharedData Exchange(SharedData request);
                }

                public abstract class UniqueProxy : ClientBase<ISharedClientContract>
                {
                }

                public sealed class UniqueProgrammaticClient
                {
                    public void Open()
                    {
                        var host = new ServiceHost(typeof(SharedHostedService));
                        var channel = new ChannelFactory<ISharedClientContract>();
                    }
                }
            }
            """);

        AddCompileItems(
            System.IO.Path.Combine(fixture.Path, "ProjectA.csproj"),
            "SharedTargets.cs",
            "UniqueRelationships.cs");
        AddCompileItems(
            System.IO.Path.Combine(fixture.Path, "ProjectB.csproj"),
            "SharedTargets.cs");

        var document = await AnalyzeAsync(fixture.Path);

        var clientRelationships = document.Edges.Where(edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ClientContract &&
            edge.UnresolvedTarget == "Fixtures.Wcf.Shared.ISharedClientContract").ToArray();
        Assert.Multiple(
            () => AssertAmbiguousTarget(
                document,
                WcfVocabulary.EdgeKinds.CallbackContract,
                "Fixtures.Wcf.Shared.ISharedCallback"),
            () => AssertAmbiguousTarget(
                document,
                WcfVocabulary.EdgeKinds.FaultDetailType,
                "Fixtures.Wcf.Shared.SharedFault"),
            () => AssertAmbiguousTarget(
                document,
                WcfVocabulary.EdgeKinds.UsesDataContract,
                "Fixtures.Wcf.Shared.SharedData"),
            () => AssertAmbiguousTarget(
                document,
                WcfVocabulary.EdgeKinds.HostsService,
                "Fixtures.Wcf.Shared.SharedHostedService"),
            () =>
            {
                Assert.Equal(2, clientRelationships.Length);
                Assert.All(clientRelationships, edge =>
                {
                    Assert.Null(edge.ToNodeId);
                    Assert.Equal(ResolutionQuality.Ambiguous, edge.Resolution.Quality);
                });
            });
        AssertValid(document);
    }

    [Fact]
    public async Task Non_net472_programmatic_and_client_only_project_emits_profile_diagnostic()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "ProgrammaticOnly.cs"),
            """
            using System.ServiceModel;

            namespace Fixtures.Wcf.ProfileOnly
            {
                public interface IExternalContract
                {
                }

                public abstract class ProfileOnlyProxy : ClientBase<IExternalContract>
                {
                }

                public sealed class ProfileOnlyHost
                {
                    public void Open()
                    {
                        var host = new ServiceHost(typeof(ProfileOnlyHost));
                        var channel = new ChannelFactory<IExternalContract>();
                    }
                }
            }
            """);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "ProgrammaticOnly.csproj"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <PropertyGroup>
                <ProjectGuid>{D438A829-3552-4264-AB2E-F24049A95F71}</ProjectGuid>
                <OutputType>Library</OutputType>
                <RootNamespace>Fixtures.Wcf.ProfileOnly</RootNamespace>
                <AssemblyName>Fixtures.Wcf.ProfileOnly</AssemblyName>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="System.Core" />
                <Reference Include="System.ServiceModel" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="ProgrammaticOnly.cs" />
              </ItemGroup>
            </Project>
            """);

        var document = await AnalyzeAsync(fixture.Path);

        var proxy = Assert.Single(document.Nodes, node =>
            node.QualifiedName == "Fixtures.Wcf.ProfileOnly.ProfileOnlyProxy");
        Assert.Contains(WcfVocabulary.Attributes.ClientBase, proxy.Attributes);
        Assert.Contains(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.ServiceHostSite &&
            node.ProjectId == proxy.ProjectId);
        Assert.Contains(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.ChannelFactorySite &&
            node.ProjectId == proxy.ProjectId);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedFrameworkProfile &&
            diagnostic.RelativePath == "ProgrammaticOnly.cs" &&
            diagnostic.Properties.GetValueOrDefault("declaredTargetFrameworks") == "v4.8");
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Independent_same_clr_name_declarations_do_not_share_wcf_metadata()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfSharedSource");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "AttributedDuplicate.cs"),
            """
            using System.ServiceModel;

            namespace Fixtures.Wcf.Duplicate
            {
                [ServiceContract]
                public interface IDuplicateContract
                {
                    [OperationContract]
                    void Send();
                }

                public abstract class DuplicateProxy : ClientBase<IDuplicateContract>
                {
                }
            }
            """);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "PlainDuplicate.cs"),
            """
            namespace Fixtures.Wcf.Duplicate
            {
                public interface IDuplicateContract
                {
                    void Ignore();
                }

                public abstract class DuplicateProxy
                {
                }
            }
            """);
        AddCompileItems(
            System.IO.Path.Combine(fixture.Path, "ProjectA.csproj"),
            "AttributedDuplicate.cs");
        AddCompileItems(
            System.IO.Path.Combine(fixture.Path, "ProjectB.csproj"),
            "PlainDuplicate.cs");

        var document = await AnalyzeAsync(fixture.Path);

        var contracts = document.Nodes.Where(node =>
            node.QualifiedName == "Fixtures.Wcf.Duplicate.IDuplicateContract").ToArray();
        Assert.Equal(2, contracts.Length);
        Assert.Single(contracts, node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.ServiceContract, StringComparer.Ordinal));

        var proxies = document.Nodes.Where(node =>
            node.QualifiedName == "Fixtures.Wcf.Duplicate.DuplicateProxy").ToArray();
        Assert.Equal(2, proxies.Length);
        Assert.Single(proxies, node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.ClientBase, StringComparer.Ordinal));

        var attributedEvidence = document.Evidence.Where(evidence =>
            evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId &&
            (evidence.Provenance.RuleId is WcfVocabulary.Rules.ServiceContract or
                WcfVocabulary.Rules.ClientBase) &&
            evidence.RelativePath.Contains("Duplicate.cs", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(attributedEvidence);
        Assert.All(attributedEvidence, evidence =>
            Assert.Equal("AttributedDuplicate.cs", evidence.RelativePath));
        AssertValid(document);
    }

    private static void AddCompileItems(string projectPath, params string[] includes)
    {
        var project = File.ReadAllText(projectPath);
        var itemGroup = string.Join(
            Environment.NewLine,
            includes.Select(include => $"    <Compile Include=\"{include}\" />"));
        project = project.Replace(
            "</Project>",
            $"  <ItemGroup>{Environment.NewLine}{itemGroup}{Environment.NewLine}  </ItemGroup>{Environment.NewLine}</Project>",
            StringComparison.Ordinal);
        File.WriteAllText(projectPath, project);
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static void AssertAmbiguousTarget(
        AnalysisDocument document,
        string edgeKind,
        string target)
    {
        var edge = Assert.Single(document.Edges, candidate =>
            candidate.Kind == edgeKind &&
            candidate.UnresolvedTarget == target);
        Assert.Null(edge.ToNodeId);
        Assert.Equal(ResolutionQuality.Ambiguous, edge.Resolution.Quality);
    }

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
