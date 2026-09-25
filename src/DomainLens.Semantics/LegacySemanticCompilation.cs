using System.Text;
using DomainLens.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using DiagnosticSeverity = DomainLens.Core.DiagnosticSeverity;

namespace DomainLens.Semantics;

/// <summary>
/// One manifest-verified C# source supplied to the controlled legacy compilation. This is an
/// in-process analyzer object and is never part of the worker protocol or a persisted result.
/// </summary>
public sealed record LegacySemanticSourceDocument(
    string RelativePath,
    ManifestEntry ManifestEntry,
    SourceText Text,
    SyntaxTree SyntaxTree);

/// <summary>
/// Read-only access to the controlled, tool-referenced Roslyn compilation used by trusted
/// in-process analyzers. Roslyn objects in this context must be projected to normalized evidence;
/// they must never cross the analyzer process protocol.
/// </summary>
public sealed class LegacySemanticCompilationContext
{
    private readonly IReadOnlyDictionary<string, LegacySemanticSourceDocument> _sourcesByPath;

    internal LegacySemanticCompilationContext(
        string snapshotId,
        CSharpCompilation compilation,
        IReadOnlyList<LegacySemanticSourceDocument> sourceDocuments,
        IReadOnlyList<SemanticAnalysisDiagnostic> diagnostics,
        IReadOnlyList<TrustedMetadataReference> metadataReferences)
    {
        SnapshotId = string.IsNullOrWhiteSpace(snapshotId)
            ? throw new ArgumentException("A semantic context snapshot ID is required.", nameof(snapshotId))
            : snapshotId;
        Compilation = compilation;
        SourceDocuments = sourceDocuments.ToArray();
        Diagnostics = diagnostics.ToArray();
        MetadataReferences = metadataReferences.ToArray();
        _sourcesByPath = SourceDocuments.ToDictionary(
            source => source.RelativePath,
            StringComparer.Ordinal);
    }

    public CSharpCompilation Compilation { get; }

    /// <summary>The captured repository snapshot used to create this controlled compilation.</summary>
    public string SnapshotId { get; }

    public IReadOnlyList<LegacySemanticSourceDocument> SourceDocuments { get; }

    public IReadOnlyList<SemanticAnalysisDiagnostic> Diagnostics { get; }

    public IReadOnlyList<TrustedMetadataReference> MetadataReferences { get; }

    public string ReferenceSetId => TrustedNet472ReferenceCatalog.ReferenceSetId;

    public SemanticCompilationScope CompilationScope =>
        SemanticCompilationScope.RepositoryManifestCSharpSources;

    public ResolutionQuality CompilationResolutionQuality => ResolutionQuality.Partial;

    /// <summary>
    /// True when both manifest-verified source and the complete pinned reference catalog were
    /// available. Syntax remains available through <see cref="SourceDocuments"/> when false.
    /// </summary>
    public bool CanResolveSemantics =>
        SourceDocuments.Count > 0 && MetadataReferences.Count > 0;

    public SemanticModel GetSemanticModel(LegacySemanticSourceDocument sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        return GetSemanticModel(sourceDocument.SyntaxTree);
    }

    public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);
        if (!Compilation.SyntaxTrees.Contains(syntaxTree))
        {
            throw new ArgumentException(
                "The syntax tree is not part of this controlled compilation.",
                nameof(syntaxTree));
        }

        return Compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: false);
    }

    public bool TryGetSourceDocument(
        string repositoryRelativePath,
        out LegacySemanticSourceDocument? sourceDocument)
    {
        sourceDocument = null;
        if (string.IsNullOrWhiteSpace(repositoryRelativePath))
        {
            return false;
        }

        try
        {
            var normalized = CanonicalIdentity.NormalizeRepositoryPath(repositoryRelativePath);
            return _sourcesByPath.TryGetValue(normalized, out sourceDocument);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static SourceSpan ToSourceSpan(SyntaxTree tree, TextSpan span)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var lineSpan = tree.GetLineSpan(span);
        return new SourceSpan(
            span.Start,
            span.Length,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            lineSpan.EndLinePosition.Line + 1,
            lineSpan.EndLinePosition.Character + 1);
    }
}

/// <summary>
/// Creates the single deterministic legacy semantic context shared by trusted analyzers. It does
/// not evaluate projects, restore/build, load repository binaries, or run analyzers/generators.
/// </summary>
public sealed class LegacySemanticCompilationService
{
    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ManifestVerifiedFileReader _fileReader = new();

