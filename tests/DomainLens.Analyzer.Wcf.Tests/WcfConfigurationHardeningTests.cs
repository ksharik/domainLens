using System.Text;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfConfigurationHardeningTests
{
    [Fact]
    public async Task Oversized_configuration_section_stops_before_external_reference_traversal()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var configuration = new StringBuilder(
            "<configuration><system.serviceModel>");
        const int elementCount = 4200;
        for (var index = 0; index < elementCount; index++)
        {
            configuration
                .Append("<unsupported configSource=\"external-")
                .Append(index)
                .Append(".config\" />");
        }

        configuration.Append("</system.serviceModel></configuration>");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Oversized.config"),
            configuration.ToString());

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ConfigurationTraversalLimitExceeded &&
            candidate.RelativePath == "Oversized.config");
        Assert.Equal("4096", diagnostic.Properties["limit"]);
        Assert.Equal(
            "namespace-qualified configuration metadata",
            diagnostic.Properties["scope"]);
        Assert.DoesNotContain(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ExternalConfigurationNotResolved &&
            candidate.RelativePath == "Oversized.config");

        var evidence = Assert.Single(document.Evidence, candidate =>
            candidate.RelativePath == "Oversized.config");
        Assert.Equal(
            WcfVocabulary.Rules.ConfigurationTraversalLimit,
            evidence.Provenance.RuleId);
        Assert.Contains(evidence.EvidenceId, diagnostic.EvidenceIds);
        Assert.Equal(ResolutionQuality.Partial, evidence.Resolution.Quality);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Deep_unsupported_behavior_metadata_is_iterative_and_bounded()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var configuration = new StringBuilder(
            "<configuration><system.serviceModel><behaviors><serviceBehaviors>" +
            "<behavior name=\"deep\"><serviceCredentials>");
        const int nestingDepth = 512;
        for (var index = 0; index < nestingDepth; index++)
        {
            configuration.Append("<n").Append(index).Append('>');
        }

        for (var index = nestingDepth - 1; index >= 0; index--)
        {
            configuration.Append("</n").Append(index).Append('>');
        }

        configuration.Append(
            "</serviceCredentials></behavior></serviceBehaviors></behaviors>" +
            "</system.serviceModel></configuration>");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Deep.config"),
            configuration.ToString());

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ConfigurationTraversalLimitExceeded &&
            candidate.RelativePath == "Deep.config");
        Assert.Equal("256", diagnostic.Properties["limit"]);
        Assert.Equal("unsupported behavior metadata", diagnostic.Properties["scope"]);
        var evidence = Assert.Single(document.Evidence, candidate =>
            candidate.Provenance.RuleId == WcfVocabulary.Rules.ConfigurationTraversalLimit &&
            candidate.RelativePath == "Deep.config");
        Assert.Contains(evidence.EvidenceId, diagnostic.EvidenceIds);
        Assert.Equal(ResolutionQuality.Partial, evidence.Resolution.Quality);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Xdt_transform_metadata_is_inert_and_prevents_declaration_promotion()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        const string configuration = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <system.serviceModel>
                <services>
                  <service name="Fixtures.Wcf.Basic.CustomerService" xdt:Transform="Remove">
                    <endpoint contract="Fixtures.Wcf.Basic.ICustomerService" binding="basicHttpBinding" />
                  </service>
                </services>
              </system.serviceModel>
            </configuration>
            """;
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Web.Release.config"),
            configuration);

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ConfigurationTransformNotApplied &&
            candidate.RelativePath == "Web.Release.config");
        var evidence = Assert.Single(document.Evidence, candidate =>
            candidate.Provenance.RuleId == WcfVocabulary.Rules.ConfigurationTransform &&
            candidate.RelativePath == "Web.Release.config");
        Assert.Contains(evidence.EvidenceId, diagnostic.EvidenceIds);
        Assert.DoesNotContain(document.Nodes, node =>
            node.Kind is WcfVocabulary.NodeKinds.ConfiguredService or
                WcfVocabulary.NodeKinds.Endpoint or
                WcfVocabulary.NodeKinds.Binding or
                WcfVocabulary.NodeKinds.Behavior or
                WcfVocabulary.NodeKinds.ServiceActivation or
                WcfVocabulary.NodeKinds.ExtensionDeclaration);
        Assert.Contains(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.UnsupportedConfiguration &&
            candidate.RelativePath == "Web.Release.config" &&
            candidate.Message.Contains("Namespace-qualified", StringComparison.Ordinal));
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Non_transform_namespace_metadata_is_diagnosed_without_hiding_static_declarations()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        const string configuration = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:custom="urn:domainlens:test-extension">
              <system.serviceModel custom:note="inert">
                <custom:extension />
                <services>
                  <service name="Fixtures.Wcf.Basic.CustomerService" />
                </services>
              </system.serviceModel>
            </configuration>
            """;
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Namespaced.config"),
            configuration);

        var document = await AnalyzeAsync(fixture.Path);

        Assert.Contains(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.ConfiguredService &&
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.ConfigServiceName) ==
            "Fixtures.Wcf.Basic.CustomerService");
        Assert.Contains(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.UnsupportedConfiguration &&
            candidate.RelativePath == "Namespaced.config" &&
            candidate.Message.Contains("Namespace-qualified configuration attribute", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.UnsupportedConfiguration &&
            candidate.RelativePath == "Namespaced.config" &&
            candidate.Message.Contains("urn:domainlens:test-extension", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ConfigurationTransformNotApplied);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task External_and_unsupported_configuration_syntax_uses_specific_rule_ids()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        const string configuration = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <system.serviceModel file="external.config" unexpected="value">
                <mystery />
              </system.serviceModel>
            </configuration>
            """;
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "RuleIds.config"),
            configuration);

        var document = await AnalyzeAsync(fixture.Path);
        var evidence = document.Evidence
            .Where(item => item.RelativePath == "RuleIds.config")
            .ToDictionary(item => item.Provenance.RuleId, StringComparer.Ordinal);

        Assert.Contains(WcfVocabulary.Rules.ConfigurationExternalReference, evidence.Keys);
        Assert.Contains(WcfVocabulary.Rules.ConfigurationUnsupportedAttribute, evidence.Keys);
        Assert.Contains(WcfVocabulary.Rules.ConfigurationUnsupportedElement, evidence.Keys);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.ExternalConfigurationNotResolved &&
            diagnostic.EvidenceIds.Contains(
                evidence[WcfVocabulary.Rules.ConfigurationExternalReference].EvidenceId,
                StringComparer.Ordinal));
        Assert.True(document.Diagnostics.Count(diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedConfiguration &&
            diagnostic.RelativePath == "RuleIds.config") >= 2);
        AssertValid(document);
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
