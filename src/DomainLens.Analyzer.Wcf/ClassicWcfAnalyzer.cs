using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// Deterministically discovers the bounded classic-WCF implementation subset and projects its
/// observations into the canonical Evidence Graph. Repository content is read only through the
/// captured manifest and is never built, loaded, activated, or contacted.
/// </summary>
public sealed class ClassicWcfAnalyzer
{
    private readonly LegacySemanticCompilationService _compilationService;
    private readonly ManifestVerifiedFileReader _fileReader;

    public ClassicWcfAnalyzer()
        : this(new LegacySemanticCompilationService(), new ManifestVerifiedFileReader())
    {
    }

    internal ClassicWcfAnalyzer(
        LegacySemanticCompilationService compilationService,
        ManifestVerifiedFileReader fileReader)
    {
        _compilationService = compilationService ?? throw new ArgumentNullException(nameof(compilationService));
        _fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
    }

    /// <summary>
    /// Creates the shared controlled semantic context and enriches <paramref name="baseline"/>.
    /// Worker orchestration should prefer the overload accepting an existing context so Milestone
    /// 0 and Milestone 2 share one verified read/compilation.
    /// </summary>
    public async Task<AnalysisDocument> AnalyzeAsync(
        string workspaceRepositoryRoot,
        AnalysisDocument baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var context = await _compilationService
            .CreateAsync(workspaceRepositoryRoot, baseline, cancellationToken)
            .ConfigureAwait(false);
        return await AnalyzeAsync(
                workspaceRepositoryRoot,
                baseline,
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Enriches a structural graph using one already-created in-process Roslyn context. Roslyn
    /// objects remain implementation details and only normalized graph records are returned.
    /// </summary>
    public async Task<AnalysisDocument> AnalyzeAsync(
        string workspaceRepositoryRoot,
        AnalysisDocument baseline,
        LegacySemanticCompilationContext semanticContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRepositoryRoot);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(semanticContext);
        cancellationToken.ThrowIfCancellationRequested();

        AnalysisGraphValidator.ValidateAndThrow(baseline);
        if (!AnalysisJson.VerifyCanonicalHash(baseline))
        {
            throw new InvalidDataException("The baseline analysis canonical hash is invalid.");
        }

        ValidateContextBinding(baseline, semanticContext);
        if (baseline.Status == AnalysisStatus.Failure)
        {
            return baseline;
        }

        var builder = new WcfContributionBuilder(baseline);
        var source = new WcfSourceAnalyzer(builder, semanticContext, cancellationToken).Analyze();
        var configuration = await new WcfConfigurationAnalyzer(_fileReader)
            .AnalyzeAsync(workspaceRepositoryRoot, builder, cancellationToken)
            .ConfigureAwait(false);
        var svc = await new WcfSvcAnalyzer(_fileReader)
            .AnalyzeAsync(workspaceRepositoryRoot, builder, cancellationToken)
            .ConfigureAwait(false);

        WcfSourceRelationshipResolver.ResolveConfiguration(builder, source, configuration);
        WcfSourceRelationshipResolver.ResolveSvc(builder, source, svc);

        var enriched = AnalysisDocumentComposer.Compose(baseline, builder.Build());
        AnalysisGraphValidator.ValidateAndThrow(enriched);
        if (!AnalysisJson.VerifyCanonicalHash(enriched))
        {
            throw new InvalidDataException("The WCF-enriched analysis canonical hash is invalid.");
        }

        return enriched;
    }

    private static void ValidateContextBinding(
        AnalysisDocument baseline,
        LegacySemanticCompilationContext semanticContext)
    {
        if (!string.Equals(
                semanticContext.SnapshotId,
                baseline.Snapshot.SnapshotId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The semantic context is not bound to the supplied analysis snapshot ID.");
        }

        if (!string.Equals(
                semanticContext.ReferenceSetId,
                TrustedNet472ReferenceCatalog.ReferenceSetId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The semantic context does not use the trusted net472 reference set.");
        }

        var manifest = baseline.Snapshot.Manifest.ToDictionary(
            item => CanonicalIdentity.NormalizeRepositoryPath(item.Path),
            StringComparer.Ordinal);
        foreach (var source in semanticContext.SourceDocuments)
        {
            if (!manifest.TryGetValue(source.RelativePath, out var entry) ||
                !string.Equals(entry.ContentHash, source.ManifestEntry.ContentHash, StringComparison.OrdinalIgnoreCase) ||
                entry.Length != source.ManifestEntry.Length)
            {
                throw new InvalidDataException(
                    "The semantic context is not bound to the supplied analysis snapshot manifest.");
            }
        }
    }
}