    public async Task<LegacySemanticCompilationContext> CreateAsync(
        string workspaceRepositoryRoot,
        AnalysisDocument analysisDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysisDocument);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<SemanticAnalysisDiagnostic>();
        var metadataReferences = LoadTrustedReferences(diagnostics, cancellationToken);
        var sourceDocuments = new List<LegacySemanticSourceDocument>();

        if (!ManifestVerifiedFileReader.TryNormalizeRoot(
                workspaceRepositoryRoot,
                out var repositoryRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                SemanticDiagnosticCode.InvalidWorkspaceRoot,
                DiagnosticSeverity.Error,
                "The workspace repository root is missing or invalid."));
            return CreateContext(analysisDocument, sourceDocuments, diagnostics, metadataReferences);
        }

        await ReadManifestSourcesAsync(
                repositoryRoot,
                analysisDocument.Snapshot.Manifest,
                sourceDocuments,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        var context = CreateContext(
            analysisDocument,
            sourceDocuments,
            diagnostics,
            metadataReferences);
        if (context.CanResolveSemantics)
        {
            AddCompilerDiagnostics(context.Compilation, diagnostics, cancellationToken);
            context = new LegacySemanticCompilationContext(
                context.SnapshotId,
                context.Compilation,
                context.SourceDocuments,
                OrderDiagnostics(diagnostics),
                context.MetadataReferences);
        }

        return context;
    }

    private async Task ReadManifestSourcesAsync(
        string repositoryRoot,
        IReadOnlyList<ManifestEntry> manifest,
        ICollection<LegacySemanticSourceDocument> sourceDocuments,
        ICollection<SemanticAnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var entries = manifest
            .Where(entry => string.Equals(
                Path.GetExtension(entry.Path),
                ".cs",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in entries.GroupBy(
                     entry => entry.Path.Replace('\\', '/'),
                     FileSystemPathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() != 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    SemanticDiagnosticCode.DuplicateManifestPath,
                    DiagnosticSeverity.Error,
                    "A C# path appears more than once in the analysis manifest.",
                    group.Key));
                continue;
            }

            var entry = group.Single();
            var read = await _fileReader
                .ReadFromNormalizedRootAsync(repositoryRoot, entry, cancellationToken)
                .ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                diagnostics.Add(ToReadDiagnostic(read));
                continue;
            }

            try
            {
                var encoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);
                var source = encoding.GetString(read.Content.Span);
                var text = SourceText.From(source, Encoding.UTF8);
                var tree = CSharpSyntaxTree.ParseText(
                    text,
                    new CSharpParseOptions(LanguageVersion.CSharp7_3, DocumentationMode.Parse),
                    read.RelativePath,
                    cancellationToken: cancellationToken);
                sourceDocuments.Add(new LegacySemanticSourceDocument(
                    read.RelativePath,
                    entry with { Path = read.RelativePath },
                    text,
                    tree));

                foreach (var diagnostic in tree.GetDiagnostics(cancellationToken)
                             .Where(item => item.Severity ==
                                 Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                {
                    diagnostics.Add(FromCompilerDiagnostic(
                        SemanticDiagnosticCode.ParseError,
                        diagnostic,
                        read.RelativePath));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DecoderFallbackException or
                    ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(CreateDiagnostic(
                    SemanticDiagnosticCode.SourceReadFailed,
                    DiagnosticSeverity.Error,
                    "A manifest-listed C# file could not be read safely.",
                    read.RelativePath));
            }
        }
    }

    private static LegacySemanticCompilationContext CreateContext(
        AnalysisDocument analysisDocument,
        IEnumerable<LegacySemanticSourceDocument> sourceDocuments,
        IEnumerable<SemanticAnalysisDiagnostic> diagnostics,
        IEnumerable<TrustedReferenceCatalogEntry> metadataReferences)
    {
        var orderedSources = sourceDocuments
            .OrderBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var orderedReferences = metadataReferences
            .OrderBy(reference => reference.Descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var compilation = CSharpCompilation.Create(
            $"DomainLens.Legacy.{analysisDocument.Snapshot.SnapshotId.Replace(':', '.')}",
            orderedSources.Select(source => source.SyntaxTree),
            orderedReferences.Select(reference => MetadataReference.CreateFromFile(reference.FullPath)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                allowUnsafe: false));
        return new LegacySemanticCompilationContext(
            analysisDocument.Snapshot.SnapshotId,
            compilation,
            orderedSources,
            OrderDiagnostics(diagnostics),
            orderedReferences.Select(reference => reference.Descriptor).ToArray());
    }

    private static IReadOnlyList<TrustedReferenceCatalogEntry> LoadTrustedReferences(
        ICollection<SemanticAnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = TrustedNet472ReferenceCatalog.GetSnapshot();
        if (!catalog.DirectoryExists)
        {
            diagnostics.Add(CreateDiagnostic(
                SemanticDiagnosticCode.TrustedReferenceDirectoryMissing,
                DiagnosticSeverity.Error,
                "The tool-owned .NET Framework 4.7.2 reference directory is unavailable."));
            return Array.Empty<TrustedReferenceCatalogEntry>();
        }

        for (var failure = 0; failure < catalog.FailureCount; failure++)
        {
            diagnostics.Add(CreateDiagnostic(
                SemanticDiagnosticCode.TrustedReferenceLoadFailed,
                DiagnosticSeverity.Error,
                "The tool-owned .NET Framework reference catalog failed its pinned content or metadata validation."));
        }

        if (catalog.FailureCount > 0)
        {
            return Array.Empty<TrustedReferenceCatalogEntry>();
        }

        if (catalog.Entries.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                SemanticDiagnosticCode.TrustedReferenceDirectoryMissing,
                DiagnosticSeverity.Error,
                "The tool-owned .NET Framework 4.7.2 reference directory contains no usable assemblies."));
        }

        return catalog.Entries;
    }

    private static void AddCompilerDiagnostics(
        CSharpCompilation compilation,
        ICollection<SemanticAnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken)
                     .Where(item => item.Severity is
                         Microsoft.CodeAnalysis.DiagnosticSeverity.Error or
                         Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                     .OrderBy(item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                     .ThenBy(item => item.Location.SourceSpan.Start)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var code = diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                ? SemanticDiagnosticCode.CompilationError
                : SemanticDiagnosticCode.CompilationWarning;
            diagnostics.Add(FromCompilerDiagnostic(code, diagnostic));
        }
    }

    private static SemanticAnalysisDiagnostic FromCompilerDiagnostic(
        SemanticDiagnosticCode code,
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        string? fallbackPath = null)
    {
        var isSource = diagnostic.Location.IsInSource && diagnostic.Location.SourceTree is not null;
        return new SemanticAnalysisDiagnostic(
            code,
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error,
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            isSource ? diagnostic.Location.SourceTree!.FilePath.Replace('\\', '/') : fallbackPath,
            isSource
                ? LegacySemanticCompilationContext.ToSourceSpan(
                    diagnostic.Location.SourceTree!,
                    diagnostic.Location.SourceSpan)
                : null,
            diagnostic.Id);
    }

    private static SemanticAnalysisDiagnostic ToReadDiagnostic(
        ManifestVerifiedFileReadResult read) =>
        read.Status switch
        {
            ManifestVerifiedFileReadStatus.UnsafeManifestPath => CreateDiagnostic(
                SemanticDiagnosticCode.UnsafeManifestPath,
                DiagnosticSeverity.Error,
                "A manifest C# path is not a safe file within the workspace repository root.",
                read.RelativePath),
            ManifestVerifiedFileReadStatus.FileMissing => CreateDiagnostic(
                SemanticDiagnosticCode.ManifestFileMissing,
                DiagnosticSeverity.Error,
                "A manifest-listed C# file no longer exists in the workspace snapshot.",
                read.RelativePath),
            ManifestVerifiedFileReadStatus.LengthMismatch => CreateDiagnostic(
                SemanticDiagnosticCode.ManifestLengthMismatch,
                DiagnosticSeverity.Error,
                "A manifest-listed C# file length changed after repository capture.",
                read.RelativePath),
            ManifestVerifiedFileReadStatus.HashMismatch => CreateDiagnostic(
                SemanticDiagnosticCode.ManifestHashMismatch,
                DiagnosticSeverity.Error,
                "A manifest-listed C# file hash changed after repository capture.",
                read.RelativePath),
            ManifestVerifiedFileReadStatus.InvalidWorkspaceRoot => CreateDiagnostic(
                SemanticDiagnosticCode.InvalidWorkspaceRoot,
                DiagnosticSeverity.Error,
                "The workspace repository root is missing or invalid."),
            _ => CreateDiagnostic(
                SemanticDiagnosticCode.SourceReadFailed,
                DiagnosticSeverity.Error,
                "A manifest-listed C# file could not be read safely.",
                read.RelativePath)
        };

    private static IReadOnlyList<SemanticAnalysisDiagnostic> OrderDiagnostics(
        IEnumerable<SemanticAnalysisDiagnostic> diagnostics) =>
        diagnostics
            .Distinct()
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.Span?.StartOffset ?? -1)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.CompilerDiagnosticId, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToArray();

    private static SemanticAnalysisDiagnostic CreateDiagnostic(
        SemanticDiagnosticCode code,
        DiagnosticSeverity severity,
        string message,
        string? relativePath = null) =>
        new(code, severity, message, relativePath, null);
}
