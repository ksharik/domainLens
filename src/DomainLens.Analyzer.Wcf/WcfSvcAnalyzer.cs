using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// Parses ASP.NET ServiceHost directives from captured <c>.svc</c> files as inert text. It does
/// not invoke ASP.NET compilation, load factories, or execute service-hosting logic.
/// </summary>
internal sealed class WcfSvcAnalyzer(ManifestVerifiedFileReader fileReader)
{
    private static readonly HashSet<string> SupportedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CodeBehind",
        "Factory",
        "Language",
        "Service",
    };

    public async Task<WcfSvcAnalysisResult> AnalyzeAsync(
        string workspaceRepositoryRoot,
        WcfContributionBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var declarations = new List<WcfSvcHostObservation>();

        foreach (var entry in builder.Baseline.Snapshot.Manifest
                     .Where(item => string.Equals(
                         Path.GetExtension(item.Path),
                         ".svc",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await fileReader.ReadAsync(workspaceRepositoryRoot, entry, cancellationToken)
                .ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.ManifestReadFailed,
                    DiagnosticSeverity.Warning,
                    $"The .svc file could not be accepted by the manifest-verified reader ({read.Status}).",
                    entry.Path,
                    properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["readStatus"] = read.Status.ToString(),
                    });
                continue;
            }

            var source = WcfTextDocument.Decode(read.Content);
            var parsed = ParseDirectives(source.Text);
            var foundServiceHost = false;
            foreach (var directive in parsed.Directives)
            {
                if (!string.Equals(directive.Name, "ServiceHost", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AddDiagnostic(
                        WcfVocabulary.Diagnostics.UnsupportedSvc,
                        DiagnosticSeverity.Warning,
                        $"The .svc directive '{directive.Name}' is outside the allowlisted ServiceHost subset.",
                        entry.Path);
                    continue;
                }

                foundServiceHost = true;
                var problems = new List<string>();
                var unsupportedNames = directive.Attributes.Keys
                    .Where(name => !SupportedAttributes.Contains(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (unsupportedNames.Length != 0)
                {
                    problems.Add($"unsupported attributes: {string.Join(", ", unsupportedNames)}");
                }

                if (directive.HasDynamicValue)
                {
                    problems.Add("dynamic attribute expression");
                }

                var service = StaticLiteral(directive, "Service");
                var factory = StaticLiteral(directive, "Factory");
                var codeBehind = StaticLiteral(directive, "CodeBehind");
                var language = StaticLiteral(directive, "Language");
                if (service is null)
                {
                    problems.Add("missing or non-literal Service attribute");
                }

                var quality = problems.Count == 0
                    ? ResolutionQuality.Exact
                    : ResolutionQuality.Partial;
                var span = source.Span(directive.StartOffset, directive.Length);
                var evidenceId = builder.AddEvidence(
                    entry.Path,
                    span,
                    WcfVocabulary.Rules.SvcDirective,
                    ResolutionBasis.DeclarativeConfiguration,
                    quality,
                    problems.Count == 0
                        ? "A static ServiceHost directive was parsed as inert text."
                        : $"The ServiceHost directive was only partially interpreted: {string.Join("; ", problems)}.");
                var displayName = service ?? "(unresolved ServiceHost directive)";
                var node = builder.AddNode(
                    WcfVocabulary.NodeKinds.HostingDeclaration,
                    displayName,
                    QualifiedIdentity(entry.Path, displayName, directive.StartOffset),
                    projectId: null,
                    evidenceIds: [evidenceId],
                    properties: Properties(
                        (WcfVocabulary.Properties.Service, service),
                        (WcfVocabulary.Properties.Factory, factory),
                        (WcfVocabulary.Properties.CodeBehind, codeBehind),
                        (WcfVocabulary.Properties.Language, language)));
                declarations.Add(new WcfSvcHostObservation(
                    entry.Path,
                    directive.StartOffset,
                    node,
                    evidenceId,
                    service,
                    factory,
                    codeBehind,
                    language));

                if (problems.Count != 0)
                {
                    builder.AddDiagnostic(
                        WcfVocabulary.Diagnostics.UnsupportedSvc,
                        DiagnosticSeverity.Warning,
                        $"The ServiceHost directive was only partially interpreted: {string.Join("; ", problems)}.",
                        entry.Path,
                        [evidenceId]);
                }
            }

            foreach (var error in parsed.Errors.OrderBy(item => item.Offset))
            {
                builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.MalformedSvc,
                    DiagnosticSeverity.Warning,
                    error.Message,
                    entry.Path,
                    properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["offset"] = error.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
            }

            if (!foundServiceHost && parsed.Errors.Count == 0)
            {
                builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.UnsupportedSvc,
                    DiagnosticSeverity.Warning,
                    "The .svc file contains no statically recognizable ServiceHost directive.",
                    entry.Path);
            }

            if (declarations.Count(item =>
                    string.Equals(item.RelativePath, entry.Path, StringComparison.Ordinal)) > 1)
            {
                builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.UnsupportedSvc,
                    DiagnosticSeverity.Warning,
                    "The .svc file contains multiple ServiceHost directives; each declaration was retained without selecting an effective host.",
                    entry.Path,
                    declarations
                        .Where(item => string.Equals(item.RelativePath, entry.Path, StringComparison.Ordinal))
                        .Select(item => item.EvidenceId));
            }
        }

        return new WcfSvcAnalysisResult(declarations);
    }

    private static ParsedSvcFile ParseDirectives(string text)
    {
        var directives = new List<ParsedDirective>();
        var errors = new List<SvcParseError>();
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var start = text.IndexOf("<%@", searchStart, StringComparison.Ordinal);
            var commentStart = text.IndexOf("<%--", searchStart, StringComparison.Ordinal);
            if (commentStart >= 0 && (start < 0 || commentStart < start))
            {
                var commentEnd = text.IndexOf("--%>", commentStart + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    // An unterminated ASP.NET server comment consumes the remaining inert text.
                    // Do not promote directive-looking content from inside it.
                    break;
                }

                searchStart = commentEnd + 4;
                continue;
            }

            if (start < 0)
            {
                break;
            }

            var end = FindDirectiveEnd(text, start + 3);
            if (end < 0)
            {
                errors.Add(new SvcParseError(
                    start,
                    "A .svc directive is not terminated with '%>' and was not interpreted."));
                break;
            }

            if (TryParseDirective(text, start, end + 2, out var directive, out var message))
            {
                directives.Add(directive!);
            }
            else
            {
                errors.Add(new SvcParseError(start, message!));
            }

            searchStart = end + 2;
        }

        return new ParsedSvcFile(directives, errors);
    }

    private static bool TryParseDirective(
        string text,
        int start,
        int endExclusive,
        out ParsedDirective? directive,
        out string? error)
    {
        directive = null;
        error = null;
        var position = start + 3;
        SkipWhitespace(text, ref position, endExclusive - 2);
        var name = ReadName(text, ref position, endExclusive - 2);
        if (name.Length == 0)
        {
            error = "A .svc directive has no directive name and was not interpreted.";
            return false;
        }

        var attributes = new Dictionary<string, DirectiveAttribute>(StringComparer.OrdinalIgnoreCase);
        var hasDynamicValue = false;
        while (position < endExclusive - 2)
        {
            SkipWhitespace(text, ref position, endExclusive - 2);
            if (position >= endExclusive - 2)
            {
                break;
            }

            var attributeStart = position;
            var attributeName = ReadName(text, ref position, endExclusive - 2);
            if (attributeName.Length == 0)
            {
                error = $"The {name} directive contains malformed attribute syntax and was not interpreted.";
                return false;
            }

            SkipWhitespace(text, ref position, endExclusive - 2);
            if (position >= endExclusive - 2 || text[position] != '=')
            {
                error = $"The {name} directive attribute '{attributeName}' has no '=' and was not interpreted.";
                return false;
            }

            position++;
            SkipWhitespace(text, ref position, endExclusive - 2);
            if (position >= endExclusive - 2 || text[position] is not ('\'' or '"'))
            {
                error = $"The {name} directive attribute '{attributeName}' has no quoted literal and was not interpreted.";
                return false;
            }

            var quote = text[position++];
            var valueStart = position;
            while (position < endExclusive - 2 && text[position] != quote)
            {
                position++;
            }

            if (position >= endExclusive - 2)
            {
                error = $"The {name} directive attribute '{attributeName}' has an unterminated quoted value and was not interpreted.";
                return false;
            }

            var value = text[valueStart..position];
            position++;
            if (!attributes.TryAdd(
                    attributeName,
                    new DirectiveAttribute(attributeName, value, attributeStart, position - attributeStart)))
            {
                error = $"The {name} directive repeats attribute '{attributeName}' and was not interpreted.";
                return false;
            }

            if (value.Contains("<%", StringComparison.Ordinal) ||
                value.Contains("%>", StringComparison.Ordinal))
            {
                hasDynamicValue = true;
            }
        }

        directive = new ParsedDirective(
            name,
            start,
            endExclusive - start,
            attributes,
            hasDynamicValue);
        return true;
    }

    private static int FindDirectiveEnd(string text, int start)
    {
        char? quote = null;
        for (var index = start; index + 1 < text.Length; index++)
        {
            var character = text[index];
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '%' && text[index + 1] == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static void SkipWhitespace(string text, ref int position, int endExclusive)
    {
        while (position < endExclusive && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private static string ReadName(string text, ref int position, int endExclusive)
    {
        var start = position;
        while (position < endExclusive &&
               (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '-' or ':' or '.'))
        {
            position++;
        }

        return text[start..position];
    }

    private static string? StaticLiteral(ParsedDirective directive, string name)
    {
        if (!directive.Attributes.TryGetValue(name, out var attribute))
        {
            return null;
        }

        var value = attribute.Value.Trim();
        if (value.Length == 0 ||
            value.Contains("<%", StringComparison.Ordinal) ||
            value.Contains("%>", StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static string QualifiedIdentity(string path, string service, int startOffset) =>
        $"wcf-svc|{path}|{service}|{startOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static SortedDictionary<string, string> Properties(
        params (string Name, string? Value)[] properties)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in properties)
        {
            if (value is not null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private sealed record ParsedSvcFile(
        IReadOnlyList<ParsedDirective> Directives,
        IReadOnlyList<SvcParseError> Errors);

    private sealed record ParsedDirective(
        string Name,
        int StartOffset,
        int Length,
        IReadOnlyDictionary<string, DirectiveAttribute> Attributes,
        bool HasDynamicValue);

    private sealed record DirectiveAttribute(
        string Name,
        string Value,
        int StartOffset,
        int Length);

    private sealed record SvcParseError(int Offset, string Message);
}

internal sealed record WcfSvcHostObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string? Service,
    string? Factory,
    string? CodeBehind,
    string? Language);

internal sealed class WcfSvcAnalysisResult
{
    public WcfSvcAnalysisResult(IEnumerable<WcfSvcHostObservation> declarations)
    {
        Declarations = declarations
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.StartOffset)
            .ThenBy(item => item.Node.NodeId, StringComparer.Ordinal)
            .ToArray();
        var index = new SortedDictionary<string, IReadOnlyList<WcfSvcHostObservation>>(StringComparer.Ordinal);
        foreach (var group in Declarations
                     .Where(item => item.Service is not null)
                     .GroupBy(item => item.Service!, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            index[group.Key] = group.ToArray();
        }

        DeclarationsByService = index;
    }

    public IReadOnlyList<WcfSvcHostObservation> Declarations { get; }
    public IReadOnlyList<WcfSvcHostObservation> HostingDeclarations => Declarations;
    public IReadOnlyDictionary<string, IReadOnlyList<WcfSvcHostObservation>> DeclarationsByService { get; }
}
