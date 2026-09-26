using DomainLens.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DomainLens.Semantics;

/// <summary>
/// Performs compiler-backed enrichment of manifest-captured legacy C# without loading a
/// repository project, build, analyzer, generator, or binary.
/// </summary>
public sealed class LegacySemanticAnalyzer
{
    private static readonly SymbolDisplayFormat SymbolFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly LegacySemanticCompilationService _compilationService;

    public LegacySemanticAnalyzer()
        : this(new LegacySemanticCompilationService())
    {
    }

    internal LegacySemanticAnalyzer(LegacySemanticCompilationService compilationService)
    {
        _compilationService = compilationService ??
            throw new ArgumentNullException(nameof(compilationService));
    }

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

        var context = await _compilationService
            .CreateAsync(workspaceRepositoryRoot, analysisDocument, cancellationToken)
            .ConfigureAwait(false);
        return await AnalyzeAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Projects the existing controlled compilation into the legacy normalized semantic result.
    /// This overload lets multiple trusted in-process analyzers share one verified compilation.
    /// </summary>
    public async Task<LegacySemanticAnalysisResult> AnalyzeAsync(
        LegacySemanticCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var observations = new List<SemanticBindingObservation>();
        if (!context.CanResolveSemantics)
        {
            return CreateResult(observations, context.Diagnostics, context.MetadataReferences);
        }

        foreach (var source in context.SourceDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = source.SyntaxTree;
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var model = context.GetSemanticModel(source);

            CollectDeclaredTypes(root, model, observations, cancellationToken);
            CollectBaseAndInterfaceBindings(root, model, observations, cancellationToken);
            CollectAttributeBindings(root, model, observations, cancellationToken);
            CollectInvocationBindings(root, model, observations, cancellationToken);
            CollectMemberBindings(root, model, observations, cancellationToken);
        }

        return CreateResult(observations, context.Diagnostics, context.MetadataReferences);
    }

    internal static async Task<ManifestSourceReadResult> ReadBoundedSourceAsync(
        Stream source,
        ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        var result = await ManifestVerifiedFileReader
            .ReadBoundedAsync(source, entry, cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            ManifestVerifiedFileReadStatus.Success =>
                new ManifestSourceReadResult(ManifestSourceReadStatus.Success, result.Bytes),
            ManifestVerifiedFileReadStatus.HashMismatch => ManifestSourceReadResult.HashMismatch,
            _ => ManifestSourceReadResult.LengthMismatch
        };
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

    private static LegacySemanticAnalysisResult CreateResult(
        IEnumerable<SemanticBindingObservation> observations,
        IEnumerable<SemanticAnalysisDiagnostic> diagnostics,
        IEnumerable<TrustedMetadataReference> metadataReferences) =>
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
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            TrustedNet472ReferenceCatalog.ReferenceSetId)
        {
            CompilationScope = SemanticCompilationScope.RepositoryManifestCSharpSources,
            CompilationResolutionQuality = ResolutionQuality.Partial
        };

    private static SourceSpan ToSourceSpan(SyntaxTree tree, Microsoft.CodeAnalysis.Text.TextSpan span)
        => LegacySemanticCompilationContext.ToSourceSpan(tree, span);

    private static string DisplaySymbol(ISymbol symbol) => symbol.ToDisplayString(SymbolFormat);
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
