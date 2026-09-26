using DomainLens.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainLens.Analyzer.Wcf;

internal sealed class WcfSourceNodeIndex
{
    private readonly IReadOnlyDictionary<SourceNodeKey, IReadOnlyList<EvidenceNode>> _nodes;

    public WcfSourceNodeIndex(AnalysisDocument document)
    {
        var evidenceById = document.Evidence.ToDictionary(item => item.EvidenceId, StringComparer.Ordinal);
        var nodes = new Dictionary<SourceNodeKey, List<EvidenceNode>>();
        foreach (var node in document.Nodes)
        {
            foreach (var evidenceId in node.EvidenceIds)
            {
                if (!evidenceById.TryGetValue(evidenceId, out var evidence) ||
                    !IsDeclarationRule(evidence.Provenance.RuleId))
                {
                    continue;
                }

                var key = new SourceNodeKey(
                    evidence.RelativePath,
                    evidence.Span.StartOffset,
                    evidence.Span.Length,
                    evidence.Provenance.RuleId);
                if (!nodes.TryGetValue(key, out var matches))
                {
                    matches = [];
                    nodes.Add(key, matches);
                }

                if (!matches.Any(match => string.Equals(match.NodeId, node.NodeId, StringComparison.Ordinal)))
                {
                    matches.Add(node);
                }
            }
        }

        _nodes = nodes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EvidenceNode>)pair.Value
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)
                .ToArray());
    }

    public bool TryGetTypeNode(INamedTypeSymbol symbol, out EvidenceNode node) =>
        TryGetDeclarationNode(symbol, "csharp.type-declaration", out node);

    public bool TryGetMethodNode(IMethodSymbol symbol, out EvidenceNode node) =>
        TryGetDeclarationNode(symbol, "csharp.method-declaration", out node);

    public bool TryGetMemberNode(ISymbol symbol, out EvidenceNode node)
    {
        var rule = symbol switch
        {
            IPropertySymbol => "csharp.property-declaration",
            IFieldSymbol => "csharp.field-declaration",
            IEventSymbol => "csharp.event-declaration",
            IMethodSymbol => "csharp.method-declaration",
            _ => string.Empty,
        };
        if (rule.Length != 0)
        {
            return TryGetDeclarationNode(symbol, rule, out node);
        }

        node = null!;
        return false;
    }

    public bool TryGetDeclarationNode(ISymbol symbol, string ruleId, out EvidenceNode node)
    {
        var matches = new Dictionary<string, EvidenceNode>(StringComparer.Ordinal);
        foreach (var reference in symbol.DeclaringSyntaxReferences
                     .OrderBy(item => item.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(item => item.Span.Start))
        {
            var path = reference.SyntaxTree.FilePath.Replace('\\', '/');
            var span = reference.Span;
            AddMatches(new SourceNodeKey(path, span.Start, span.Length, ruleId), matches);

            // Scanner field/event nodes are created from each VariableDeclarator, while Roslyn
            // source symbols can occasionally report the containing declaration.
            var syntax = reference.GetSyntax();
            if (syntax is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
            {
                foreach (var variable in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    AddMatches(
                        new SourceNodeKey(path, variable.Span.Start, variable.Span.Length, ruleId),
                        matches);
                }
            }
        }

        if (matches.Count == 1)
        {
            node = matches.Values.Single();
            return true;
        }

        node = null!;
        return false;
    }

    public IReadOnlyList<EvidenceNode> GetDeclarationNodes(SyntaxNode syntax, string ruleId)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var path = syntax.SyntaxTree.FilePath.Replace('\\', '/');
        return _nodes.TryGetValue(
                new SourceNodeKey(path, syntax.SpanStart, syntax.Span.Length, ruleId),
                out var matches)
            ? matches
            : [];
    }

    private void AddMatches(
        SourceNodeKey key,
        IDictionary<string, EvidenceNode> matches)
    {
        if (!_nodes.TryGetValue(key, out var candidates))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            matches[candidate.NodeId] = candidate;
        }
    }

    private static bool IsDeclarationRule(string ruleId) =>
        ruleId.StartsWith("csharp.", StringComparison.Ordinal) &&
        ruleId.EndsWith("-declaration", StringComparison.Ordinal);

    private sealed record SourceNodeKey(
        string RelativePath,
        int StartOffset,
        int Length,
        string RuleId);
}
