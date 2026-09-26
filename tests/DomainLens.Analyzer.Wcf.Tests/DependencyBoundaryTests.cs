namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Wcf_analyzer_keeps_untrusted_content_analysis_out_of_trusted_and_delivery_layers()
    {
        var referencedAssemblies = typeof(ClassicWcfAnalyzer)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("DomainLens.Analyzer.Host", referencedAssemblies);
        Assert.DoesNotContain("DomainLens.Analyzer.Worker", referencedAssemblies);
        Assert.DoesNotContain("DomainLens.Analyzer.Protocol", referencedAssemblies);
        Assert.DoesNotContain("DomainLens.Scanner", referencedAssemblies);
        Assert.DoesNotContain("DomainLens.Cli", referencedAssemblies);
    }
}
