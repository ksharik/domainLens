using System.Globalization;
using DomainLens.Core;
using DomainLens.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using DiagnosticSeverity = DomainLens.Core.DiagnosticSeverity;

namespace DomainLens.Analyzer.Wcf;

internal sealed record WcfSourceTypeCandidate(
    INamedTypeSymbol Symbol,
    EvidenceNode Node,
    string ClrIdentity,
    string? ConfigurationName,
    bool IsServiceContract,
    bool IsServiceImplementation);

internal sealed record WcfSourceAnalysisResult(
    IReadOnlyList<WcfSourceTypeCandidate> SourceTypes,
    IReadOnlyList<WcfSourceTypeCandidate> ServiceContracts,
    IReadOnlyList<WcfSourceTypeCandidate> ServiceImplementations);

internal sealed class WcfSourceAnalyzer
{
    private const string SourceRelationshipDetails =
        "The source relationship was resolved in the repository-wide synthetic net472 compilation; effective project configuration was not evaluated.";

    private static readonly SymbolDisplayFormat DisplayFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly WcfContributionBuilder _builder;
    private readonly WcfSourceNodeIndex _nodeIndex;
    private readonly WcfFrameworkProfilePolicy _frameworkProfilePolicy;
    private readonly LegacySemanticCompilationContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, INamedTypeSymbol?> _framework = new(StringComparer.Ordinal);
    private readonly Dictionary<INamedTypeSymbol, SourceTypeInfo> _sourceTypes =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ContractInfo> _contracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ContractInfo> _callbackContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, EvidenceNode> _dataContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, EvidenceNode> _messageContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<SourceTypeInfo>> _ambiguousTypes =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<SourceTypeInfo>> _ambiguousServiceContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<EvidenceNode>> _ambiguousDataContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<EvidenceNode>> _ambiguousMessageContracts =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, OperationInfo> _operations =
        new(SymbolEqualityComparer.Default);
    private readonly List<WcfSourceTypeCandidate> _ambiguousSourceTypes = [];
    private readonly HashSet<string> _implementationNodeIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _profileDiagnostics = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unsupportedAttributeDiagnostics = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unsupportedPlacementDiagnostics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProfileEvidenceSource> _profileEvidence =
        new(StringComparer.Ordinal);

