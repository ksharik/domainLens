using DomainLens.Scanner;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfContextBindingTests
{
    [Fact]
    public async Task Shared_semantic_context_must_match_the_exact_repository_snapshot()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var scanner = new RepositoryScanner();
        var original = await scanner.AnalyzeAsync(new ScannerOptions(fixture.Path));
        var context = await new LegacySemanticCompilationService().CreateAsync(fixture.Path, original);

        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "snapshot-change.txt"),
            "changes the captured snapshot without changing a C# source file");
        var changed = await scanner.AnalyzeAsync(new ScannerOptions(fixture.Path));

        Assert.NotEqual(original.Snapshot.SnapshotId, changed.Snapshot.SnapshotId);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ClassicWcfAnalyzer().AnalyzeAsync(fixture.Path, changed, context));
        Assert.Contains("snapshot ID", exception.Message, StringComparison.Ordinal);
    }
}
