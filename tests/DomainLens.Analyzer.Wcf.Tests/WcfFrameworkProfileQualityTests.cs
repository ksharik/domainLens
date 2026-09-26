using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfFrameworkProfileQualityTests
{
    private const string Net472Property =
        "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>";

    [Fact]
    public async Task Net472_source_attribute_observation_can_remain_exact()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");

        var document = await AnalyzeAsync(fixture.Path);

        var evidence = ServiceContractEvidence(document);
        Assert.Equal(ResolutionQuality.Exact, evidence.Resolution.Quality);
        Assert.DoesNotContain(
            document.Diagnostics,
            diagnostic => diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedFrameworkProfile);
        AssertValid(document);
    }

    [Fact]
    public async Task Net461_source_attribute_is_partial_and_emits_profile_diagnostic()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await ReplaceFrameworkAsync(
            fixture.Path,
            "WcfBasic.csproj",
            "    <TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>");

        var document = await AnalyzeAsync(fixture.Path);

        Assert.Equal(ResolutionQuality.Partial, ServiceContractEvidence(document).Resolution.Quality);
        AssertProfileDiagnostic(document, "v4.6.1");
        AssertValid(document);
    }

    [Fact]
    public async Task Net48_programmatic_and_client_observations_are_not_exact()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfProgrammatic");
        await ReplaceFrameworkAsync(
            fixture.Path,
            "WcfProgrammatic.csproj",
            "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>");

        var document = await AnalyzeAsync(fixture.Path);
        var rules = new[]
        {
            WcfVocabulary.Rules.ServiceHost,
            WcfVocabulary.Rules.ChannelFactory,
            WcfVocabulary.Rules.ClientBase,
        };
        var evidence = WcfEvidence(document)
            .Where(item => rules.Contains(item.Provenance.RuleId, StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.NotEqual(ResolutionQuality.Exact, item.Resolution.Quality));
        Assert.Contains(evidence, item => item.Provenance.RuleId == WcfVocabulary.Rules.ServiceHost);
        Assert.Contains(evidence, item => item.Provenance.RuleId == WcfVocabulary.Rules.ChannelFactory);
        Assert.Contains(evidence, item => item.Provenance.RuleId == WcfVocabulary.Rules.ClientBase);
        AssertProfileDiagnostic(document, "v4.8");
        AssertValid(document);
    }

    [Fact]
    public async Task Unknown_framework_source_observation_is_not_exact()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await ReplaceFrameworkAsync(fixture.Path, "WcfBasic.csproj", string.Empty);

        var document = await AnalyzeAsync(fixture.Path);

        Assert.NotEqual(ResolutionQuality.Exact, ServiceContractEvidence(document).Resolution.Quality);
        AssertProfileDiagnostic(document, "unknown");
        AssertValid(document);
    }

    [Fact]
    public async Task Multiple_framework_source_observation_is_not_exact()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await ReplaceFrameworkAsync(
            fixture.Path,
            "WcfBasic.csproj",
            "    <TargetFrameworks>net472;net48</TargetFrameworks>");

        var document = await AnalyzeAsync(fixture.Path);

        Assert.NotEqual(ResolutionQuality.Exact, ServiceContractEvidence(document).Resolution.Quality);
        AssertProfileDiagnostic(document, "net472;net48");
        AssertValid(document);
    }

    [Fact]
    public async Task Conditional_framework_source_observation_is_not_exact_even_with_net472_literal()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await ReplaceFrameworkAsync(
            fixture.Path,
            "WcfBasic.csproj",
            Net472Property + Environment.NewLine +
            "    <TargetFrameworkVersion Condition=\" '$(Configuration)' == 'Release' \">v4.8</TargetFrameworkVersion>");

        var document = await AnalyzeAsync(fixture.Path);

        Assert.NotEqual(ResolutionQuality.Exact, ServiceContractEvidence(document).Resolution.Quality);
        AssertProfileDiagnostic(document, "v4.7.2");
        AssertValid(document);
    }

    [Fact]
    public async Task Unsupported_source_profile_does_not_downgrade_declarative_configuration()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfHostingConfig");
        await ReplaceFrameworkAsync(
            fixture.Path,
            "WcfHostingConfig.csproj",
            "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>");

        var document = await AnalyzeAsync(fixture.Path);
        var configuredEndpoints = WcfEvidence(document)
            .Where(evidence => evidence.Provenance.RuleId == WcfVocabulary.Rules.ConfigurationEndpoint)
            .ToArray();

        Assert.NotEmpty(configuredEndpoints);
        Assert.All(configuredEndpoints, configuredEndpoint =>
        {
            Assert.Equal(ResolutionBasis.DeclarativeConfiguration, configuredEndpoint.Resolution.Basis);
            Assert.Equal(ResolutionQuality.Exact, configuredEndpoint.Resolution.Quality);
        });
        AssertProfileDiagnostic(document, "v4.8");
        AssertValid(document);
    }

    [Fact]
    public async Task Source_to_source_relationships_remain_partial_for_supported_profile()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");

        var document = await AnalyzeAsync(fixture.Path);
        var relationship = Assert.Single(
            document.Edges,
            edge => edge.Kind == WcfVocabulary.EdgeKinds.ImplementsContract);

        Assert.Equal(ResolutionBasis.Semantic, relationship.Resolution.Basis);
        Assert.Equal(ResolutionQuality.Partial, relationship.Resolution.Quality);
        AssertValid(document);
    }

    private static async Task ReplaceFrameworkAsync(
        string repositoryPath,
        string projectName,
        string replacement)
    {
        var projectPath = System.IO.Path.Combine(repositoryPath, projectName);
        var project = await File.ReadAllTextAsync(projectPath);
        Assert.Contains(Net472Property, project, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            projectPath,
            project.Replace(Net472Property, replacement, StringComparison.Ordinal));
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static EvidenceRecord ServiceContractEvidence(AnalysisDocument document) =>
        Assert.Single(
            WcfEvidence(document),
            evidence => evidence.Provenance.RuleId == WcfVocabulary.Rules.ServiceContract &&
                        evidence.RelativePath == "Contracts.cs");

    private static IEnumerable<EvidenceRecord> WcfEvidence(AnalysisDocument document) =>
        document.Evidence.Where(evidence =>
            evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId);

    private static void AssertProfileDiagnostic(
        AnalysisDocument document,
        string declaredFrameworks)
    {
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedFrameworkProfile &&
            diagnostic.Properties.GetValueOrDefault("declaredTargetFrameworks") == declaredFrameworks);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
    }

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