    public WcfSourceAnalyzer(
        WcfContributionBuilder builder,
        LegacySemanticCompilationContext context,
        CancellationToken cancellationToken)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _nodeIndex = new WcfSourceNodeIndex(builder.Baseline);
        _frameworkProfilePolicy = new WcfFrameworkProfilePolicy(builder.Baseline);
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cancellationToken = cancellationToken;
        foreach (var metadataName in FrameworkMetadataNames)
        {
            _framework[metadataName] = context.Compilation.GetTypeByMetadataName(metadataName);
        }
    }

    public WcfSourceAnalysisResult Analyze()
    {
        IndexSourceTypes();
        DiagnoseUnresolvedWcfAttributeSyntax();
        DiscoverAttributedTypes();
        DiscoverContractOperations();
        DiagnoseUnsupportedOperationAttributes();
        DiscoverAttributedMembers();
        DiscoverImplementationsAndClients();
        DiscoverAmbiguousImplementationsAndClients();
        DiscoverProgrammaticSites();
        DiagnoseFrameworkProfiles();

        var all = _sourceTypes.Values
            .Select(type => new WcfSourceTypeCandidate(
                type.Symbol,
                type.Node,
                Display(type.Symbol),
                _contracts.TryGetValue(type.Symbol, out var contract)
                    ? contract.ConfigurationName
                    : null,
                _contracts.ContainsKey(type.Symbol),
                _implementationNodeIds.Contains(type.Node.NodeId)))
            .Concat(_ambiguousSourceTypes.Select(type => type with
            {
                IsServiceImplementation = _implementationNodeIds.Contains(type.Node.NodeId),
            }))
            .OrderBy(item => item.ClrIdentity, StringComparer.Ordinal)
            .ThenBy(item => item.Node.NodeId, StringComparer.Ordinal)
            .ToArray();
        return new WcfSourceAnalysisResult(
            all,
            all.Where(item => item.IsServiceContract).ToArray(),
            all.Where(item => item.IsServiceImplementation).ToArray());
    }

    private void IndexSourceTypes()
    {
        var candidates = new Dictionary<INamedTypeSymbol, List<SourceTypeInfo>>(
            SymbolEqualityComparer.Default);
        foreach (var source in _context.SourceDocuments)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var model = _context.GetSemanticModel(source);
            var root = source.SyntaxTree.GetRoot(_cancellationToken);
            foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(declaration, _cancellationToken) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                foreach (var node in _nodeIndex.GetDeclarationNodes(
                             declaration,
                             "csharp.type-declaration"))
                {
                    if (!candidates.TryGetValue(symbol, out var symbolCandidates))
                    {
                        symbolCandidates = [];
                        candidates.Add(symbol, symbolCandidates);
                    }

                    symbolCandidates.Add(new SourceTypeInfo(symbol, node, source, declaration));
                }
            }
        }

        foreach (var pair in candidates.OrderBy(item => Display(item.Key), StringComparer.Ordinal))
        {
            var distinct = pair.Value
                .GroupBy(item => item.Node.NodeId, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(item => item.Source.RelativePath, StringComparer.Ordinal)
                    .ThenBy(item => item.Declaration.SpanStart)
                    .First())
                .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal)
                .ToArray();
            if (distinct.Length == 1)
            {
                _sourceTypes.Add(pair.Key, distinct[0]);
                continue;
            }

            RecordAmbiguousStructuralCorrelation(pair.Key, distinct);
        }
    }

    private void RecordAmbiguousStructuralCorrelation(
        INamedTypeSymbol symbol,
        IReadOnlyList<SourceTypeInfo> candidates)
    {
        _ambiguousTypes[symbol] = candidates.ToArray();
        foreach (var declarationGroup in candidates
                     .GroupBy(DeclarationIdentity, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var declarationCandidates = declarationGroup
                .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal)
                .ToArray();
            var representative = declarationCandidates[0];
            var serviceAttribute = FindAttributeForDeclaration(
                representative,
                WcfVocabulary.FrameworkTypes.ServiceContractAttribute);
            var dataAttribute = FindAttributeForDeclaration(
                representative,
                WcfVocabulary.FrameworkTypes.DataContractAttribute);
            var messageAttribute = FindAttributeForDeclaration(
                representative,
                WcfVocabulary.FrameworkTypes.MessageContractAttribute);
            var serviceSupported = serviceAttribute is not null &&
                                   symbol.TypeKind is TypeKind.Class or TypeKind.Interface;
            var configurationName = serviceSupported
                ? GetNamedString(serviceAttribute!, "ConfigurationName")
                : null;

            foreach (var candidate in declarationCandidates)
            {
                _ambiguousSourceTypes.Add(new WcfSourceTypeCandidate(
                    symbol,
                    candidate.Node,
                    Display(symbol),
                    configurationName,
                    serviceSupported,
                    IsServiceImplementation: false));
            }

            var evidenceIds = new List<string>();
            if (serviceSupported)
            {
                var evidenceId = EnrichAmbiguousTypeDeclaration(
                    symbol,
                    declarationCandidates,
                    serviceAttribute!,
                    WcfVocabulary.Rules.ServiceContract,
                    [WcfVocabulary.Attributes.ServiceContract],
                    ServiceContractProperties(symbol, serviceAttribute!, configurationName));
                evidenceIds.Add(evidenceId);
                AppendAmbiguousTypeCandidates(
                    _ambiguousServiceContracts,
                    symbol,
                    declarationCandidates);
            }
            else if (serviceAttribute is not null)
            {
                evidenceIds.Add(ReportAmbiguousUnsupportedPlacement(
                    representative,
                    serviceAttribute,
                    WcfVocabulary.Rules.ServiceContract,
                    "ServiceContract is only promoted on source classes and interfaces."));
            }

            if (dataAttribute is not null &&
                symbol.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum)
            {
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractName,
                    GetNamedString(dataAttribute, "Name"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractNamespace,
                    GetNamedString(dataAttribute, "Namespace"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractIsReference,
                    GetNamedValue(dataAttribute, "IsReference"));
                evidenceIds.Add(EnrichAmbiguousTypeDeclaration(
                    symbol,
                    declarationCandidates,
                    dataAttribute,
                    WcfVocabulary.Rules.DataContract,
                    [WcfVocabulary.Attributes.DataContract],
                    properties));
                AppendAmbiguousNodes(
                    _ambiguousDataContracts,
                    symbol,
                    declarationCandidates.Select(item => item.Node));
            }
            else if (dataAttribute is not null)
            {
                evidenceIds.Add(ReportAmbiguousUnsupportedPlacement(
                    representative,
                    dataAttribute,
                    WcfVocabulary.Rules.DataContract,
                    "DataContract is only promoted on source classes, structs, and enums."));
            }

            if (messageAttribute is not null &&
                symbol.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageIsWrapped,
                    GetNamedValue(messageAttribute, "IsWrapped"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageWrapperName,
                    GetNamedString(messageAttribute, "WrapperName"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageWrapperNamespace,
                    GetNamedString(messageAttribute, "WrapperNamespace"));
                evidenceIds.Add(EnrichAmbiguousTypeDeclaration(
                    symbol,
                    declarationCandidates,
                    messageAttribute,
                    WcfVocabulary.Rules.MessageContract,
                    [WcfVocabulary.Attributes.MessageContract],
                    properties));
                AppendAmbiguousNodes(
                    _ambiguousMessageContracts,
                    symbol,
                    declarationCandidates.Select(item => item.Node));
            }
            else if (messageAttribute is not null)
            {
                evidenceIds.Add(ReportAmbiguousUnsupportedPlacement(
                    representative,
                    messageAttribute,
                    WcfVocabulary.Rules.MessageContract,
                    "MessageContract is only promoted on source classes and structs."));
            }

            if (evidenceIds.Count == 0)
            {
                continue;
            }

            _builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                DiagnosticSeverity.Warning,
                "Child WCF member and implementation projection is limited for a source declaration that maps to multiple project-specific structural nodes.",
                representative.Source.RelativePath,
                evidenceIds,
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["candidateCount"] = declarationCandidates.Length.ToString(CultureInfo.InvariantCulture),
                    ["clrIdentity"] = Display(symbol),
                });
        }
    }

    private string EnrichAmbiguousTypeDeclaration(
        INamedTypeSymbol symbol,
        IReadOnlyList<SourceTypeInfo> candidates,
        AttributeData attribute,
        string ruleId,
        IReadOnlyCollection<string> attributes,
        IReadOnlyDictionary<string, string> properties)
    {
        var evidenceId = AddAttributeEvidence(
            candidates[0].Source,
            attribute,
            ruleId,
            ResolutionQuality.Ambiguous,
            "The framework attribute identity is known, but this physical declaration maps to multiple project-specific structural nodes.");
        foreach (var candidate in candidates)
        {
            _builder.EnrichNode(candidate.Node, [evidenceId], attributes, properties);
            TrackProfile(candidate.Node, candidate.Source.RelativePath);
        }

        _builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.AttributeIdentityUnresolved,
            DiagnosticSeverity.Warning,
            "A WCF-attributed source declaration maps to multiple project-specific structural nodes; DomainLens retained the candidates and did not guess a project context.",
            candidates[0].Source.RelativePath,
            [evidenceId],
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidateCount"] = candidates.Count.ToString(CultureInfo.InvariantCulture),
                ["clrIdentity"] = Display(symbol),
            });
        return evidenceId;
    }

    private string ReportAmbiguousUnsupportedPlacement(
        SourceTypeInfo sourceType,
        AttributeData attribute,
        string ruleId,
        string message)
    {
        var evidenceId = AddAttributeEvidence(
            sourceType.Source,
            attribute,
            ruleId,
            ResolutionQuality.Ambiguous,
            "The trusted framework attribute identity was established, but its physical declaration maps to multiple structural nodes and its placement is outside the bounded supported rule.");
        _builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
            DiagnosticSeverity.Warning,
            message,
            sourceType.Source.RelativePath,
            [evidenceId]);
        return evidenceId;
    }

    private static void AppendAmbiguousNodes(
        IDictionary<INamedTypeSymbol, IReadOnlyList<EvidenceNode>> index,
        INamedTypeSymbol symbol,
        IEnumerable<EvidenceNode> nodes)
    {
        index.TryGetValue(symbol, out var existing);
        index[symbol] = (existing ?? [])
            .Concat(nodes)
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendAmbiguousTypeCandidates(
        IDictionary<INamedTypeSymbol, IReadOnlyList<SourceTypeInfo>> index,
        INamedTypeSymbol symbol,
        IEnumerable<SourceTypeInfo> candidates)
    {
        index.TryGetValue(symbol, out var existing);
        index[symbol] = (existing ?? [])
            .Concat(candidates)
            .GroupBy(candidate => candidate.Node.NodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Node.NodeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string DeclarationIdentity(SourceTypeInfo sourceType) =>
        string.Join(
            "|",
            sourceType.Source.RelativePath,
            sourceType.Declaration.SpanStart.ToString(CultureInfo.InvariantCulture),
            sourceType.Declaration.Span.Length.ToString(CultureInfo.InvariantCulture));

    private void DiscoverAttributedTypes()
    {
        foreach (var type in _sourceTypes.Values
                     .OrderBy(item => item.Source.RelativePath, StringComparer.Ordinal)
                     .ThenBy(item => item.Declaration.SpanStart))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var serviceAttribute = FindAttribute(
                type.Symbol,
                WcfVocabulary.FrameworkTypes.ServiceContractAttribute);
            if (serviceAttribute is not null &&
                type.Symbol.TypeKind is TypeKind.Class or TypeKind.Interface)
            {
                var evidenceId = AddAttributeEvidence(
                    type.Source,
                    serviceAttribute,
                    WcfVocabulary.Rules.ServiceContract);
                var configurationName = GetNamedString(serviceAttribute, "ConfigurationName");
                var properties = ServiceContractProperties(
                    type.Symbol,
                    serviceAttribute,
                    configurationName);
                _builder.EnrichNode(
                    type.Node,
                    [evidenceId],
                    [WcfVocabulary.Attributes.ServiceContract],
                    properties);
                TrackProfile(type.Node, type.Source.RelativePath);
                _contracts[type.Symbol] = new ContractInfo(
                    type.Symbol,
                    type.Node,
                    type.Source,
                    serviceAttribute,
                    evidenceId,
                    configurationName);

                var callback = GetNamedType(serviceAttribute, "CallbackContract");
                if (callback is not null)
                {
                    var callbackCandidates = GetSourceTypeCandidates(callback);
                    var quality = callbackCandidates.Count switch
                    {
                        0 => ResolutionQuality.Unresolved,
                        1 => ResolutionQuality.Partial,
                        _ => ResolutionQuality.Ambiguous,
                    };
                    var relationshipEvidence = AddAttributeEvidence(
                        type.Source,
                        serviceAttribute,
                        WcfVocabulary.Rules.CallbackContract,
                        quality,
                        quality == ResolutionQuality.Partial
                            ? SourceRelationshipDetails
                            : "The callback contract target could not be uniquely correlated to a selected structural source node.");
                    if (callbackCandidates.Count == 1)
                    {
                        var callbackSource = callbackCandidates[0];
                        _builder.AddEdge(
                            WcfVocabulary.EdgeKinds.CallbackContract,
                            type.Node.NodeId,
                            callbackSource.Node.NodeId,
                            null,
                            [relationshipEvidence],
                            ResolutionBasis.Semantic,
                            ResolutionQuality.Partial,
                            SourceRelationshipDetails);
                        if (!_contracts.ContainsKey(callback))
                        {
                            _callbackContracts.TryAdd(
                                callback,
                                new ContractInfo(
                                    callback,
                                    callbackSource.Node,
                                    callbackSource.Source,
                                    serviceAttribute,
                                    relationshipEvidence,
                                    ConfigurationName: null));
                        }
                        TrackProfile(callbackSource.Node, callbackSource.Source.RelativePath);
                    }
                    else
                    {
                        _builder.AddEdge(
                            WcfVocabulary.EdgeKinds.CallbackContract,
                            type.Node.NodeId,
                            null,
                            Display(callback),
                            [relationshipEvidence],
                            ResolutionBasis.Semantic,
                            quality,
                            callbackCandidates.Count == 0
                                ? "The callback type is unavailable as a selected structural source node."
                                : "The callback type maps to multiple project-specific structural source nodes; DomainLens did not guess a target.");
                        foreach (var callbackSource in callbackCandidates)
                        {
                            TrackProfile(callbackSource.Node, callbackSource.Source.RelativePath);
                        }

                        _builder.AddDiagnostic(
                            WcfVocabulary.Diagnostics.CallbackContractUnresolved,
                            DiagnosticSeverity.Warning,
                            callbackCandidates.Count == 0
                                ? "A statically declared WCF callback contract could not be correlated to a selected source type."
                                : "A statically declared WCF callback contract maps to multiple project-specific source types and remains ambiguous.",
                            type.Source.RelativePath,
                            [relationshipEvidence],
                            new SortedDictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["candidateCount"] = callbackCandidates.Count.ToString(CultureInfo.InvariantCulture),
                                ["target"] = Display(callback),
                            });
                    }
                }
            }
            else if (serviceAttribute is not null)
            {
                ReportUnsupportedAttributePlacement(
                    type,
                    serviceAttribute,
                    WcfVocabulary.Rules.ServiceContract,
                    "ServiceContract is only promoted on source classes and interfaces.");
            }

            var dataAttribute = FindAttribute(
                type.Symbol,
                WcfVocabulary.FrameworkTypes.DataContractAttribute);
            if (dataAttribute is not null &&
                type.Symbol.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum)
            {
                var evidenceId = AddAttributeEvidence(
                    type.Source,
                    dataAttribute,
                    WcfVocabulary.Rules.DataContract);
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractName,
                    GetNamedString(dataAttribute, "Name"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractNamespace,
                    GetNamedString(dataAttribute, "Namespace"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.DataContractIsReference,
                    GetNamedValue(dataAttribute, "IsReference"));
                _builder.EnrichNode(
                    type.Node,
                    [evidenceId],
                    [WcfVocabulary.Attributes.DataContract],
                    properties);
                TrackProfile(type.Node, type.Source.RelativePath);
                _dataContracts[type.Symbol] = type.Node;
            }
            else if (dataAttribute is not null)
            {
                ReportUnsupportedAttributePlacement(
                    type,
                    dataAttribute,
                    WcfVocabulary.Rules.DataContract,
                    "DataContract is only promoted on source classes, structs, and enums.");
            }

            var messageAttribute = FindAttribute(
                type.Symbol,
                WcfVocabulary.FrameworkTypes.MessageContractAttribute);
            if (messageAttribute is not null &&
                type.Symbol.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                var evidenceId = AddAttributeEvidence(
                    type.Source,
                    messageAttribute,
                    WcfVocabulary.Rules.MessageContract);
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageIsWrapped,
                    GetNamedValue(messageAttribute, "IsWrapped"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageWrapperName,
                    GetNamedString(messageAttribute, "WrapperName"));
                AddIfNotNull(
                    properties,
                    WcfVocabulary.Properties.MessageWrapperNamespace,
                    GetNamedString(messageAttribute, "WrapperNamespace"));
                _builder.EnrichNode(
                    type.Node,
                    [evidenceId],
                    [WcfVocabulary.Attributes.MessageContract],
                    properties);
                TrackProfile(type.Node, type.Source.RelativePath);
                _messageContracts[type.Symbol] = type.Node;
            }
            else if (messageAttribute is not null)
            {
                ReportUnsupportedAttributePlacement(
                    type,
                    messageAttribute,
                    WcfVocabulary.Rules.MessageContract,
                    "MessageContract is only promoted on source classes and structs.");
            }
        }
    }

    private void DiscoverContractOperations()
    {
        foreach (var contract in _contracts.Values
                     .Concat(_callbackContracts.Values)
                     .GroupBy(item => item.Node.NodeId, StringComparer.Ordinal)
                     .Select(group => group.First())
                     .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal))
        {
            var candidateTypes = new[] { contract.Symbol }
                .Concat(contract.Symbol.AllInterfaces)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .OrderBy(item => Display(item), StringComparer.Ordinal);
            foreach (var declaringType in candidateTypes)
            {
                foreach (var method in declaringType.GetMembers()
                             .OfType<IMethodSymbol>()
                             .Where(item => item.MethodKind == MethodKind.Ordinary)
                             .OrderBy(Display, StringComparer.Ordinal))
                {
                    var operationAttribute = FindAttribute(
                        method,
                        WcfVocabulary.FrameworkTypes.OperationContractAttribute);
                    if (operationAttribute is null ||
                        !_nodeIndex.TryGetMethodNode(method, out var operationNode) ||
                        !_context.TryGetSourceDocument(
                            method.Locations.FirstOrDefault(location => location.IsInSource)
                                ?.SourceTree?.FilePath ?? string.Empty,
                            out var source) ||
                        source is null)
                    {
                        continue;
                    }

                    if (!_operations.TryGetValue(method, out var operation))
                    {
                        var evidenceId = AddAttributeEvidence(
                            source,
                            operationAttribute,
                            WcfVocabulary.Rules.OperationContract);
                        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
                        {
                            [WcfVocabulary.Properties.OperationName] =
                                GetNamedString(operationAttribute, "Name") ?? method.Name,
                            [WcfVocabulary.Properties.ParameterTypes] = string.Join(
                                ";",
                                method.Parameters.Select(parameter => Display(parameter.Type))),
                            [WcfVocabulary.Properties.ReturnType] = Display(method.ReturnType),
                        };
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationAction,
                            GetNamedString(operationAttribute, "Action"));
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationReplyAction,
                            GetNamedString(operationAttribute, "ReplyAction"));
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationIsOneWay,
                            GetNamedValue(operationAttribute, "IsOneWay"));
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationAsyncPattern,
                            GetNamedValue(operationAttribute, "AsyncPattern"));
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationIsInitiating,
                            GetNamedValue(operationAttribute, "IsInitiating"));
                        AddIfNotNull(
                            properties,
                            WcfVocabulary.Properties.OperationIsTerminating,
                            GetNamedValue(operationAttribute, "IsTerminating"));
                        _builder.EnrichNode(
                            operationNode,
                            [evidenceId],
                            [WcfVocabulary.Attributes.OperationContract],
                            properties);
                        operation = new OperationInfo(
                            method,
                            operationNode,
                            source,
                            operationAttribute,
                            evidenceId);
                        _operations.Add(method, operation);
                        DiscoverFaults(operation);
                        DiscoverOperationContractUses(operation);
                    }

                    var inherited = !SymbolEqualityComparer.Default.Equals(
                        method.ContainingType,
                        contract.Symbol);
                    var relationshipEvidence = AddAttributeEvidence(
                        operation.Source,
                        operation.Attribute,
                        WcfVocabulary.Rules.ContractOperation,
                        ResolutionQuality.Partial,
                        SourceRelationshipDetails);
                    _builder.AddEdge(
                        WcfVocabulary.EdgeKinds.ContractOperation,
                        contract.Node.NodeId,
                        operation.Node.NodeId,
                        null,
                        [relationshipEvidence],
                        ResolutionBasis.Semantic,
                        ResolutionQuality.Partial,
                        inherited
                            ? "The operation is inherited by the service contract in the repository-wide synthetic compilation."
                            : SourceRelationshipDetails);
                }
            }
        }
    }

    private void DiscoverFaults(OperationInfo operation)
    {
        var faultAttributes = FindAttributes(
                operation.Symbol,
                WcfVocabulary.FrameworkTypes.FaultContractAttribute)
            .OrderBy(item => item.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue)
            .ToArray();
        for (var index = 0; index < faultAttributes.Length; index++)
        {
            var attribute = faultAttributes[index];
            var evidenceId = AddAttributeEvidence(
                operation.Source,
                attribute,
                WcfVocabulary.Rules.FaultContract);
            var detail = GetConstructorType(attribute, 0);
            var detailIdentity = detail is null ? "<unresolved-fault-detail>" : Display(detail);
            var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [WcfVocabulary.Properties.DetailType] = detailIdentity,
            };
            AddIfNotNull(properties, WcfVocabulary.Properties.FaultName, GetNamedString(attribute, "Name"));
            AddIfNotNull(properties, WcfVocabulary.Properties.FaultNamespace, GetNamedString(attribute, "Namespace"));
            AddIfNotNull(properties, WcfVocabulary.Properties.FaultAction, GetNamedString(attribute, "Action"));
            var faultNode = _builder.AddNode(
                WcfVocabulary.NodeKinds.FaultDeclaration,
                "FaultContract",
                $"{operation.Node.QualifiedName}#fault:{index}:{detailIdentity}",
                operation.Node.ProjectId,
                [evidenceId],
                properties: properties);
            _builder.AddEdge(
                WcfVocabulary.EdgeKinds.DeclaresFault,
                operation.Node.NodeId,
                faultNode.NodeId,
                null,
                [evidenceId],
                ResolutionBasis.Semantic,
                ResolutionQuality.Partial,
                SourceRelationshipDetails);

            var detailCandidates = detail is null
                ? Array.Empty<SourceTypeInfo>()
                : GetSourceTypeCandidates(detail);
            if (detailCandidates.Count == 1)
            {
                var detailSource = detailCandidates[0];
                _builder.AddEdge(
                    WcfVocabulary.EdgeKinds.FaultDetailType,
                    faultNode.NodeId,
                    detailSource.Node.NodeId,
                    null,
                    [evidenceId],
                    ResolutionBasis.Semantic,
                    ResolutionQuality.Partial,
                    SourceRelationshipDetails);
            }
            else
            {
                var quality = detailCandidates.Count == 0
                    ? ResolutionQuality.Unresolved
                    : ResolutionQuality.Ambiguous;
                _builder.AddEdge(
                    WcfVocabulary.EdgeKinds.FaultDetailType,
                    faultNode.NodeId,
                    null,
                    detailIdentity,
                    [evidenceId],
                    ResolutionBasis.Semantic,
                    quality,
                    detailCandidates.Count == 0
                        ? "The statically declared fault detail type is unavailable as a selected source node."
                        : "The statically declared fault detail type maps to multiple project-specific source nodes; DomainLens did not guess a target.");
                _builder.AddDiagnostic(
                    detailCandidates.Count == 0
                        ? WcfVocabulary.Diagnostics.GeneratedTypeUnavailable
                        : WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                    DiagnosticSeverity.Warning,
                    detailCandidates.Count == 0
                        ? "A statically declared WCF fault detail type could not be correlated to a selected source declaration."
                        : "A statically declared WCF fault detail type maps to multiple project-specific source declarations and remains ambiguous.",
                    operation.Source.RelativePath,
                    [evidenceId],
                    new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["candidateCount"] = detailCandidates.Count.ToString(CultureInfo.InvariantCulture),
                        ["target"] = detailIdentity,
                    });
            }
        }
    }

    private void DiscoverOperationContractUses(OperationInfo operation)
    {
        var syntax = operation.Symbol.DeclaringSyntaxReferences
            .OrderBy(item => item.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Span.Start)
            .Select(item => item.GetSyntax(_cancellationToken))
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (syntax is null)
        {
            return;
        }

        for (var index = 0; index < operation.Symbol.Parameters.Length; index++)
        {
            var typeSyntax = index < syntax.ParameterList.Parameters.Count
                ? syntax.ParameterList.Parameters[index].Type
                : null;
            AddContractUseEdges(
                operation,
                operation.Symbol.Parameters[index].Type,
                (SyntaxNode?)typeSyntax ?? syntax,
                WcfVocabulary.Rules.OperationParameterContract);
        }

        AddContractUseEdges(
            operation,
            operation.Symbol.ReturnType,
            syntax.ReturnType,
            WcfVocabulary.Rules.OperationReturnContract);
    }

    private void AddContractUseEdges(
        OperationInfo operation,
        ITypeSymbol type,
        SyntaxNode sourceSyntax,
        string ruleId)
    {
        foreach (var namedType in EnumerateNamedTypes(type)
                     .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                     .OrderBy(item => Display(item), StringComparer.Ordinal))
        {
            string? edgeKind = null;
            IReadOnlyList<EvidenceNode> targets = [];
            if (_dataContracts.TryGetValue(namedType, out var dataTarget))
            {
                edgeKind = WcfVocabulary.EdgeKinds.UsesDataContract;
                targets = [dataTarget];
            }
            else if (_ambiguousDataContracts.TryGetValue(namedType, out var ambiguousDataTargets))
            {
                edgeKind = WcfVocabulary.EdgeKinds.UsesDataContract;
                targets = ambiguousDataTargets;
            }
            else if (_messageContracts.TryGetValue(namedType, out var messageTarget))
            {
                edgeKind = WcfVocabulary.EdgeKinds.UsesMessageContract;
                targets = [messageTarget];
            }
            else if (_ambiguousMessageContracts.TryGetValue(namedType, out var ambiguousMessageTargets))
            {
                edgeKind = WcfVocabulary.EdgeKinds.UsesMessageContract;
                targets = ambiguousMessageTargets;
            }

            if (edgeKind is null || targets.Count == 0)
            {
                continue;
            }

            var quality = targets.Count == 1
                ? ResolutionQuality.Partial
                : ResolutionQuality.Ambiguous;
            var evidenceId = AddSyntaxEvidence(
                operation.Source,
                sourceSyntax,
                ruleId,
                ResolutionBasis.Semantic,
                quality,
                quality == ResolutionQuality.Partial
                    ? SourceRelationshipDetails
                    : "The referenced WCF contract type maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
            _builder.AddEdge(
                edgeKind,
                operation.Node.NodeId,
                targets.Count == 1 ? targets[0].NodeId : null,
                targets.Count == 1 ? null : Display(namedType),
                [evidenceId],
                ResolutionBasis.Semantic,
                quality,
                quality == ResolutionQuality.Partial
                    ? SourceRelationshipDetails
                    : "The referenced WCF contract type maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
            if (targets.Count > 1)
            {
                _builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                    DiagnosticSeverity.Warning,
                    "An operation data/message contract maps to multiple project-specific source declarations and remains ambiguous.",
                    operation.Source.RelativePath,
                    [evidenceId],
                    new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["candidateCount"] = targets.Count.ToString(CultureInfo.InvariantCulture),
                        ["target"] = Display(namedType),
                    });
            }
        }
    }

    private void DiscoverAttributedMembers()
    {
        foreach (var type in _sourceTypes.Values
                     .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal))
        {
            foreach (var member in type.Symbol.GetMembers().OrderBy(Display, StringComparer.Ordinal))
            {
                if (!_nodeIndex.TryGetMemberNode(member, out var memberNode))
                {
                    continue;
                }

                var sourcePath = member.Locations.FirstOrDefault(item => item.IsInSource)
                    ?.SourceTree?.FilePath;
                if (!_context.TryGetSourceDocument(sourcePath ?? string.Empty, out var source) || source is null)
                {
                    continue;
                }

                var dataMember = FindAttribute(member, WcfVocabulary.FrameworkTypes.DataMemberAttribute);
                if (dataMember is not null &&
                    member is IFieldSymbol or IPropertySymbol &&
                    _dataContracts.TryGetValue(type.Symbol, out var dataOwnerNode))
                {
                    var evidenceId = AddAttributeEvidence(source, dataMember, WcfVocabulary.Rules.DataMember);
                    var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    AddIfNotNull(properties, WcfVocabulary.Properties.DataMemberName, GetNamedString(dataMember, "Name"));
                    AddIfNotNull(properties, WcfVocabulary.Properties.DataMemberOrder, GetNamedValue(dataMember, "Order"));
                    AddIfNotNull(properties, WcfVocabulary.Properties.DataMemberIsRequired, GetNamedValue(dataMember, "IsRequired"));
                    AddIfNotNull(properties, WcfVocabulary.Properties.DataMemberEmitDefaultValue, GetNamedValue(dataMember, "EmitDefaultValue"));
                    _builder.EnrichNode(
                        memberNode,
                        [evidenceId],
                        [WcfVocabulary.Attributes.DataMember],
                        properties);
                    _builder.AddEdge(
                        WcfVocabulary.EdgeKinds.DataMember,
                        dataOwnerNode.NodeId,
                        memberNode.NodeId,
                        null,
                        [evidenceId],
                        ResolutionBasis.Semantic,
                        ResolutionQuality.Partial,
                        SourceRelationshipDetails);
                }
                else if (dataMember is not null)
                {
                    ReportUnsupportedAttributePlacement(
                        source,
                        dataMember,
                        WcfVocabulary.Rules.DataMember,
                        "DataMember is only promoted on a field or property within a recognized DataContract.");
                }

                var header = FindAttribute(member, WcfVocabulary.FrameworkTypes.MessageHeaderAttribute);
                if (header is not null &&
                    member is IFieldSymbol or IPropertySymbol &&
                    _messageContracts.TryGetValue(type.Symbol, out var headerOwnerNode))
                {
                    var evidenceId = AddAttributeEvidence(source, header, WcfVocabulary.Rules.MessageHeader);
                    var properties = MessageMemberProperties(
                        header,
                        WcfVocabulary.Properties.MessageHeaderName,
                        WcfVocabulary.Properties.MessageHeaderNamespace,
                        orderProperty: null);
                    AddIfNotNull(properties, WcfVocabulary.Properties.MessageHeaderActor, GetNamedString(header, "Actor"));
                    AddIfNotNull(properties, WcfVocabulary.Properties.MessageHeaderMustUnderstand, GetNamedValue(header, "MustUnderstand"));
                    AddIfNotNull(properties, WcfVocabulary.Properties.MessageHeaderRelay, GetNamedValue(header, "Relay"));
                    _builder.EnrichNode(
                        memberNode,
                        [evidenceId],
                        [WcfVocabulary.Attributes.MessageHeader],
                        properties);
                    _builder.AddEdge(
                        WcfVocabulary.EdgeKinds.MessageHeader,
                        headerOwnerNode.NodeId,
                        memberNode.NodeId,
                        null,
                        [evidenceId],
                        ResolutionBasis.Semantic,
                        ResolutionQuality.Partial,
                        SourceRelationshipDetails);
                }
                else if (header is not null)
                {
                    ReportUnsupportedAttributePlacement(
                        source,
                        header,
                        WcfVocabulary.Rules.MessageHeader,
                        "MessageHeader is only promoted on a field or property within a recognized MessageContract.");
                }

                var body = FindAttribute(member, WcfVocabulary.FrameworkTypes.MessageBodyMemberAttribute);
                if (body is not null &&
                    member is IFieldSymbol or IPropertySymbol &&
                    _messageContracts.TryGetValue(type.Symbol, out var bodyOwnerNode))
                {
                    var evidenceId = AddAttributeEvidence(source, body, WcfVocabulary.Rules.MessageBodyMember);
                    var properties = MessageMemberProperties(
                        body,
                        WcfVocabulary.Properties.MessageBodyMemberName,
                        WcfVocabulary.Properties.MessageBodyMemberNamespace,
                        WcfVocabulary.Properties.MessageBodyMemberOrder);
                    _builder.EnrichNode(
                        memberNode,
                        [evidenceId],
                        [WcfVocabulary.Attributes.MessageBodyMember],
                        properties);
                    _builder.AddEdge(
                        WcfVocabulary.EdgeKinds.MessageBodyMember,
                        bodyOwnerNode.NodeId,
                        memberNode.NodeId,
                        null,
                        [evidenceId],
                        ResolutionBasis.Semantic,
                        ResolutionQuality.Partial,
                        SourceRelationshipDetails);
                }
                else if (body is not null)
                {
                    ReportUnsupportedAttributePlacement(
                        source,
                        body,
                        WcfVocabulary.Rules.MessageBodyMember,
                        "MessageBodyMember is only promoted on a field or property within a recognized MessageContract.");
                }
            }
        }
    }

    private void DiscoverImplementationsAndClients()
    {
        var clientBaseDefinition = Framework(WcfVocabulary.FrameworkTypes.ClientBase);
        var compilerGenerated = Framework(WcfVocabulary.FrameworkTypes.CompilerGeneratedAttribute);
        var generatedCode = Framework(WcfVocabulary.FrameworkTypes.GeneratedCodeAttribute);
        foreach (var type in _sourceTypes.Values
                     .Where(item => item.Symbol.TypeKind == TypeKind.Class)
                     .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal))
        {
            foreach (var contractType in type.Symbol.AllInterfaces
                         .Where(item => _contracts.ContainsKey(item))
                         .OrderBy(Display, StringComparer.Ordinal))
            {
                var contract = _contracts[contractType];
                var evidenceId = AddSyntaxEvidence(
                    type.Source,
                    type.Declaration,
                    WcfVocabulary.Rules.ImplementsContract,
                    ResolutionBasis.Semantic,
                    ResolutionQuality.Partial,
                    SourceRelationshipDetails);
                _builder.AddEdge(
                    WcfVocabulary.EdgeKinds.ImplementsContract,
                    type.Node.NodeId,
                    contract.Node.NodeId,
                    null,
                    [evidenceId],
                    ResolutionBasis.Semantic,
                    ResolutionQuality.Partial,
                    SourceRelationshipDetails);
                _implementationNodeIds.Add(type.Node.NodeId);
                TrackProfile(type.Node, type.Source.RelativePath);

                foreach (var operation in _operations.Values
                             .Where(item => contractType.Equals(item.Symbol.ContainingType, SymbolEqualityComparer.Default) ||
                                            contractType.AllInterfaces.Contains(
                                                item.Symbol.ContainingType,
                                                SymbolEqualityComparer.Default))
                             .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal))
                {
                    var implementation = type.Symbol.FindImplementationForInterfaceMember(operation.Symbol);
                    if (implementation is IMethodSymbol implementationMethod &&
                        _nodeIndex.TryGetMethodNode(implementationMethod, out var implementationNode) &&
                        TryGetSource(implementationMethod, out var implementationSource))
                    {
                        var methodEvidence = AddDeclarationEvidence(
                            implementationSource,
                            implementationMethod,
                            WcfVocabulary.Rules.ImplementsOperation);
                        _builder.AddEdge(
                            WcfVocabulary.EdgeKinds.ImplementsOperation,
                            implementationNode.NodeId,
                            operation.Node.NodeId,
                            null,
                            [methodEvidence],
                            ResolutionBasis.Semantic,
                            ResolutionQuality.Partial,
                            SourceRelationshipDetails);
                    }
                    else
                    {
                        _builder.AddDiagnostic(
                            WcfVocabulary.Diagnostics.ImplementationUnresolved,
                            DiagnosticSeverity.Warning,
                            "A service contract operation implementation could not be correlated to a selected source method.",
                            type.Source.RelativePath,
                            [evidenceId]);
                    }
                }
            }

            var clientBase = FindConstructedBase(type.Symbol, clientBaseDefinition);
            if (clientBase is null || clientBase.TypeArguments.Length != 1)
            {
                continue;
            }

            var clientEvidence = AddSyntaxEvidence(
                type.Source,
                type.Declaration,
                WcfVocabulary.Rules.ClientBase,
                ResolutionBasis.Semantic,
                ResolutionQuality.Partial,
                SourceRelationshipDetails);
            var isCompilerGenerated = type.Symbol.GetAttributes().Any(attribute =>
                (compilerGenerated is not null &&
                 SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, compilerGenerated)) ||
                (generatedCode is not null &&
                 SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, generatedCode)));
            var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (isCompilerGenerated)
            {
                properties[WcfVocabulary.Properties.Generated] = "true";
            }

            _builder.EnrichNode(
                type.Node,
                [clientEvidence],
                [WcfVocabulary.Attributes.ClientBase],
                properties);
            TrackProfile(type.Node, type.Source.RelativePath);
            AddClientContractEdge(
                type.Node,
                clientBase.TypeArguments[0],
                clientEvidence,
                WcfVocabulary.Rules.ClientBase);
        }
    }

    private void DiscoverAmbiguousImplementationsAndClients()
    {
        var clientBaseDefinition = Framework(WcfVocabulary.FrameworkTypes.ClientBase);
        foreach (var group in _ambiguousTypes
                     .Where(item => item.Key.TypeKind == TypeKind.Class)
                     .OrderBy(item => Display(item.Key), StringComparer.Ordinal))
        {
            var symbol = group.Key;
            foreach (var declarationGroup in group.Value
                         .GroupBy(DeclarationIdentity, StringComparer.Ordinal)
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var candidates = declarationGroup
                    .OrderBy(item => item.Node.NodeId, StringComparer.Ordinal)
                    .ToArray();
                var representative = candidates[0];
                var implementedContracts = GetInterfacesForDeclaration(representative)
                    .Where(item => GetServiceContractCandidates(item).Count > 0)
                    .OrderBy(Display, StringComparer.Ordinal)
                    .ToArray();
                foreach (var contractType in implementedContracts)
                {
                    var targets = GetServiceContractCandidates(contractType);
                    var quality = targets.Count switch
                    {
                        0 => ResolutionQuality.Unresolved,
                        1 => ResolutionQuality.Partial,
                        _ => ResolutionQuality.Ambiguous,
                    };
                    foreach (var candidate in candidates)
                    {
                        var evidenceId = AddSyntaxEvidence(
                            candidate.Source,
                            candidate.Declaration,
                            WcfVocabulary.Rules.ImplementsContract,
                            ResolutionBasis.Semantic,
                            quality,
                            quality == ResolutionQuality.Partial
                                ? SourceRelationshipDetails
                                : "The implementation or contract declaration maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
                        _builder.AddEdge(
                            WcfVocabulary.EdgeKinds.ImplementsContract,
                            candidate.Node.NodeId,
                            targets.Count == 1 ? targets[0].Node.NodeId : null,
                            targets.Count == 1 ? null : Display(contractType),
                            [evidenceId],
                            ResolutionBasis.Semantic,
                            quality,
                            quality == ResolutionQuality.Partial
                                ? SourceRelationshipDetails
                                : "The implementation or contract declaration maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
                        _implementationNodeIds.Add(candidate.Node.NodeId);
                        TrackProfile(candidate.Node, candidate.Source.RelativePath);
                    }

                    _builder.AddDiagnostic(
                        WcfVocabulary.Diagnostics.ImplementationAmbiguous,
                        DiagnosticSeverity.Warning,
                        "A WCF service implementation shares a CLR identity with multiple structural declarations; contract relationships were retained from this declaration's base syntax, while implementation-method projection remains limited.",
                        representative.Source.RelativePath,
                        properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["candidateCount"] = group.Value.Count.ToString(CultureInfo.InvariantCulture),
                            ["contractCandidateCount"] = targets.Count.ToString(CultureInfo.InvariantCulture),
                            ["contract"] = Display(contractType),
                            ["declarationCandidateCount"] = candidates.Length.ToString(CultureInfo.InvariantCulture),
                            ["implementation"] = Display(symbol),
                        });
                }

                var clientBase = FindConstructedBase(representative, clientBaseDefinition);
                if (clientBase is null || clientBase.TypeArguments.Length != 1)
                {
                    continue;
                }

                var isCompilerGenerated = FindAttributeForDeclaration(
                        representative,
                        WcfVocabulary.FrameworkTypes.CompilerGeneratedAttribute) is not null ||
                    FindAttributeForDeclaration(
                        representative,
                        WcfVocabulary.FrameworkTypes.GeneratedCodeAttribute) is not null;
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (isCompilerGenerated)
                {
                    properties[WcfVocabulary.Properties.Generated] = "true";
                }

                foreach (var candidate in candidates)
                {
                    var evidenceId = AddSyntaxEvidence(
                        candidate.Source,
                        candidate.Declaration,
                        WcfVocabulary.Rules.ClientBase,
                        ResolutionBasis.Semantic,
                        ResolutionQuality.Ambiguous,
                        "The trusted ClientBase<T> identity is known, but the CLR identity maps to multiple structural declarations.");
                    _builder.EnrichNode(
                        candidate.Node,
                        [evidenceId],
                        [WcfVocabulary.Attributes.ClientBase],
                        properties);
                    TrackProfile(candidate.Node, candidate.Source.RelativePath);
                    AddClientContractEdge(
                        candidate.Node,
                        clientBase.TypeArguments[0],
                        evidenceId,
                        WcfVocabulary.Rules.ClientBase);
                }

                _builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                    DiagnosticSeverity.Warning,
                    "A ClientBase<T> declaration shares a CLR identity with multiple structural declarations; only candidates for the physical declaration containing the recognized base syntax were enriched.",
                    representative.Source.RelativePath,
                    properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["candidateCount"] = group.Value.Count.ToString(CultureInfo.InvariantCulture),
                        ["client"] = Display(symbol),
                        ["declarationCandidateCount"] = candidates.Length.ToString(CultureInfo.InvariantCulture),
                    });
            }
        }
    }

    private void DiscoverProgrammaticSites()
    {
        var serviceHost = Framework(WcfVocabulary.FrameworkTypes.ServiceHost);
        var channelFactory = Framework(WcfVocabulary.FrameworkTypes.ChannelFactory);
        foreach (var source in _context.SourceDocuments)
        {
            var model = _context.GetSemanticModel(source);
            var root = source.SyntaxTree.GetRoot(_cancellationToken);
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (model.GetTypeInfo(creation, _cancellationToken).Type is not INamedTypeSymbol createdType)
                {
                    continue;
                }

                var original = createdType.OriginalDefinition;
                if (serviceHost is not null && SymbolEqualityComparer.Default.Equals(original, serviceHost))
                {
                    AddServiceHostSite(source, model, creation);
                }
                else if (channelFactory is not null &&
                         SymbolEqualityComparer.Default.Equals(original, channelFactory))
                {
                    AddChannelFactorySite(source, model, creation, createdType);
                }
            }
        }
    }

    private void AddServiceHostSite(
        LegacySemanticSourceDocument source,
        SemanticModel model,
        ObjectCreationExpressionSyntax creation)
    {
        var evidenceId = AddSyntaxEvidence(
            source,
            creation,
            WcfVocabulary.Rules.ServiceHost,
            ResolutionBasis.Semantic,
            ResolutionQuality.Exact,
            "The constructed type resolved to the trusted System.ServiceModel.ServiceHost metadata identity.");
        var site = _builder.AddNode(
            WcfVocabulary.NodeKinds.ServiceHostSite,
            $"ServiceHost@{Line(creation)}",
            $"WcfServiceHostSite|{source.RelativePath}|source|{creation.SpanStart}",
            GetContainingProjectId(model, creation),
            [evidenceId]);
        TrackProfile(site, source.RelativePath);
        var typeOf = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression as TypeOfExpressionSyntax;
        var hostedType = typeOf is null
            ? null
            : model.GetTypeInfo(typeOf.Type, _cancellationToken).Type as INamedTypeSymbol;
        var hostedCandidates = hostedType is null
            ? Array.Empty<SourceTypeInfo>()
            : GetSourceTypeCandidates(hostedType);
        if (hostedCandidates.Count == 1)
        {
            var sourceType = hostedCandidates[0];
            _builder.AddEdge(
                WcfVocabulary.EdgeKinds.HostsService,
                site.NodeId,
                sourceType.Node.NodeId,
                null,
                [evidenceId],
                ResolutionBasis.Semantic,
                ResolutionQuality.Partial,
                SourceRelationshipDetails);
        }
        else
        {
            var target = hostedType is null ? "<dynamic-service-type>" : Display(hostedType);
            var quality = hostedCandidates.Count == 0
                ? ResolutionQuality.Unresolved
                : ResolutionQuality.Ambiguous;
            _builder.AddEdge(
                WcfVocabulary.EdgeKinds.HostsService,
                site.NodeId,
                null,
                target,
                [evidenceId],
                ResolutionBasis.Semantic,
                quality,
                hostedCandidates.Count == 0
                    ? "Only direct ServiceHost(typeof(T)) service selection with an available source type is resolved."
                    : "The direct ServiceHost(typeof(T)) target maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
            _builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                DiagnosticSeverity.Warning,
                hostedCandidates.Count == 0
                    ? "A ServiceHost construction did not use a directly resolvable typeof(service) argument."
                    : "A ServiceHost construction target maps to multiple project-specific source declarations and remains ambiguous.",
                source.RelativePath,
                [evidenceId],
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["candidateCount"] = hostedCandidates.Count.ToString(CultureInfo.InvariantCulture),
                    ["target"] = target,
                });
        }
    }

    private void AddChannelFactorySite(
        LegacySemanticSourceDocument source,
        SemanticModel model,
        ObjectCreationExpressionSyntax creation,
        INamedTypeSymbol createdType)
    {
        var evidenceId = AddSyntaxEvidence(
            source,
            creation,
            WcfVocabulary.Rules.ChannelFactory,
            ResolutionBasis.Semantic,
            ResolutionQuality.Exact,
            "The constructed generic type resolved to the trusted System.ServiceModel.ChannelFactory<T> metadata identity.");
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var creationOperation = model.GetOperation(creation, _cancellationToken) as IObjectCreationOperation;
        var constructor = creationOperation?.Constructor ??
                          model.GetSymbolInfo(creation, _cancellationToken).Symbol as IMethodSymbol;
        var unsupportedSelection = constructor is null;
        var endpointNameArgument = constructor is not null && constructor.Parameters.Length > 0 &&
                                   constructor.Parameters[0].Type.SpecialType == SpecialType.System_String
            ? FindArgument(creationOperation, constructor.Parameters[0])
            : null;
        if (endpointNameArgument is not null &&
            model.GetConstantValue(endpointNameArgument.Expression, _cancellationToken) is
            { HasValue: true, Value: string endpointName })
        {
            properties[WcfVocabulary.Properties.EndpointName] = endpointName;
        }
        else if (endpointNameArgument is not null)
        {
            unsupportedSelection = true;
        }

        var endpointAddressType = Framework(WcfVocabulary.FrameworkTypes.EndpointAddress);
        ExpressionSyntax? address = null;
        var hasEndpointAddressParameter = false;
        if (constructor is not null && endpointAddressType is not null)
        {
            for (var index = 0; index < constructor.Parameters.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        constructor.Parameters[index].Type,
                        endpointAddressType))
                {
                    continue;
                }

                hasEndpointAddressParameter = true;
                var argument = FindArgument(creationOperation, constructor.Parameters[index]);
                if (argument?.Expression is not ObjectCreationExpressionSyntax endpointCreation ||
                    !SymbolEqualityComparer.Default.Equals(
                        model.GetTypeInfo(endpointCreation, _cancellationToken).Type,
                        endpointAddressType))
                {
                    continue;
                }

                address = endpointCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                break;
            }
        }
        if (address is not null && model.GetConstantValue(address, _cancellationToken) is { HasValue: true, Value: string addressValue })
        {
            properties[WcfVocabulary.Properties.Address] = addressValue;
        }
        else if (hasEndpointAddressParameter)
        {
            unsupportedSelection = true;
        }

        var site = _builder.AddNode(
            WcfVocabulary.NodeKinds.ChannelFactorySite,
            $"ChannelFactory@{Line(creation)}",
            $"WcfChannelFactorySite|{source.RelativePath}|source|{creation.SpanStart}",
            GetContainingProjectId(model, creation),
            [evidenceId],
            properties: properties);
        TrackProfile(site, source.RelativePath);
        if (unsupportedSelection)
        {
            _builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                DiagnosticSeverity.Warning,
                "A ChannelFactory<T> construction used endpoint selection outside the bounded direct literal/configured-name patterns; no runtime selection was inferred.",
                source.RelativePath,
                [evidenceId]);
        }

        if (createdType.TypeArguments.Length == 1)
        {
            AddClientContractEdge(
                site,
                createdType.TypeArguments[0],
                evidenceId,
                WcfVocabulary.Rules.ChannelFactory);
        }
    }

    private static ArgumentSyntax? FindArgument(
        IObjectCreationOperation? creation,
        IParameterSymbol parameter) =>
        creation?.Arguments
            .FirstOrDefault(argument =>
                !argument.IsImplicit &&
                SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter))
            ?.Syntax as ArgumentSyntax;

    private void AddClientContractEdge(
        EvidenceNode clientNode,
        ITypeSymbol contractType,
        string evidenceId,
        string ruleId)
    {
        var targets = contractType is INamedTypeSymbol named
            ? GetSourceTypeCandidates(named)
            : Array.Empty<SourceTypeInfo>();
        if (targets.Count == 1)
        {
            var target = targets[0];
            _builder.AddEdge(
                WcfVocabulary.EdgeKinds.ClientContract,
                clientNode.NodeId,
                target.Node.NodeId,
                null,
                [evidenceId],
                ResolutionBasis.Semantic,
                ResolutionQuality.Partial,
                SourceRelationshipDetails);
        }
        else
        {
            var quality = targets.Count == 0
                ? ResolutionQuality.Unresolved
                : ResolutionQuality.Ambiguous;
            _builder.AddEdge(
                WcfVocabulary.EdgeKinds.ClientContract,
                clientNode.NodeId,
                null,
                Display(contractType),
                [evidenceId],
                ResolutionBasis.Semantic,
                quality,
                targets.Count == 0
                    ? $"The {ruleId} contract type is unavailable as a selected source node."
                    : $"The {ruleId} contract type maps to multiple project-specific structural nodes; DomainLens did not guess a target.");
            _builder.AddDiagnostic(
                targets.Count == 0
                    ? WcfVocabulary.Diagnostics.GeneratedTypeUnavailable
                    : WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                DiagnosticSeverity.Warning,
                targets.Count == 0
                    ? "A WCF client contract type could not be correlated to a selected source declaration."
                    : "A WCF client contract type maps to multiple project-specific source declarations and remains ambiguous.",
                _builder.GetContributionEvidence(evidenceId).RelativePath,
                [evidenceId],
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["candidateCount"] = targets.Count.ToString(CultureInfo.InvariantCulture),
                    ["target"] = Display(contractType),
                });
        }
    }

    private void DiagnoseUnresolvedWcfAttributeSyntax()
    {
        foreach (var source in _context.SourceDocuments)
        {
            var model = _context.GetSemanticModel(source);
            var root = source.SyntaxTree.GetRoot(_cancellationToken);
            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                var shortName = AttributeShortName(attribute.Name);
                if (!AttributeRuleByShortName.TryGetValue(shortName, out var ruleId))
                {
                    continue;
                }

                var symbolInfo = model.GetSymbolInfo(attribute, _cancellationToken);
                var attributeType = symbolInfo.Symbol switch
                {
                    IMethodSymbol constructor => constructor.ContainingType,
                    INamedTypeSymbol named => named,
                    _ => null,
                };
                if (attributeType is not null)
                {
                    // A resolved repository-defined look-alike is intentionally ignored.
                    continue;
                }

                var quality = symbolInfo.CandidateReason == CandidateReason.Ambiguous ||
                              symbolInfo.CandidateSymbols.Length > 1
                    ? ResolutionQuality.Ambiguous
                    : ResolutionQuality.Unresolved;
                var evidenceId = AddSyntaxEvidence(
                    source,
                    attribute,
                    ruleId,
                    ResolutionBasis.Semantic,
                    quality,
                    "WCF-looking attribute syntax did not bind to a trusted framework attribute identity.");
                _builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.AttributeIdentityUnresolved,
                    DiagnosticSeverity.Warning,
                    quality == ResolutionQuality.Ambiguous
                        ? "WCF-looking attribute syntax had multiple semantic candidates and was not promoted to WCF evidence."
                        : "WCF-looking attribute syntax could not be bound to a trusted framework identity and was not promoted to WCF evidence.",
                    source.RelativePath,
                    [evidenceId],
                    new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["candidateCount"] = symbolInfo.CandidateSymbols.Length.ToString(CultureInfo.InvariantCulture),
                    });
            }
        }
    }

    private void DiagnoseUnsupportedOperationAttributes()
    {
        var operationAttribute = Framework(WcfVocabulary.FrameworkTypes.OperationContractAttribute);
        var faultAttribute = Framework(WcfVocabulary.FrameworkTypes.FaultContractAttribute);
        foreach (var source in _context.SourceDocuments)
        {
            var model = _context.GetSemanticModel(source);
            var root = source.SyntaxTree.GetRoot(_cancellationToken);
            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (model.GetSymbolInfo(attribute, _cancellationToken).Symbol is not IMethodSymbol constructor ||
                    attribute.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration ||
                    model.GetDeclaredSymbol(declaration, _cancellationToken) is not IMethodSymbol method ||
                    _operations.ContainsKey(method))
                {
                    continue;
                }

                string? ruleId = null;
                string? message = null;
                if (operationAttribute is not null &&
                    SymbolEqualityComparer.Default.Equals(constructor.ContainingType, operationAttribute))
                {
                    ruleId = WcfVocabulary.Rules.OperationContract;
                    message = "An OperationContract declaration is outside a uniquely selected service/callback contract surface and was not promoted.";
                }
                else if (faultAttribute is not null &&
                         SymbolEqualityComparer.Default.Equals(constructor.ContainingType, faultAttribute))
                {
                    ruleId = WcfVocabulary.Rules.FaultContract;
                    message = "A FaultContract declaration is outside a uniquely selected WCF operation surface and was not promoted.";
                }

                if (ruleId is null || message is null ||
                    !_unsupportedPlacementDiagnostics.Add(
                        $"{source.RelativePath}:{attribute.SpanStart}:{ruleId}"))
                {
                    continue;
                }

                var structuralCandidates = _nodeIndex.GetDeclarationNodes(
                    declaration,
                    "csharp.method-declaration");
                var quality = structuralCandidates.Count > 1
                    ? ResolutionQuality.Ambiguous
                    : ResolutionQuality.Partial;
                var evidenceId = AddSyntaxEvidence(
                    source,
                    attribute,
                    ruleId,
                    ResolutionBasis.Semantic,
                    quality,
                    structuralCandidates.Count > 1
                        ? "The trusted framework attribute identity was established, but the declaration maps to multiple project-specific method nodes."
                        : "The trusted framework attribute identity was established, but the declaration is outside the bounded operation surface.");
                _builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
                    DiagnosticSeverity.Warning,
                    message,
                    source.RelativePath,
                    [evidenceId]);
            }
        }
    }

    private void DiagnoseFrameworkProfiles()
    {
        foreach (var source in _profileEvidence.Values
                     .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                     .ThenBy(item => item.Node.NodeId, StringComparer.Ordinal))
        {
            var projectId = source.Node.ProjectId;
            var assessment = _frameworkProfilePolicy.AssessProjectContext(
                source.RelativePath,
                projectId);
            if (assessment.AllowsExactSourceObservation)
            {
                continue;
            }

            var diagnosticKey = projectId ?? $"<unassigned>:{source.RelativePath}";
            if (!_profileDiagnostics.Add(diagnosticKey))
            {
                continue;
            }

            _builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.UnsupportedFrameworkProfile,
                DiagnosticSeverity.Warning,
                "WCF semantic enrichment uses only the trusted net472 profile; the selected source project declared another, multiple, conditional, or unknown target profile.",
                source.RelativePath,
                properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["declaredTargetFrameworks"] = assessment.DeclaredTargetFrameworks,
                    ["profileCompatibility"] = assessment.Compatibility.ToString(),
                    ["trustedReferenceSet"] = _context.ReferenceSetId,
                });
        }
    }

    private void TrackProfile(EvidenceNode node, string relativePath)
    {
        if (_profileEvidence.TryGetValue(node.NodeId, out var existing) &&
            string.CompareOrdinal(existing.RelativePath, relativePath) <= 0)
        {
            return;
        }

        _profileEvidence[node.NodeId] = new ProfileEvidenceSource(node, relativePath);
    }

    private IReadOnlyList<SourceTypeInfo> GetSourceTypeCandidates(INamedTypeSymbol symbol)
    {
        if (_sourceTypes.TryGetValue(symbol, out var sourceType))
        {
            return [sourceType];
        }

        return _ambiguousTypes.TryGetValue(symbol, out var ambiguous)
            ? ambiguous
            : [];
    }

    private IReadOnlyList<SourceTypeInfo> GetServiceContractCandidates(INamedTypeSymbol symbol)
    {
        if (_contracts.ContainsKey(symbol) && _sourceTypes.TryGetValue(symbol, out var sourceType))
        {
            return [sourceType];
        }

        return _ambiguousServiceContracts.TryGetValue(symbol, out var ambiguous)
            ? ambiguous
            : [];
    }

    private IReadOnlyList<INamedTypeSymbol> GetInterfacesForDeclaration(SourceTypeInfo sourceType)
    {
        if (sourceType.Declaration.BaseList is null)
        {
            return [];
        }

        var model = _context.GetSemanticModel(sourceType.Source);
        var interfaces = new List<INamedTypeSymbol>();
        foreach (var baseTypeSyntax in sourceType.Declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseTypeSyntax.Type, _cancellationToken).Type is not INamedTypeSymbol baseType)
            {
                continue;
            }

            if (baseType.TypeKind == TypeKind.Interface)
            {
                interfaces.Add(baseType);
            }

            interfaces.AddRange(baseType.AllInterfaces);
        }

        return interfaces
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(Display, StringComparer.Ordinal)
            .ToArray();
    }

    private static SortedDictionary<string, string> ServiceContractProperties(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        string? configurationName)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [WcfVocabulary.Properties.ContractClrIdentity] = Display(symbol),
        };
        AddIfNotNull(
            properties,
            WcfVocabulary.Properties.ServiceContractName,
            GetNamedString(attribute, "Name"));
        AddIfNotNull(
            properties,
            WcfVocabulary.Properties.ServiceContractNamespace,
            GetNamedString(attribute, "Namespace"));
        AddIfNotNull(
            properties,
            WcfVocabulary.Properties.ServiceContractConfigurationName,
            configurationName);
        return properties;
    }

    private void ReportUnsupportedAttributePlacement(
        SourceTypeInfo type,
        AttributeData attribute,
        string ruleId,
        string message) =>
        ReportUnsupportedAttributePlacement(type.Source, attribute, ruleId, message);

    private void ReportUnsupportedAttributePlacement(
        LegacySemanticSourceDocument source,
        AttributeData attribute,
        string ruleId,
        string message)
    {
        var evidenceId = AddAttributeEvidence(
            source,
            attribute,
            ruleId,
            ResolutionQuality.Partial,
            "The trusted framework attribute identity was established, but its source placement is outside the bounded supported rule.");
        _builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.UnsupportedSourcePattern,
            DiagnosticSeverity.Warning,
            message,
            source.RelativePath,
            [evidenceId]);
    }

    private string AddAttributeEvidence(
        LegacySemanticSourceDocument source,
        AttributeData attribute,
        string ruleId,
        ResolutionQuality quality = ResolutionQuality.Exact,
        string? details = null)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(_cancellationToken) is not AttributeSyntax syntax)
        {
            throw new InvalidOperationException("A source WCF attribute has no source syntax reference.");
        }

        if (_context.TryGetSourceDocument(syntax.SyntaxTree.FilePath, out var actualSource) &&
            actualSource is not null)
        {
            source = actualSource;
        }

        var evidenceId = AddSyntaxEvidence(
            source,
            syntax,
            ruleId,
            ResolutionBasis.Semantic,
            quality,
            details ?? "The attribute type matched the pinned tool-owned framework metadata identity.");
        var hasUnsupportedValue = attribute.ConstructorArguments.Any(HasErrorValue) ||
                                  attribute.NamedArguments.Any(pair => HasErrorValue(pair.Value));
        var diagnosticKey = $"{source.RelativePath}:{syntax.SpanStart}:{syntax.Span.Length}";
        if (hasUnsupportedValue && _unsupportedAttributeDiagnostics.Add(diagnosticKey))
        {
            _builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.UnsupportedAttributeValue,
                DiagnosticSeverity.Warning,
                "A recognized WCF attribute contained a value the controlled compiler could not establish as a supported static constant; that value was omitted.",
                source.RelativePath,
                [evidenceId]);
        }

        return evidenceId;
    }

    private string AddDeclarationEvidence(
        LegacySemanticSourceDocument source,
        ISymbol symbol,
        string ruleId)
    {
        var syntax = symbol.DeclaringSyntaxReferences
            .OrderBy(item => item.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Span.Start)
            .Select(item => item.GetSyntax(_cancellationToken))
            .First();
        return AddSyntaxEvidence(
            source,
            syntax,
            ruleId,
            ResolutionBasis.Semantic,
            ResolutionQuality.Partial,
            SourceRelationshipDetails);
    }

    private string AddSyntaxEvidence(
        LegacySemanticSourceDocument source,
        SyntaxNode syntax,
        string ruleId,
        ResolutionBasis basis,
        ResolutionQuality quality,
        string? details)
    {
        var assessment = _frameworkProfilePolicy.AssessObservation(source.RelativePath);
        var constrainedQuality = WcfFrameworkProfilePolicy.Constrain(quality, assessment);
        if (constrainedQuality != quality)
        {
            details = string.IsNullOrWhiteSpace(details)
                ? assessment.QualityDetails
                : $"{details} {assessment.QualityDetails}";
        }

        return _builder.AddEvidence(
            source.RelativePath,
            LegacySemanticCompilationContext.ToSourceSpan(source.SyntaxTree, syntax.Span),
            ruleId,
            basis,
            constrainedQuality,
            details);
    }

    private bool TryGetSource(ISymbol symbol, out LegacySemanticSourceDocument source)
    {
        var path = symbol.Locations.FirstOrDefault(item => item.IsInSource)?.SourceTree?.FilePath;
        if (_context.TryGetSourceDocument(path ?? string.Empty, out var found) && found is not null)
        {
            source = found;
            return true;
        }

        source = null!;
        return false;
    }

    private string? GetContainingProjectId(SemanticModel model, SyntaxNode syntax)
    {
        var containingType = model.GetEnclosingSymbol(syntax.SpanStart, _cancellationToken)?.ContainingType;
        return containingType is not null && _sourceTypes.TryGetValue(containingType, out var sourceType)
            ? sourceType.Node.ProjectId
            : null;
    }

    private static INamedTypeSymbol? FindConstructedBase(
        INamedTypeSymbol source,
        INamedTypeSymbol? originalDefinition)
    {
        if (originalDefinition is null)
        {
            return null;
        }

        for (var current = source.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, originalDefinition))
            {
                return current;
            }
        }

        return null;
    }

    private INamedTypeSymbol? FindConstructedBase(
        SourceTypeInfo source,
        INamedTypeSymbol? originalDefinition)
    {
        if (originalDefinition is null || source.Declaration.BaseList is null)
        {
            return null;
        }

        var model = _context.GetSemanticModel(source.Source);
        foreach (var baseTypeSyntax in source.Declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseTypeSyntax.Type, _cancellationToken).Type is not INamedTypeSymbol baseType)
            {
                continue;
            }

            for (var current = baseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, originalDefinition))
                {
                    return current;
                }
            }
        }

        return null;
    }

    private INamedTypeSymbol? Framework(string metadataName) => _framework[metadataName];

    private AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        FindAttributes(symbol, metadataName).FirstOrDefault();

    private AttributeData? FindAttributeForDeclaration(
        SourceTypeInfo sourceType,
        string metadataName) =>
        FindAttributes(sourceType.Symbol, metadataName)
            .FirstOrDefault(attribute =>
                attribute.ApplicationSyntaxReference is { } reference &&
                ReferenceEquals(reference.SyntaxTree, sourceType.Declaration.SyntaxTree) &&
                sourceType.Declaration.Span.Contains(reference.Span));

    private IEnumerable<AttributeData> FindAttributes(ISymbol symbol, string metadataName)
    {
        var frameworkType = Framework(metadataName);
        return frameworkType is null
            ? []
            : symbol.GetAttributes().Where(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, frameworkType));
    }

    private static INamedTypeSymbol? GetConstructorType(AttributeData attribute, int index)
    {
        if (index >= attribute.ConstructorArguments.Length)
        {
            return null;
        }

        var argument = attribute.ConstructorArguments[index];
        return argument.Kind == TypedConstantKind.Type ? argument.Value as INamedTypeSymbol : null;
    }

    private static INamedTypeSymbol? GetNamedType(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal) &&
                pair.Value.Kind == TypedConstantKind.Type)
            {
                return pair.Value.Value as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal) &&
                pair.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetNamedValue(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (!string.Equals(pair.Key, name, StringComparison.Ordinal) || pair.Value.Value is null)
            {
                continue;
            }

            return pair.Value.Value switch
            {
                bool value => value ? "true" : "false",
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                _ => pair.Value.Value.ToString(),
            };
        }

        return null;
    }

    private static bool HasErrorValue(TypedConstant value) =>
        value.Kind == TypedConstantKind.Error ||
        (value.Kind == TypedConstantKind.Array && value.Values.Any(HasErrorValue));

    private static SortedDictionary<string, string> MessageMemberProperties(
        AttributeData attribute,
        string nameProperty,
        string namespaceProperty,
        string? orderProperty)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddIfNotNull(properties, nameProperty, GetNamedString(attribute, "Name"));
        AddIfNotNull(properties, namespaceProperty, GetNamedString(attribute, "Namespace"));
        if (orderProperty is not null)
        {
            AddIfNotNull(properties, orderProperty, GetNamedValue(attribute, "Order"));
        }

        return properties;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var nested in EnumerateNamedTypes(array.ElementType))
                {
                    yield return nested;
                }

                break;
            case INamedTypeSymbol named:
                yield return named;
                foreach (var argument in named.TypeArguments)
                {
                    foreach (var nested in EnumerateNamedTypes(argument))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static void AddIfNotNull(
        IDictionary<string, string> properties,
        string key,
        string? value)
    {
        if (value is not null)
        {
            properties[key] = value;
        }
    }

    private static string AttributeShortName(NameSyntax name)
    {
        var text = name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => name.ToString().Split('.').Last(),
        };
        return text.EndsWith("Attribute", StringComparison.Ordinal)
            ? text[..^"Attribute".Length]
            : text;
    }

    private static int Line(SyntaxNode syntax) =>
        syntax.SyntaxTree.GetLineSpan(syntax.Span).StartLinePosition.Line + 1;

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(DisplayFormat);

    private static readonly string[] FrameworkMetadataNames =
    [
        WcfVocabulary.FrameworkTypes.ServiceContractAttribute,
        WcfVocabulary.FrameworkTypes.OperationContractAttribute,
        WcfVocabulary.FrameworkTypes.FaultContractAttribute,
        WcfVocabulary.FrameworkTypes.DataContractAttribute,
        WcfVocabulary.FrameworkTypes.DataMemberAttribute,
        WcfVocabulary.FrameworkTypes.MessageContractAttribute,
        WcfVocabulary.FrameworkTypes.MessageHeaderAttribute,
        WcfVocabulary.FrameworkTypes.MessageBodyMemberAttribute,
        WcfVocabulary.FrameworkTypes.ServiceHost,
        WcfVocabulary.FrameworkTypes.ChannelFactory,
        WcfVocabulary.FrameworkTypes.ClientBase,
        WcfVocabulary.FrameworkTypes.EndpointAddress,
        WcfVocabulary.FrameworkTypes.CompilerGeneratedAttribute,
        WcfVocabulary.FrameworkTypes.GeneratedCodeAttribute,
    ];

    private static readonly IReadOnlyDictionary<string, string> AttributeRuleByShortName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ServiceContract"] = WcfVocabulary.Rules.ServiceContract,
            ["OperationContract"] = WcfVocabulary.Rules.OperationContract,
            ["FaultContract"] = WcfVocabulary.Rules.FaultContract,
            ["DataContract"] = WcfVocabulary.Rules.DataContract,
            ["DataMember"] = WcfVocabulary.Rules.DataMember,
            ["MessageContract"] = WcfVocabulary.Rules.MessageContract,
            ["MessageHeader"] = WcfVocabulary.Rules.MessageHeader,
            ["MessageBodyMember"] = WcfVocabulary.Rules.MessageBodyMember,
        };

    private sealed record SourceTypeInfo(
        INamedTypeSymbol Symbol,
        EvidenceNode Node,
        LegacySemanticSourceDocument Source,
        BaseTypeDeclarationSyntax Declaration);

    private sealed record ContractInfo(
        INamedTypeSymbol Symbol,
        EvidenceNode Node,
        LegacySemanticSourceDocument Source,
        AttributeData Attribute,
        string EvidenceId,
        string? ConfigurationName);

    private sealed record OperationInfo(
        IMethodSymbol Symbol,
        EvidenceNode Node,
        LegacySemanticSourceDocument Source,
        AttributeData Attribute,
        string EvidenceId);

    private sealed record ProfileEvidenceSource(
        EvidenceNode Node,
        string RelativePath);
}
