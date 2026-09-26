using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfFixtureContractTests
{
    public static TheoryData<string, string> FixtureProjects => new()
    {
        { "WcfBasic", "WcfBasic.csproj" },
        { "WcfRich", "WcfRich.csproj" },
        { "WcfInheritance", "WcfInheritance.csproj" },
        { "WcfHostingConfig", "WcfHostingConfig.csproj" },
        { "WcfProgrammatic", "WcfProgrammatic.csproj" },
        { "WcfAmbiguousHostile", "WcfAmbiguousHostile.csproj" },
    };

    [Theory]
    [MemberData(nameof(FixtureProjects))]
    public void Fixture_copy_is_inert_and_contains_no_generated_build_output(
        string fixtureName,
        string projectName)
    {
        using var fixture = TemporaryWcfFixture.Copy(fixtureName);

        Assert.True(File.Exists(System.IO.Path.Combine(fixture.Path, projectName)));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "bin")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "obj")));
        Assert.Empty(Directory.EnumerateFiles(fixture.Path, "*.marker", SearchOption.AllDirectories));
        Assert.False(File.Exists(fixture.ExtensionMarkerPath));
    }

    [Fact]
    public void Hostile_fixture_retains_inert_security_probes()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfAmbiguousHostile");
        var extensionSource = File.ReadAllText(
            System.IO.Path.Combine(fixture.Path, "CustomExtension.cs"));
        var escapedMarkerPath = fixture.ExtensionMarkerPath
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        Assert.Contains(
            "WCF_PROJECT_BUILD_SHOULD_NOT_EXIST.marker",
            File.ReadAllText(System.IO.Path.Combine(fixture.Path, "WcfAmbiguousHostile.csproj")),
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"{escapedMarkerPath}\"",
            extensionSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TemporaryWcfFixture.ExtensionMarkerPlaceholder,
            extensionSource,
            StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.ExtensionMarkerPath));
        Assert.Contains(
            "<!DOCTYPE",
            File.ReadAllText(System.IO.Path.Combine(fixture.Path, "dtd.config")),
            StringComparison.Ordinal);
        Assert.Contains(
            "configSource",
            File.ReadAllText(System.IO.Path.Combine(fixture.Path, "external.config")),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FixtureProjects))]
    public async Task Fixture_is_safely_scannable_without_executing_repository_content(
        string fixtureName,
        string projectName)
    {
        using var fixture = TemporaryWcfFixture.Copy(fixtureName);

        var document = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(fixture.Path));

        Assert.NotEqual(AnalysisStatus.Failure, document.Status);
        Assert.Contains(
            document.Nodes,
            node => node.Kind == "Project" && node.QualifiedName == projectName);
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.Empty(Directory.EnumerateFiles(fixture.Path, "*.marker", SearchOption.AllDirectories));
        Assert.False(File.Exists(fixture.ExtensionMarkerPath));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "bin")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "obj")));
    }
}
