using System.Security.Cryptography;
using System.Text;
using DomainLens.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using DiagnosticSeverity = DomainLens.Core.DiagnosticSeverity;

namespace DomainLens.Semantics;

/// <summary>
/// Performs compiler-backed enrichment of manifest-captured legacy C# without loading a
/// repository project, build, analyzer, generator, or binary.
/// </summary>
public sealed class LegacySemanticAnalyzer
{
    private const int SourceReadChunkSize = 64 * 1024;

    private static readonly SymbolDisplayFormat SymbolFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Analyzes only C# files named by <paramref name="analysisDocument"/>'s manifest after
    /// revalidating every selected file against its recorded length and SHA-256 digest.
    /// </summary>
    public async Task<LegacySemanticAnalysisResult> AnalyzeAsync(
        string workspaceRepositoryRoot,
        AnalysisDocument analysisDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysisDocument);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<SemanticAnalysisDiagnostic>();
        var observations = new List<SemanticBindingObservation>();
        var metadataReferences = LoadTrustedReferences(diagnostics, cancellationToken);

        if (!TryNormalizeRoot(workspaceRepositoryRoot, out var repositoryRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                SemanticDiagnosticCode.InvalidWorkspaceRoot,
                DiagnosticSeverity.Error,
                "The workspace repository root is missing or invalid."));
            return CreateResult(observations, diagnostics, metadataReferences);
        }

        var syntaxTrees = await ReadManifestSourcesAsync(
                repositoryRoot,
                analysisDocument.Snapshot.Manifest,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        if (syntaxTrees.Count == 0 || metadataReferences.Count == 0)
        {
            return CreateResult(observations, diagnostics, metadataReferences);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var compilation = CSharpCompilation.Create(
            $"DomainLens.Legacy.{analysisDocument.Snapshot.SnapshotId.Replace(':', '.')}",
            syntaxTrees,
            metadataReferences.Select(reference => MetadataReference.CreateFromFile(reference.FullPath)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                allowUnsafe: false));

        foreach (var tree in syntaxTrees.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: false);

            CollectDeclaredTypes(root, model, observations, cancellationToken);
            CollectBaseAndInterfaceBindings(root, model, observations, cancellationToken);
            CollectAttributeBindings(root, model, observations, cancellationToken);
            CollectInvocationBindings(root, model, observations, cancellationToken);
            CollectMemberBindings(root, model, observations, cancellationToken);
        }

        AddCompilerDiagnostics(compilation, diagnostics, cancellationToken);
        return CreateResult(observations, diagnostics, metadataReferences);
    }

    private static async Task<IReadOnlyList<SyntaxTree>> ReadManifestSourcesAsync(
        string repositoryRoot,
        IReadOnlyList<ManifestEntry> manifest,
        List<SemanticAnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var syntaxTrees = new List<SyntaxTree>();
        var entries = manifest
            .Where(entry => string.Equals(Path.GetExtension(entry.Path), ".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in entries.GroupBy(entry => entry.Path.Replace('\\', '/'), FileSystemPathComparer))
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
            if (!TryResolveManifestPath(repositoryRoot, entry.Path, out var fullPath, out var relativePath) ||
                ContainsReparsePoint(repositoryRoot, fullPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    SemanticDiagnosticCode.UnsafeManifestPath,
                    DiagnosticSeverity.Error,
                    "A manifest C# path is not a safe file within the workspace repository root.",
                    entry.Path.Replace('\\', '/')));
                continue;
            }

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    SemanticDiagnosticCode.ManifestFileMissing,
                    DiagnosticSeverity.Error,
                    "A manifest-listed C# file no longer exists in the workspace snapshot.",
                    relativePath));
                continue;
            }

            try
            {
                await using var sourceStream = new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Read,
                        Mode = FileMode.Open,
                        Share = FileShare.Read,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                        // Disable FileStream read-ahead; the bounded reader owns the only
                        // source-content buffer and controls every requested byte count.
                        BufferSize = 1
                    });
                var readResult = await ReadBoundedSourceAsync(sourceStream, entry, cancellationToken)
                    .ConfigureAwait(false);
                if (readResult.Status == ManifestSourceReadStatus.LengthMismatch)
                {
                    diagnostics.Add(CreateDiagnostic(
                        SemanticDiagnosticCode.ManifestLengthMismatch,
                        DiagnosticSeverity.Error,
                        "A manifest-listed C# file length changed after repository capture.",
                        relativePath));
                    continue;
                }

                if (readResult.Status == ManifestSourceReadStatus.HashMismatch)
                {
                    diagnostics.Add(CreateDiagnostic(
                        SemanticDiagnosticCode.ManifestHashMismatch,
                        DiagnosticSeverity.Error,
                        "A manifest-listed C# file hash changed after repository capture.",
                        relativePath));
                    continue;
                }

                var bytes = readResult.Bytes!;
                var source = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes);
                var tree = CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.CSharp7_3, DocumentationMode.Parse),
                    relativePath,
                    Encoding.UTF8,
                    cancellationToken);
                syntaxTrees.Add(tree);

                foreach (var diagnostic in tree.GetDiagnostics(cancellationToken)
                             .Where(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                {
                    diagnostics.Add(FromCompilerDiagnostic(
                        SemanticDiagnosticCode.ParseError,
                        diagnostic,
                        relativePath));
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
                    relativePath));
            }
        }

        return syntaxTrees;
    }

    internal static async Task<ManifestSourceReadResult> ReadBoundedSourceAsync(
        Stream source,
        ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (entry.Length < 0 || entry.Length > Array.MaxLength)
        {
            return ManifestSourceReadResult.LengthMismatch;
        }

        if (!source.CanSeek)
        {
            throw new NotSupportedException("Manifest source verification requires a seekable stream.");
        }

        var initialPosition = source.Position;
        var expectedEndPosition = checked(initialPosition + entry.Length);
        if (source.Length != expectedEndPosition)
        {
            return ManifestSourceReadResult.LengthMismatch;
        }

        var expectedLength = checked((int)entry.Length);
        var bytes = GC.AllocateUninitializedArray<byte>(expectedLength);
        var totalRead = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        while (totalRead < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedCount = Math.Min(SourceReadChunkSize, expectedLength - totalRead);
            var count = await source
                .ReadAsync(bytes.AsMemory(totalRead, requestedCount), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return ManifestSourceReadResult.LengthMismatch;
            }

            hash.AppendData(bytes.AsSpan(totalRead, count));
            totalRead += count;
        }

        // Recheck the open handle after the exact bounded read. This detects growth during
        // the read without requesting or consuming any byte past the captured manifest length.
        if (source.Position != expectedEndPosition || source.Length != expectedEndPosition)
        {
            return ManifestSourceReadResult.LengthMismatch;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var contentHash = Convert.ToHexString(hash.GetHashAndReset());
        return string.Equals(contentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase)
            ? new ManifestSourceReadResult(ManifestSourceReadStatus.Success, bytes)
            : ManifestSourceReadResult.HashMismatch;
    }

    private static IReadOnlyList<TrustedReferenceCatalogEntry> LoadTrustedReferences(
        List<SemanticAnalysisDiagnostic> diagnostics,
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

    private static void CollectDeclaredTypes(
        SyntaxNode root,
        SemanticModel model,
        List<SemanticBindingObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
            observations.Add(CreateObservation(
                SemanticObservationKind.DeclaredType,
                declaration,
                model,
                symbol,
                default,
                symbol is null ? "The compiler could not create a symbol for this declaration." : null));
        }
    }

    private static void CollectBaseAndInterfaceBindings(
        SyntaxNode root,
        SemanticModel model,
        List<SemanticBindingObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (declaration.BaseList is null)
            {
                continue;
            }

            var baseTypes = declaration.BaseList.Types;
            for (var index = 0; index < baseTypes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = baseTypes[index].Type;
                var symbolInfo = model.GetSymbolInfo(type, cancellationToken);
                var target = symbolInfo.Symbol as ITypeSymbol;
                SemanticObservationKind? kind = target?.TypeKind switch
                {
                    TypeKind.Interface => SemanticObservationKind.ImplementedInterface,
                    TypeKind.Class => SemanticObservationKind.BaseType,
                    _ when declaration.Kind() is SyntaxKind.InterfaceDeclaration or
                        SyntaxKind.StructDeclaration or SyntaxKind.RecordStructDeclaration || index > 0 =>
                        SemanticObservationKind.ImplementedInterface,
                    // An unresolved first class/record base-list item may be either a base class or
                    // an interface. Preserve the compiler diagnostic instead of asserting either.
                    _ => null
                };
                if (kind is null)
                {
                    continue;
                }

                observations.Add(CreateObservation(kind.Value, type, model, target, symbolInfo));
            }
        }
    }

    private static void CollectAttributeBindings(
        SyntaxNode root,
        SemanticModel model,
        List<SemanticBindingObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbolInfo = model.GetSymbolInfo(attribute, cancellationToken);
            var target = symbolInfo.Symbol switch
            {
                IMethodSymbol constructor => constructor.ContainingType,
                ITypeSymbol type => type,
                _ => null
            };
            observations.Add(CreateObservation(
                SemanticObservationKind.AttributeType,
                attribute,
                model,
                target,
                symbolInfo));
        }
    }

    private static void CollectInvocationBindings(
        SyntaxNode root,
        SemanticModel model,
        List<SemanticBindingObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(invocation, cancellationToken) is INameOfOperation)
            {
                continue;
            }

            var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
            observations.Add(CreateObservation(
                SemanticObservationKind.Invocation,
                invocation,
                model,
                symbolInfo.Symbol,
                symbolInfo));
        }
    }

    private static void CollectMemberBindings(
        SyntaxNode root,
        SemanticModel model,
        List<SemanticBindingObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbolInfo = model.GetSymbolInfo(member, cancellationToken);
            observations.Add(CreateObservation(
                SemanticObservationKind.MemberAccess,
                member,
                model,
                symbolInfo.Symbol,
                symbolInfo));
        }
    }

    private static SemanticBindingObservation CreateObservation(
        SemanticObservationKind kind,
        SyntaxNode syntax,
        SemanticModel model,
        ISymbol? symbol,
        SymbolInfo symbolInfo = default,
        string? details = null)
    {
        if (symbol is IErrorTypeSymbol)
        {
            symbol = null;
        }

        var candidates = symbolInfo.CandidateSymbols
            .Where(candidate => candidate is not IErrorTypeSymbol)
            .Select(DisplaySymbol)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var isSourceSymbol = symbol is not null &&
                             symbol.Locations.Any(location => location.IsInSource);
        var quality = symbol is not null
            ? isSourceSymbol ? ResolutionQuality.Partial : ResolutionQuality.Exact
            : candidates.Length switch
            {
                > 1 => ResolutionQuality.Ambiguous,
                1 => ResolutionQuality.Partial,
                _ => ResolutionQuality.Unresolved
            };
        var resolvedAssembly = symbol?.ContainingAssembly?.Identity.Name;
        var containingSymbol = model.GetEnclosingSymbol(syntax.SpanStart)?.ToDisplayString(SymbolFormat);

        if (details is null && isSourceSymbol)
        {
            details =
                "The symbol bound within the repository-wide synthetic compilation; effective project and dependency selection were not evaluated.";
        }
        else if (details is null && symbol is null)
        {
            details = symbolInfo.CandidateReason == CandidateReason.Ambiguous
                ? "The compiler found multiple candidate symbols."
                : candidates.Length == 1
                    ? "The compiler found one candidate but could not bind it exactly."
                    : "The compiler could not bind this source expression.";
        }

        return new SemanticBindingObservation(
            kind,
            syntax.SyntaxTree.FilePath.Replace('\\', '/'),
            ToSourceSpan(syntax.SyntaxTree, syntax.Span),
            syntax.ToString(),
            containingSymbol,
            symbol is null ? null : DisplaySymbol(symbol),
            resolvedAssembly,
            quality,
            candidates,
            details);
    }

    private static void AddCompilerDiagnostics(
        CSharpCompilation compilation,
        List<SemanticAnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken)
                     .Where(item => item.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error or
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
            isSource ? ToSourceSpan(diagnostic.Location.SourceTree!, diagnostic.Location.SourceSpan) : null,
            diagnostic.Id);
    }

    private static LegacySemanticAnalysisResult CreateResult(
        IEnumerable<SemanticBindingObservation> observations,
        IEnumerable<SemanticAnalysisDiagnostic> diagnostics,
        IEnumerable<TrustedReferenceCatalogEntry> metadataReferences) =>
        new(
            observations
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ThenBy(item => item.Span.StartOffset)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.ResolvedSymbol, StringComparer.Ordinal)
                .ToArray(),
            diagnostics
                .Distinct()
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ThenBy(item => item.Span?.StartOffset ?? -1)
                .ThenBy(item => item.Code)
                .ThenBy(item => item.CompilerDiagnosticId, StringComparer.Ordinal)
                .ToArray(),
            metadataReferences
                .Select(item => item.Descriptor)
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            TrustedNet472ReferenceCatalog.ReferenceSetId)
        {
            CompilationScope = SemanticCompilationScope.RepositoryManifestCSharpSources,
            CompilationResolutionQuality = ResolutionQuality.Partial
        };

    private static bool TryNormalizeRoot(string root, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return Directory.Exists(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static bool TryResolveManifestPath(
        string repositoryRoot,
        string candidate,
        out string fullPath,
        out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = candidate.Replace('\\', '/');
        try
        {
            var normalized = CanonicalIdentity.NormalizeRepositoryPath(candidate);
            var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinRoot(repositoryRoot, resolved))
            {
                return false;
            }

            fullPath = resolved;
            relativePath = normalized;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static bool ContainsReparsePoint(string repositoryRoot, string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(repositoryRoot, fullPath);
            var current = repositoryRoot;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static SourceSpan ToSourceSpan(SyntaxTree tree, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new SourceSpan(
            span.Start,
            span.Length,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            lineSpan.EndLinePosition.Line + 1,
            lineSpan.EndLinePosition.Character + 1);
    }

    private static string DisplaySymbol(ISymbol symbol) => symbol.ToDisplayString(SymbolFormat);

    private static SemanticAnalysisDiagnostic CreateDiagnostic(
        SemanticDiagnosticCode code,
        DiagnosticSeverity severity,
        string message,
        string? relativePath = null) =>
        new(code, severity, message, relativePath, null);

    private static StringComparer FileSystemPathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal enum ManifestSourceReadStatus
{
    Success,
    LengthMismatch,
    HashMismatch
}

internal sealed record ManifestSourceReadResult(
    ManifestSourceReadStatus Status,
    byte[]? Bytes)
{
    public static ManifestSourceReadResult LengthMismatch { get; } =
        new(ManifestSourceReadStatus.LengthMismatch, null);

    public static ManifestSourceReadResult HashMismatch { get; } =
        new(ManifestSourceReadStatus.HashMismatch, null);
}
