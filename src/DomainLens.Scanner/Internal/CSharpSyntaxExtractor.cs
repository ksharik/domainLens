using System.Globalization;
using System.Text;
using DomainLens.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DomainLens.Scanner.Internal;

internal sealed class CSharpSyntaxExtractor
{
    private const string ExtractorId = "domainlens.csharp-syntax";
    private const string ExtractorVersion = "0.1.0";
    private const string PartialSourceMembershipDetails =
        "Project source membership is partial because MSBuild item/property evaluation was not performed.";
    private const string ConditionalCompilationDetails =
        "C# conditional compilation symbols were not established from MSBuild evaluation.";

    public void Analyze(
        RepositoryInventory inventory,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyDictionary<string, string> projectKeys,
        EvidenceGraphBuilder graph,
        List<ScannerIssue> issues)
    {
        var parsedFiles = ParseFiles(inventory, projects, projectKeys, issues);
        var types = DiscoverTypes(parsedFiles, graph);
        var accessibleProjectKeys = BuildAccessibleProjectKeys(inventory, projects, projectKeys);
        var index = new TypeIndex(types, parsedFiles, accessibleProjectKeys);

        foreach (var type in types.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AddBaseRelationships(type, index, graph);
            AddMembers(type, index, graph);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildAccessibleProjectKeys(
        RepositoryInventory inventory,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyDictionary<string, string> projectKeys)
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            if (!projectKeys.TryGetValue(project.RelativePath, out var sourceProjectKey) ||
                !inventory.ByRelativePath.TryGetValue(project.RelativePath, out var projectFile))
            {
                continue;
            }

            var accessible = new HashSet<string>(StringComparer.Ordinal) { sourceProjectKey };
            if (!project.DependencySelectionPartial)
            {
                foreach (var reference in project.ProjectReferences.Where(reference =>
                             !reference.IsConditional && AllowsGlobalCompileReference(reference)))
                {
                    if (!IsLiteralProjectReference(reference.Include) ||
                        !PathSafety.TryResolveWithinRoot(
                            inventory.RootPath,
                            Path.GetDirectoryName(projectFile.FullPath)!,
                            reference.Include,
                            out _,
                            out var targetPath) ||
                        !projectKeys.TryGetValue(targetPath, out var targetProjectKey))
                    {
                        continue;
                    }

                    accessible.Add(targetProjectKey);
                }
            }

            result.Add(sourceProjectKey, accessible);
        }

        return result;
    }

    private static bool AllowsGlobalCompileReference(ProjectItemReference reference)
    {
        if (reference.ReferenceOutputAssembly is not null &&
            !string.Equals(reference.ReferenceOutputAssembly, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(reference.Aliases))
        {
            return true;
        }

        return reference.Aliases
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("global", StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLiteralProjectReference(string include) =>
        !string.IsNullOrWhiteSpace(include) &&
        !include.Contains('*') &&
        !include.Contains('?') &&
        !include.Contains("$(", StringComparison.Ordinal);

    private static IReadOnlyList<ParsedSourceFile> ParseFiles(
        RepositoryInventory inventory,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyDictionary<string, string> projectKeys,
        List<ScannerIssue> issues)
    {
        var files = new List<ParsedSourceFile>();
        foreach (var project in projects.Where(item => item.ParsedSuccessfully)
                     .OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            foreach (var sourcePath in project.SourceRelativePaths.OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!inventory.ByRelativePath.TryGetValue(sourcePath, out var sourceFile) ||
                    sourceFile.CapturedContent is null)
                {
                    issues.Add(new ScannerIssue(
                        "DL3001",
                        ScannerIssueSeverity.Warning,
                        "A selected C# source file was unavailable in the immutable snapshot.",
                        sourcePath));
                    continue;
                }

                var text = sourceFile.ReadCapturedText();
                var sourceText = SourceText.From(text, Encoding.UTF8, SourceHashAlgorithm.Sha256);
                var parseOptions = new CSharpParseOptions(
                    LanguageVersion.Preview,
                    DocumentationMode.Parse,
                    SourceCodeKind.Regular);
                var tree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, sourcePath);
                var root = tree.GetCompilationUnitRoot();

                foreach (var diagnostic in tree.GetDiagnostics().Where(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                {
                    var location = diagnostic.Location.GetLineSpan().StartLinePosition;
                    issues.Add(new ScannerIssue(
                        "DL3002",
                        ScannerIssueSeverity.Warning,
                        $"C# syntax could only be partially parsed: {diagnostic.Id}: {Sanitize(diagnostic.GetMessage(CultureInfo.InvariantCulture))}",
                        sourcePath,
                        location.Line + 1,
                        location.Character + 1));
                }

                var hasConditionalDirectives = root.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                    trivia.IsDirective && trivia.GetStructure() is ConditionalDirectiveTriviaSyntax);
                if (hasConditionalDirectives)
                {
                    issues.Add(new ScannerIssue(
                        "DL3003",
                        ScannerIssueSeverity.Warning,
                        "Conditional compilation was parsed without evaluating project-defined symbols.",
                        sourcePath));
                }

                files.Add(new ParsedSourceFile(
                    project,
                    projectKeys[project.RelativePath],
                    sourceFile,
                    tree,
                    root,
                    hasConditionalDirectives));
            }
        }

        return files;
    }

    private static IReadOnlyList<TypeObservation> DiscoverTypes(
        IReadOnlyList<ParsedSourceFile> parsedFiles,
        EvidenceGraphBuilder graph)
    {
        var observations = new Dictionary<string, TypeObservation>(StringComparer.Ordinal);

        foreach (var file in parsedFiles)
        {
            foreach (var namespaceDeclaration in file.Root.DescendantNodes()
                         .OfType<BaseNamespaceDeclarationSyntax>()
                         .Where(declaration => HasUsableName(declaration.Name)))
            {
                var qualifiedNamespace = GetQualifiedNamespace(namespaceDeclaration);
                var namespaceKey = NamespaceKey(file.ProjectKey, qualifiedNamespace);
                var declarationEvidenceId = AddSyntaxEvidence(
                    graph,
                    file,
                    namespaceDeclaration.Name,
                    "csharp.namespace-declaration",
                    ResolutionQuality.Exact);
                var membershipQuality = GetProjectMembershipQuality(file);
                var membershipEvidenceId = AddSyntaxEvidence(
                    graph,
                    file,
                    namespaceDeclaration.Name,
                    "csharp.project-source-membership",
                    membershipQuality,
                    GetProjectMembershipDetails(file),
                    ResolutionBasis.DeclarativeConfiguration);
                graph.AddNode(
                    namespaceKey,
                    "Namespace",
                    qualifiedNamespace,
                    qualifiedNamespace,
                    file.ProjectKey,
                    new[] { declarationEvidenceId },
                    properties: GetProjectMembershipProperties(file));
                graph.AddEdge(
                    "Contains",
                    file.ProjectKey,
                    namespaceKey,
                    null,
                    new[] { membershipEvidenceId },
                    ResolutionBasis.DeclarativeConfiguration,
                    membershipQuality,
                    GetProjectMembershipDetails(file));
            }

            var typeDeclarations = file.Root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Where(declaration => HasUsableIdentifier(declaration.Identifier));
            foreach (var declaration in typeDeclarations)
            {
                var observation = CreateTypeObservation(file, declaration);
                AddOrMergeType(observations, observation, graph);
            }

            foreach (var declaration in file.Root.DescendantNodes()
                         .OfType<DelegateDeclarationSyntax>()
                         .Where(declaration => HasUsableIdentifier(declaration.Identifier)))
            {
                var observation = CreateTypeObservation(file, declaration);
                AddOrMergeType(observations, observation, graph);
            }
        }

        return observations.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
    }

    private static void AddOrMergeType(
        IDictionary<string, TypeObservation> observations,
        TypeDeclarationPart part,
        EvidenceGraphBuilder graph)
    {
        var declarationEvidence = AddSyntaxEvidence(
            graph,
            part.File,
            part.Declaration,
            "csharp.type-declaration",
            ResolutionQuality.Exact);
        var (attributeNames, attributeEvidence) = AddAttributeEvidence(
            graph,
            part.File,
            part.AttributeLists,
            "csharp.type-attribute");
        var evidence = new[] { declarationEvidence }.Concat(attributeEvidence).ToArray();

        if (!observations.TryGetValue(part.Key, out var observation))
        {
            observation = new TypeObservation(
                part.Key,
                part.ProjectKey,
                part.Kind,
                part.Name,
                part.QualifiedName,
                part.MetadataName,
                part.Namespace,
                part.ContainingTypeKey,
                new List<TypeDeclarationPart>());
            observations.Add(part.Key, observation);
        }

        observation.Parts.Add(part);
        graph.AddNode(
            part.Key,
            part.Kind,
            part.Name,
            part.QualifiedName,
            part.ProjectKey,
            evidence,
            attributeNames,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["metadataName"] = part.MetadataName,
                ["namespace"] = part.Namespace,
                ["modifiers"] = NormalizeTokens(part.Modifiers),
                ["projectMembershipResolution"] = GetProjectMembershipQuality(part.File).ToString().ToLowerInvariant(),
                ["targetFrameworks"] = string.Join(';', part.File.Project.TargetFrameworks),
            });

        var containerKey = part.ContainingTypeKey ?? NamespaceKey(part.ProjectKey, part.Namespace);
        if (part.ContainingTypeKey is null && part.Namespace == "<global>")
        {
            var membershipQuality = GetProjectMembershipQuality(part.File);
            var membershipEvidenceId = AddSyntaxEvidence(
                graph,
                part.File,
                part.Declaration,
                "csharp.project-source-membership",
                membershipQuality,
                GetProjectMembershipDetails(part.File),
                ResolutionBasis.DeclarativeConfiguration);
            graph.AddNode(
                containerKey,
                "Namespace",
                "<global>",
                "<global>",
                part.ProjectKey,
                evidence,
                properties: GetProjectMembershipProperties(part.File));
            graph.AddEdge(
                "Contains",
                part.ProjectKey,
                containerKey,
                null,
                new[] { membershipEvidenceId },
                ResolutionBasis.DeclarativeConfiguration,
                membershipQuality,
                GetProjectMembershipDetails(part.File));
        }

        graph.AddEdge(
            "Contains",
            containerKey,
            part.Key,
            null,
            evidence,
            ResolutionBasis.Syntax,
            GetSyntaxObservationQuality(part.File),
            GetSyntaxObservationDetails(part.File));
    }

    private static void AddBaseRelationships(
        TypeObservation type,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        foreach (var part in type.Parts)
        {
            if (part.Declaration is not BaseTypeDeclarationSyntax declaration || declaration.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in declaration.BaseList.Types)
            {
                var evidenceId = AddSyntaxEvidence(
                    graph,
                    part.File,
                    baseType.Type,
                    "csharp.base-type",
                    ResolutionQuality.Exact);
                var resolution = index.Resolve(baseType.Type, type, part.File);
                if (resolution.Target is null)
                {
                    graph.AddEdge(
                        "DeclaredTypeDependency",
                        type.Key,
                        null,
                        NormalizeType(baseType.Type),
                        new[] { evidenceId },
                        ResolutionBasis.Syntax,
                        resolution.Quality,
                        resolution.Details);
                    continue;
                }

                var edgeKind = DetermineBaseEdgeKind(type.Kind, resolution.Target.Kind);
                graph.AddEdge(
                    edgeKind,
                    type.Key,
                    resolution.Target.Key,
                    null,
                    new[] { evidenceId },
                    ResolutionBasis.Syntax,
                    GetResolvedRelationshipQuality(part.File, resolution.Target),
                    GetResolvedRelationshipDetails(part.File, resolution.Target));
            }
        }
    }

    private static string DetermineBaseEdgeKind(string sourceKind, string targetKind)
    {
        if (sourceKind == "Interface")
        {
            return "Inherits";
        }

        if (targetKind == "Interface")
        {
            return "Implements";
        }

        return sourceKind is "Struct" or "RecordStruct"
            ? "Implements"
            : "Inherits";
    }

    private static void AddMembers(
        TypeObservation type,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        foreach (var part in type.Parts)
        {
            switch (part.Declaration)
            {
                case TypeDeclarationSyntax declaration:
                    if (declaration is RecordDeclarationSyntax recordDeclaration && recordDeclaration.ParameterList is not null)
                    {
                        AddRecordPrimaryConstructor(type, part, recordDeclaration, index, graph);
                    }

                    foreach (var member in declaration.Members)
                    {
                        AddMember(type, part.File, member, index, graph);
                    }

                    break;

                case EnumDeclarationSyntax enumDeclaration:
                    foreach (var member in enumDeclaration.Members)
                    {
                        AddEnumMember(type, part.File, member, graph);
                    }

                    break;

                case DelegateDeclarationSyntax delegateDeclaration:
                    AddDelegateDependencies(type, part.File, delegateDeclaration, index, graph);
                    break;
            }
        }
    }

    private static void AddMember(
        TypeObservation owner,
        ParsedSourceFile file,
        MemberDeclarationSyntax member,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        if (!HasUsableMemberIdentity(member))
        {
            return;
        }

        switch (member)
        {
            case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                return;

            case MethodDeclarationSyntax method:
                AddMethod(owner, file, method, index, graph);
                return;

            case ConstructorDeclarationSyntax constructor:
                AddConstructor(owner, file, constructor, index, graph);
                return;

            case DestructorDeclarationSyntax destructor:
                AddSimpleMember(owner, file, destructor, "Destructor", $"~{owner.Name}()", null, null, graph);
                return;

            case PropertyDeclarationSyntax property:
                AddProperty(owner, file, property, index, graph);
                return;

            case IndexerDeclarationSyntax indexer:
                AddIndexer(owner, file, indexer, index, graph);
                return;

            case FieldDeclarationSyntax field:
                AddFields(owner, file, field, index, graph);
                return;

            case EventFieldDeclarationSyntax eventField:
                AddEventFields(owner, file, eventField, index, graph);
                return;

            case EventDeclarationSyntax eventDeclaration:
                AddEvent(owner, file, eventDeclaration, index, graph);
                return;

            case OperatorDeclarationSyntax operatorDeclaration:
                AddOperator(owner, file, operatorDeclaration, index, graph);
                return;

            case ConversionOperatorDeclarationSyntax conversion:
                AddConversion(owner, file, conversion, index, graph);
                return;
        }
    }

    private static void AddMethod(
        TypeObservation owner,
        ParsedSourceFile file,
        MethodDeclarationSyntax method,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var methodName = method.ExplicitInterfaceSpecifier is null
            ? method.Identifier.ValueText
            : $"{NormalizeName(method.ExplicitInterfaceSpecifier.Name)}.{method.Identifier.ValueText}";
        var genericArity = method.TypeParameterList?.Parameters.Count ?? 0;
        var signature = $"{methodName}{AritySuffix(genericArity)}({ParameterSignature(method.ParameterList.Parameters)})";
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["signature"] = signature,
            ["returnType"] = NormalizeType(method.ReturnType),
            ["modifiers"] = NormalizeTokens(method.Modifiers),
        };
        var memberKey = MemberKey(owner.Key, "Method", signature);
        AddSimpleMember(owner, file, method, "Method", method.Identifier.ValueText, signature, properties, graph, memberKey);
        AddTypeDependencies(memberKey, owner, file, method.ReturnType, index, graph, "csharp.method-return-type");
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, file, parameter.Type, index, graph, "csharp.method-parameter-type");
            }
        }
    }

    private static void AddConstructor(
        TypeObservation owner,
        ParsedSourceFile file,
        ConstructorDeclarationSyntax constructor,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var isStatic = constructor.Modifiers.Any(SyntaxKind.StaticKeyword);
        var signature = isStatic
            ? ".cctor()"
            : $".ctor({ParameterSignature(constructor.ParameterList.Parameters)})";
        var memberKey = MemberKey(owner.Key, "Constructor", signature);
        AddSimpleMember(
            owner,
            file,
            constructor,
            "Constructor",
            owner.Name,
            signature,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["signature"] = signature,
                ["modifiers"] = NormalizeTokens(constructor.Modifiers),
            },
            graph,
            memberKey);
        foreach (var parameter in constructor.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, file, parameter.Type, index, graph, "csharp.constructor-parameter-type");
            }
        }
    }

    private static void AddRecordPrimaryConstructor(
        TypeObservation owner,
        TypeDeclarationPart part,
        RecordDeclarationSyntax declaration,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var parameters = declaration.ParameterList!.Parameters;
        var signature = $".ctor({ParameterSignature(parameters)})";
        var memberKey = MemberKey(owner.Key, "Constructor", signature);
        AddSimpleMember(
            owner,
            part.File,
            declaration.ParameterList,
            "Constructor",
            owner.Name,
            signature,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["signature"] = signature,
                ["primary"] = "true",
            },
            graph,
            memberKey);
        foreach (var parameter in parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, part.File, parameter.Type, index, graph, "csharp.constructor-parameter-type");
            }
        }
    }

    private static void AddProperty(
        TypeObservation owner,
        ParsedSourceFile file,
        PropertyDeclarationSyntax property,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var name = property.ExplicitInterfaceSpecifier is null
            ? property.Identifier.ValueText
            : $"{NormalizeName(property.ExplicitInterfaceSpecifier.Name)}.{property.Identifier.ValueText}";
        var signature = $"{name}:{NormalizeType(property.Type)}";
        var memberKey = MemberKey(owner.Key, "Property", signature);
        AddSimpleMember(
            owner,
            file,
            property,
            "Property",
            property.Identifier.ValueText,
            signature,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["signature"] = signature,
                ["type"] = NormalizeType(property.Type),
                ["modifiers"] = NormalizeTokens(property.Modifiers),
            },
            graph,
            memberKey);
        AddTypeDependencies(memberKey, owner, file, property.Type, index, graph, "csharp.property-type");
    }

    private static void AddIndexer(
        TypeObservation owner,
        ParsedSourceFile file,
        IndexerDeclarationSyntax indexer,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var indexerName = indexer.ExplicitInterfaceSpecifier is null
            ? "this"
            : $"{NormalizeName(indexer.ExplicitInterfaceSpecifier.Name)}.this";
        var signature = $"{indexerName}[{ParameterSignature(indexer.ParameterList.Parameters)}]:{NormalizeType(indexer.Type)}";
        var memberKey = MemberKey(owner.Key, "Indexer", signature);
        AddSimpleMember(
            owner,
            file,
            indexer,
            "Indexer",
            "this[]",
            signature,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["signature"] = signature,
                ["type"] = NormalizeType(indexer.Type),
                ["modifiers"] = NormalizeTokens(indexer.Modifiers),
            },
            graph,
            memberKey);
        AddTypeDependencies(memberKey, owner, file, indexer.Type, index, graph, "csharp.indexer-type");
        foreach (var parameter in indexer.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, file, parameter.Type, index, graph, "csharp.indexer-parameter-type");
            }
        }
    }

    private static void AddFields(
        TypeObservation owner,
        ParsedSourceFile file,
        FieldDeclarationSyntax field,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            if (!HasUsableIdentifier(variable.Identifier))
            {
                continue;
            }

            var signature = $"{variable.Identifier.ValueText}:{NormalizeType(field.Declaration.Type)}";
            var memberKey = MemberKey(owner.Key, "Field", signature);
            AddSimpleMember(
                owner,
                file,
                variable,
                "Field",
                variable.Identifier.ValueText,
                signature,
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["signature"] = signature,
                    ["type"] = NormalizeType(field.Declaration.Type),
                    ["modifiers"] = NormalizeTokens(field.Modifiers),
                },
                graph,
                memberKey,
                field.AttributeLists);
            AddTypeDependencies(memberKey, owner, file, field.Declaration.Type, index, graph, "csharp.field-type");
        }
    }

    private static void AddEventFields(
        TypeObservation owner,
        ParsedSourceFile file,
        EventFieldDeclarationSyntax field,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            if (!HasUsableIdentifier(variable.Identifier))
            {
                continue;
            }

            var signature = $"{variable.Identifier.ValueText}:{NormalizeType(field.Declaration.Type)}";
            var memberKey = MemberKey(owner.Key, "Event", signature);
            AddSimpleMember(
                owner,
                file,
                variable,
                "Event",
                variable.Identifier.ValueText,
                signature,
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["signature"] = signature,
                    ["type"] = NormalizeType(field.Declaration.Type),
                    ["modifiers"] = NormalizeTokens(field.Modifiers),
                },
                graph,
                memberKey,
                field.AttributeLists);
            AddTypeDependencies(memberKey, owner, file, field.Declaration.Type, index, graph, "csharp.event-type");
        }
    }

    private static void AddEvent(
        TypeObservation owner,
        ParsedSourceFile file,
        EventDeclarationSyntax eventDeclaration,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var eventName = eventDeclaration.ExplicitInterfaceSpecifier is null
            ? eventDeclaration.Identifier.ValueText
            : $"{NormalizeName(eventDeclaration.ExplicitInterfaceSpecifier.Name)}.{eventDeclaration.Identifier.ValueText}";
        var signature = $"{eventName}:{NormalizeType(eventDeclaration.Type)}";
        var memberKey = MemberKey(owner.Key, "Event", signature);
        AddSimpleMember(
            owner,
            file,
            eventDeclaration,
            "Event",
            eventDeclaration.Identifier.ValueText,
            signature,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["signature"] = signature,
                ["type"] = NormalizeType(eventDeclaration.Type),
                ["modifiers"] = NormalizeTokens(eventDeclaration.Modifiers),
            },
            graph,
            memberKey);
        AddTypeDependencies(memberKey, owner, file, eventDeclaration.Type, index, graph, "csharp.event-type");
    }

    private static void AddOperator(
        TypeObservation owner,
        ParsedSourceFile file,
        OperatorDeclarationSyntax operation,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var checkedModifier = operation.CheckedKeyword.IsKind(SyntaxKind.None) ? string.Empty : "checked ";
        var signature = $"{checkedModifier}operator {operation.OperatorToken.Text}({ParameterSignature(operation.ParameterList.Parameters)}):{NormalizeType(operation.ReturnType)}";
        var memberKey = MemberKey(owner.Key, "Operator", signature);
        AddSimpleMember(owner, file, operation, "Operator", operation.OperatorToken.Text, signature, null, graph, memberKey);
        AddTypeDependencies(memberKey, owner, file, operation.ReturnType, index, graph, "csharp.operator-return-type");
        foreach (var parameter in operation.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, file, parameter.Type, index, graph, "csharp.operator-parameter-type");
            }
        }
    }

    private static void AddConversion(
        TypeObservation owner,
        ParsedSourceFile file,
        ConversionOperatorDeclarationSyntax conversion,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        var checkedModifier = conversion.CheckedKeyword.IsKind(SyntaxKind.None) ? string.Empty : " checked";
        var signature = $"{conversion.ImplicitOrExplicitKeyword.Text}{checkedModifier} operator {NormalizeType(conversion.Type)}({ParameterSignature(conversion.ParameterList.Parameters)})";
        var memberKey = MemberKey(owner.Key, "ConversionOperator", signature);
        AddSimpleMember(owner, file, conversion, "ConversionOperator", "operator", signature, null, graph, memberKey);
        AddTypeDependencies(memberKey, owner, file, conversion.Type, index, graph, "csharp.conversion-type");
        foreach (var parameter in conversion.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(memberKey, owner, file, parameter.Type, index, graph, "csharp.conversion-parameter-type");
            }
        }
    }

    private static void AddEnumMember(
        TypeObservation owner,
        ParsedSourceFile file,
        EnumMemberDeclarationSyntax member,
        EvidenceGraphBuilder graph)
    {
        if (!HasUsableIdentifier(member.Identifier))
        {
            return;
        }

        AddSimpleMember(
            owner,
            file,
            member,
            "EnumMember",
            member.Identifier.ValueText,
            member.Identifier.ValueText,
            null,
            graph);
    }

    private static void AddDelegateDependencies(
        TypeObservation owner,
        ParsedSourceFile file,
        DelegateDeclarationSyntax declaration,
        TypeIndex index,
        EvidenceGraphBuilder graph)
    {
        AddTypeDependencies(owner.Key, owner, file, declaration.ReturnType, index, graph, "csharp.delegate-return-type");
        foreach (var parameter in declaration.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                AddTypeDependencies(owner.Key, owner, file, parameter.Type, index, graph, "csharp.delegate-parameter-type");
            }
        }
    }

    private static string AddSimpleMember(
        TypeObservation owner,
        ParsedSourceFile file,
        SyntaxNode declaration,
        string kind,
        string name,
        string? signature,
        IReadOnlyDictionary<string, string>? properties,
        EvidenceGraphBuilder graph,
        string? memberKey = null,
        SyntaxList<AttributeListSyntax>? attributes = null)
    {
        memberKey ??= MemberKey(owner.Key, kind, signature ?? name);
        var evidenceId = AddSyntaxEvidence(
            graph,
            file,
            declaration,
            $"csharp.{kind.ToLowerInvariant()}-declaration",
            ResolutionQuality.Exact);
        var attributeLists = attributes ?? (declaration as MemberDeclarationSyntax)?.AttributeLists ?? default;
        var (attributeNames, attributeEvidence) = AddAttributeEvidence(
            graph,
            file,
            attributeLists,
            "csharp.member-attribute");
        var evidence = new[] { evidenceId }.Concat(attributeEvidence).ToArray();
        var qualifiedName = signature is null
            ? $"{owner.QualifiedName}.{name}"
            : $"{owner.QualifiedName}.{signature}";

        var sourceProperties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (properties is not null)
        {
            foreach (var property in properties)
            {
                sourceProperties[property.Key] = property.Value;
            }
        }

        sourceProperties["projectMembershipResolution"] = GetProjectMembershipQuality(file).ToString().ToLowerInvariant();
        graph.AddNode(
            memberKey,
            kind,
            name,
            qualifiedName,
            owner.ProjectKey,
            evidence,
            attributeNames,
            sourceProperties);
        graph.AddEdge(
            "Contains",
            owner.Key,
            memberKey,
            null,
            evidence,
            ResolutionBasis.Syntax,
            GetSyntaxObservationQuality(file),
            GetSyntaxObservationDetails(file));
        return memberKey;
    }

    private static void AddTypeDependencies(
        string ownerKey,
        TypeObservation context,
        ParsedSourceFile file,
        TypeSyntax syntax,
        TypeIndex index,
        EvidenceGraphBuilder graph,
        string ruleId)
    {
        foreach (var reference in EnumerateNamedTypeReferences(syntax))
        {
            var evidenceId = AddSyntaxEvidence(
                graph,
                file,
                reference,
                ruleId,
                ResolutionQuality.Exact);
            var resolution = index.Resolve(reference, context, file);
            graph.AddEdge(
                "DeclaredTypeDependency",
                ownerKey,
                resolution.Target?.Key,
                resolution.Target is null ? NormalizeName(reference) : null,
                new[] { evidenceId },
                ResolutionBasis.Syntax,
                resolution.Target is null
                    ? resolution.Quality
                    : GetResolvedRelationshipQuality(file, resolution.Target),
                resolution.Target is null
                    ? resolution.Details
                    : GetResolvedRelationshipDetails(file, resolution.Target));
        }
    }

    private static IEnumerable<NameSyntax> EnumerateNamedTypeReferences(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case PredefinedTypeSyntax:
                yield break;
            case NullableTypeSyntax nullable:
                foreach (var item in EnumerateNamedTypeReferences(nullable.ElementType)) yield return item;
                yield break;
            case ArrayTypeSyntax array:
                foreach (var item in EnumerateNamedTypeReferences(array.ElementType)) yield return item;
                yield break;
            case PointerTypeSyntax pointer:
                foreach (var item in EnumerateNamedTypeReferences(pointer.ElementType)) yield return item;
                yield break;
            case RefTypeSyntax reference:
                foreach (var item in EnumerateNamedTypeReferences(reference.Type)) yield return item;
                yield break;
            case TupleTypeSyntax tuple:
                foreach (var element in tuple.Elements)
                    foreach (var item in EnumerateNamedTypeReferences(element.Type))
                        yield return item;
                yield break;
            case QualifiedNameSyntax qualified:
                yield return qualified;
                if (qualified.Right is GenericNameSyntax qualifiedGeneric)
                {
                    foreach (var argument in qualifiedGeneric.TypeArgumentList.Arguments)
                        foreach (var item in EnumerateNamedTypeReferences(argument))
                            yield return item;
                }
                yield break;
            case AliasQualifiedNameSyntax alias:
                yield return alias;
                if (alias.Name is GenericNameSyntax aliasGeneric)
                {
                    foreach (var argument in aliasGeneric.TypeArgumentList.Arguments)
                        foreach (var item in EnumerateNamedTypeReferences(argument))
                            yield return item;
                }
                yield break;
            case GenericNameSyntax generic:
                yield return generic;
                foreach (var argument in generic.TypeArgumentList.Arguments)
                    foreach (var item in EnumerateNamedTypeReferences(argument))
                        yield return item;
                yield break;
            case IdentifierNameSyntax identifier:
                yield return identifier;
                yield break;
        }
    }

    private static (IReadOnlyList<string> Names, IReadOnlyList<string> EvidenceIds) AddAttributeEvidence(
        EvidenceGraphBuilder graph,
        ParsedSourceFile file,
        SyntaxList<AttributeListSyntax> attributeLists,
        string ruleId)
    {
        if (attributeLists.Count == 0)
        {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var names = new List<string>();
        var evidence = new List<string>();
        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            names.Add(NormalizeName(attribute.Name));
            evidence.Add(AddSyntaxEvidence(
                graph,
                file,
                attribute,
                ruleId,
                ResolutionQuality.Exact));
        }

        return (
            names.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            evidence.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string AddSyntaxEvidence(
        EvidenceGraphBuilder graph,
        ParsedSourceFile file,
        SyntaxNode node,
        string ruleId,
        ResolutionQuality quality,
        string? details = null,
        ResolutionBasis basis = ResolutionBasis.Syntax)
    {
        if (quality == ResolutionQuality.Exact && file.HasConditionalDirectives)
        {
            quality = ResolutionQuality.Partial;
            details ??= ConditionalCompilationDetails;
        }

        var lineSpan = file.Tree.GetLineSpan(node.Span);
        return graph.AddEvidence(
            file.File.RelativePath,
            new CapturedSpan(
                node.Span.Start,
                node.Span.Length,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                lineSpan.EndLinePosition.Line + 1,
                lineSpan.EndLinePosition.Character + 1),
            ExtractorId,
            ExtractorVersion,
            ruleId,
            basis,
            quality,
            details);
    }

    private static ResolutionQuality GetProjectMembershipQuality(ParsedSourceFile file) =>
        file.Project.SourceSelectionPartial || file.HasConditionalDirectives
            ? ResolutionQuality.Partial
            : ResolutionQuality.Exact;

    private static string? GetProjectMembershipDetails(ParsedSourceFile file)
    {
        if (file.Project.SourceSelectionPartial && file.HasConditionalDirectives)
        {
            return $"{PartialSourceMembershipDetails} {ConditionalCompilationDetails}";
        }

        if (file.Project.SourceSelectionPartial)
        {
            return PartialSourceMembershipDetails;
        }

        return file.HasConditionalDirectives ? ConditionalCompilationDetails : null;
    }

    private static ResolutionQuality GetSyntaxObservationQuality(ParsedSourceFile file) =>
        file.HasConditionalDirectives ? ResolutionQuality.Partial : ResolutionQuality.Exact;

    private static string? GetSyntaxObservationDetails(ParsedSourceFile file) =>
        file.HasConditionalDirectives ? ConditionalCompilationDetails : null;

    private static ResolutionQuality GetResolvedRelationshipQuality(
        ParsedSourceFile source,
        TypeObservation target) =>
        source.HasConditionalDirectives ||
        target.Parts.Any(part =>
            part.File.HasConditionalDirectives || part.File.Project.SourceSelectionPartial)
            ? ResolutionQuality.Partial
            : ResolutionQuality.Exact;

    private static string? GetResolvedRelationshipDetails(
        ParsedSourceFile source,
        TypeObservation target) =>
        GetResolvedRelationshipQuality(source, target) == ResolutionQuality.Partial
            ? "The relationship involves source whose compilation membership or preprocessor branch is partial."
            : null;

    private static IReadOnlyDictionary<string, string> GetProjectMembershipProperties(ParsedSourceFile file) =>
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectMembershipResolution"] = GetProjectMembershipQuality(file).ToString().ToLowerInvariant(),
        };

    private static TypeDeclarationPart CreateTypeObservation(
        ParsedSourceFile file,
        BaseTypeDeclarationSyntax declaration)
    {
        var kind = declaration switch
        {
            InterfaceDeclarationSyntax => "Interface",
            StructDeclarationSyntax => "Struct",
            EnumDeclarationSyntax => "Enum",
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "RecordStruct",
            RecordDeclarationSyntax => "Record",
            _ => "Class",
        };
        var arity = declaration is TypeDeclarationSyntax typeDeclaration
            ? typeDeclaration.TypeParameterList?.Parameters.Count ?? 0
            : 0;
        var modifiers = declaration.Modifiers;
        return CreateTypeObservationCore(
            file,
            declaration,
            declaration.AttributeLists,
            modifiers,
            declaration.Identifier.ValueText,
            arity,
            kind);
    }

    private static TypeDeclarationPart CreateTypeObservation(
        ParsedSourceFile file,
        DelegateDeclarationSyntax declaration) =>
        CreateTypeObservationCore(
            file,
            declaration,
            declaration.AttributeLists,
            declaration.Modifiers,
            declaration.Identifier.ValueText,
            declaration.TypeParameterList?.Parameters.Count ?? 0,
            "Delegate");

    private static TypeDeclarationPart CreateTypeObservationCore(
        ParsedSourceFile file,
        MemberDeclarationSyntax declaration,
        SyntaxList<AttributeListSyntax> attributeLists,
        SyntaxTokenList modifiers,
        string name,
        int arity,
        string kind)
    {
        var namespaceName = GetQualifiedNamespace(declaration);
        var containingTypes = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => TypeMetadataSegment(type.Identifier.ValueText, type is TypeDeclarationSyntax typed
                ? typed.TypeParameterList?.Parameters.Count ?? 0
                : 0))
            .ToArray();
        var currentSegment = TypeMetadataSegment(name, arity);
        var metadataPrefix = namespaceName == "<global>" ? string.Empty : namespaceName + ".";
        var metadataName = metadataPrefix + string.Join('+', containingTypes.Append(currentSegment));
        var qualifiedName = (namespaceName == "<global>" ? string.Empty : namespaceName + ".") +
                            string.Join('.', containingTypes.Append(currentSegment));
        var key = TypeKey(file.ProjectKey, kind, metadataName);
        string? containingTypeKey = null;
        var containingDeclaration = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (containingDeclaration is not null)
        {
            var containingKind = containingDeclaration switch
            {
                InterfaceDeclarationSyntax => "Interface",
                StructDeclarationSyntax => "Struct",
                EnumDeclarationSyntax => "Enum",
                RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "RecordStruct",
                RecordDeclarationSyntax => "Record",
                _ => "Class",
            };
            var containingArity = containingDeclaration is TypeDeclarationSyntax containingTyped
                ? containingTyped.TypeParameterList?.Parameters.Count ?? 0
                : 0;
            var containingMetadata = metadataPrefix + string.Join('+', containingTypes[..^1].Append(
                TypeMetadataSegment(containingDeclaration.Identifier.ValueText, containingArity)));
            containingTypeKey = TypeKey(file.ProjectKey, containingKind, containingMetadata);
        }

        return new TypeDeclarationPart(
            key,
            file.ProjectKey,
            kind,
            name,
            qualifiedName,
            metadataName,
            namespaceName,
            containingTypeKey,
            file,
            declaration,
            attributeLists,
            modifiers);
    }

    private static string GetQualifiedNamespace(SyntaxNode declaration)
    {
        var namespaces = declaration.AncestorsAndSelf()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => NormalizeName(item.Name))
            .Where(item => item.Length > 0)
            .ToArray();
        return namespaces.Length == 0 ? "<global>" : string.Join('.', namespaces);
    }

    private static string ParameterSignature(SeparatedSyntaxList<ParameterSyntax> parameters) =>
        string.Join(
            ",",
            parameters.Select(parameter =>
            {
                var modifiers = NormalizeTokens(parameter.Modifiers);
                var type = parameter.Type is null ? "?" : NormalizeType(parameter.Type);
                return string.IsNullOrEmpty(modifiers) ? type : $"{modifiers} {type}";
            }));

    private static string NormalizeType(TypeSyntax type) =>
        type.WithoutTrivia().NormalizeWhitespace().ToFullString();

    private static string NormalizeName(NameSyntax name) =>
        name.WithoutTrivia().NormalizeWhitespace().ToFullString();

    private static string NormalizeTokens(SyntaxTokenList tokens) =>
        string.Join(' ', tokens.Select(token => token.ValueText));

    private static string AritySuffix(int arity) => arity == 0 ? string.Empty : $"`{arity}";

    private static string TypeMetadataSegment(string name, int arity) => name + AritySuffix(arity);

    private static string NamespaceKey(string projectKey, string qualifiedName) =>
        $"namespace|{projectKey}|{qualifiedName}";

    private static string TypeKey(string projectKey, string kind, string metadataName) =>
        $"type|{projectKey}|{kind}|{metadataName}";

    private static string MemberKey(string ownerKey, string kind, string signature) =>
        $"member|{ownerKey}|{kind}|{signature}";

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static bool HasUsableIdentifier(SyntaxToken identifier) =>
        !identifier.IsMissing && !string.IsNullOrWhiteSpace(identifier.ValueText);

    private static bool HasUsableName(NameSyntax name) =>
        !name.DescendantTokens().Any(token => token.IsMissing) &&
        !string.IsNullOrWhiteSpace(NormalizeName(name));

    private static bool HasUsableMemberIdentity(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => HasUsableIdentifier(method.Identifier),
        ConstructorDeclarationSyntax constructor => HasUsableIdentifier(constructor.Identifier),
        PropertyDeclarationSyntax property => HasUsableIdentifier(property.Identifier),
        IndexerDeclarationSyntax indexer => !indexer.ThisKeyword.IsMissing,
        EventDeclarationSyntax eventDeclaration => HasUsableIdentifier(eventDeclaration.Identifier),
        OperatorDeclarationSyntax operation => !operation.OperatorToken.IsMissing,
        ConversionOperatorDeclarationSyntax conversion =>
            !conversion.ImplicitOrExplicitKeyword.IsMissing && !conversion.Type.IsMissing,
        EnumMemberDeclarationSyntax enumMember => HasUsableIdentifier(enumMember.Identifier),
        _ => true,
    };

    private sealed record ParsedSourceFile(
        ProjectDescriptor Project,
        string ProjectKey,
        RepositoryFile File,
        SyntaxTree Tree,
        CompilationUnitSyntax Root,
        bool HasConditionalDirectives);

    private sealed record TypeDeclarationPart(
        string Key,
        string ProjectKey,
        string Kind,
        string Name,
        string QualifiedName,
        string MetadataName,
        string Namespace,
        string? ContainingTypeKey,
        ParsedSourceFile File,
        MemberDeclarationSyntax Declaration,
        SyntaxList<AttributeListSyntax> AttributeLists,
        SyntaxTokenList Modifiers);

    private sealed record TypeObservation(
        string Key,
        string ProjectKey,
        string Kind,
        string Name,
        string QualifiedName,
        string MetadataName,
        string Namespace,
        string? ContainingTypeKey,
        List<TypeDeclarationPart> Parts);

    private sealed record TypeResolution(
        TypeObservation? Target,
        ResolutionQuality Quality,
        string? Details);

    private sealed class TypeIndex
    {
        private readonly IReadOnlyDictionary<string, TypeObservation[]> _byMetadataName;
        private readonly IReadOnlyDictionary<string, TypeObservation[]> _byQualifiedName;
        private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _accessibleProjectKeys;
        private readonly IReadOnlyDictionary<string, UsingDirectiveSyntax[]> _globalUsingsByProject;
        private readonly IReadOnlyDictionary<string, TypeObservation> _typesByKey;

        public TypeIndex(
            IEnumerable<TypeObservation> types,
            IEnumerable<ParsedSourceFile> files,
            IReadOnlyDictionary<string, IReadOnlySet<string>> accessibleProjectKeys)
        {
            var values = types.ToArray();
            _byMetadataName = values.GroupBy(type => NormalizeMetadataName(type.MetadataName), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            _byQualifiedName = values.GroupBy(type => type.QualifiedName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            _typesByKey = values.ToDictionary(type => type.Key, StringComparer.Ordinal);
            _accessibleProjectKeys = accessibleProjectKeys;
            _globalUsingsByProject = files
                .GroupBy(file => file.ProjectKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .SelectMany(file => file.Root.Usings)
                        .Where(usingDirective => !usingDirective.GlobalKeyword.IsKind(SyntaxKind.None))
                        .OrderBy(usingDirective => usingDirective.SpanStart)
                        .ToArray(),
                    StringComparer.Ordinal);
        }

        public TypeResolution Resolve(
            TypeSyntax syntax,
            TypeObservation context,
            ParsedSourceFile file)
        {
            var candidate = GetLookupName(syntax);
            if (candidate is null)
            {
                return new TypeResolution(null, ResolutionQuality.Unresolved, "The syntax does not identify a named type.");
            }

            if (IsTypeParameter(syntax, candidate))
            {
                return new TypeResolution(
                    null,
                    ResolutionQuality.Unresolved,
                    $"The name '{candidate}' is a source type parameter, not a declared source type.");
            }

            var visibleUsings = GetVisibleUsings(syntax, file);
            if (candidate.StartsWith("global::", StringComparison.Ordinal))
            {
                return ResolveQualified(candidate[8..], context, candidate);
            }

            var aliasSeparator = candidate.IndexOf("::", StringComparison.Ordinal);
            if (aliasSeparator >= 0)
            {
                var aliasName = candidate[..aliasSeparator];
                var aliasSuffix = candidate[(aliasSeparator + 2)..];
                var aliasTargets = FindAliasTargets(visibleUsings, aliasName);
                if (aliasTargets.Count != 1)
                {
                    return new TypeResolution(
                        null,
                        aliasTargets.Count == 0 ? ResolutionQuality.Unresolved : ResolutionQuality.Ambiguous,
                        aliasTargets.Count == 0
                            ? $"The extern or using alias '{aliasName}' could not be resolved from source syntax."
                            : $"The using alias '{aliasName}' has multiple source declarations.");
                }

                return ResolveQualified(CombineName(aliasTargets[0], aliasSuffix), context, candidate);
            }

            var firstSegment = FirstSegment(candidate);
            var firstSegmentAliases = FindAliasTargets(visibleUsings, firstSegment);
            if (firstSegmentAliases.Count > 1)
            {
                return new TypeResolution(
                    null,
                    ResolutionQuality.Ambiguous,
                    $"The using alias '{firstSegment}' has multiple source declarations.");
            }

            if (firstSegmentAliases.Count == 1)
            {
                var aliasExpanded = candidate.Length == firstSegment.Length
                    ? firstSegmentAliases[0]
                    : CombineName(firstSegmentAliases[0], candidate[(firstSegment.Length + 1)..]);
                var aliasResolution = TryResolveQualified(aliasExpanded, context, candidate);
                if (aliasResolution is not null)
                {
                    return aliasResolution;
                }
            }

            foreach (var containingTypeName in GetContainingTypeNames(context))
            {
                var nestedResolution = TryResolveQualified(
                    CombineName(containingTypeName, candidate),
                    context,
                    candidate);
                if (nestedResolution is not null)
                {
                    return nestedResolution;
                }
            }

            if (context.Namespace != "<global>")
            {
                var namespaceResolution = TryResolveQualified(
                    CombineName(context.Namespace, candidate),
                    context,
                    candidate);
                if (namespaceResolution is not null)
                {
                    return namespaceResolution;
                }
            }

            if (candidate.Contains('.', StringComparison.Ordinal))
            {
                var globalResolution = TryResolveQualified(candidate, context, candidate);
                if (globalResolution is not null)
                {
                    return globalResolution;
                }
            }

            var importResolutions = visibleUsings
                .Where(IsNamespaceImport)
                .Select(usingDirective => NormalizeUsingName(usingDirective.Name!))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(namespaceImport => TryResolveQualified(
                    CombineName(namespaceImport, candidate),
                    context,
                    candidate))
                .Where(resolution => resolution is not null)
                .Cast<TypeResolution>()
                .ToArray();
            if (importResolutions.Any(resolution => resolution.Quality == ResolutionQuality.Ambiguous))
            {
                return new TypeResolution(
                    null,
                    ResolutionQuality.Ambiguous,
                    $"The name '{candidate}' was ambiguous within an imported namespace.");
            }

            var importedTargets = importResolutions
                .Select(resolution => resolution.Target)
                .Where(target => target is not null)
                .Cast<TypeObservation>()
                .DistinctBy(target => target.Key, StringComparer.Ordinal)
                .ToArray();
            if (importedTargets.Length == 1)
            {
                return new TypeResolution(importedTargets[0], ResolutionQuality.Exact, null);
            }

            if (importedTargets.Length > 1)
            {
                return new TypeResolution(
                    null,
                    ResolutionQuality.Ambiguous,
                    $"The name '{candidate}' matched multiple declared source types through using directives.");
            }

            var globalNamespaceResolution = TryResolveQualified(candidate, context, candidate);
            return globalNamespaceResolution ?? new TypeResolution(
                null,
                ResolutionQuality.Unresolved,
                "No declared source type in the current project or a literal project-reference closure matched the syntax-supported namespace scope.");
        }

        private TypeResolution ResolveQualified(
            string qualifiedName,
            TypeObservation context,
            string candidate) =>
            TryResolveQualified(qualifiedName, context, candidate) ?? new TypeResolution(
                null,
                ResolutionQuality.Unresolved,
                $"The name '{candidate}' did not match a declared source type in the accessible project closure.");

        private TypeResolution? TryResolveQualified(
            string qualifiedName,
            TypeObservation context,
            string candidate)
        {
            TypeObservation[]? matches;
            if (!_byQualifiedName.TryGetValue(qualifiedName, out matches) &&
                !_byMetadataName.TryGetValue(qualifiedName, out matches))
            {
                return null;
            }

            var accessible = _accessibleProjectKeys.TryGetValue(context.ProjectKey, out var projectKeys)
                ? projectKeys
                : new HashSet<string>(StringComparer.Ordinal) { context.ProjectKey };
            var accessibleMatches = matches
                .Where(match => accessible.Contains(match.ProjectKey))
                .OrderBy(match => match.Key, StringComparer.Ordinal)
                .ToArray();
            if (accessibleMatches.Length == 0)
            {
                return null;
            }

            var localMatches = accessibleMatches
                .Where(match => match.ProjectKey == context.ProjectKey)
                .ToArray();
            if (localMatches.Length == 1)
            {
                return new TypeResolution(localMatches[0], ResolutionQuality.Exact, null);
            }

            return accessibleMatches.Length == 1
                ? new TypeResolution(accessibleMatches[0], ResolutionQuality.Exact, null)
                : new TypeResolution(
                    null,
                    ResolutionQuality.Ambiguous,
                    $"The name '{candidate}' matched multiple declared source types in the accessible project closure.");
        }

        private IEnumerable<string> GetContainingTypeNames(TypeObservation context)
        {
            for (var current = context; current is not null;)
            {
                yield return current.QualifiedName;
                current = current.ContainingTypeKey is not null &&
                          _typesByKey.TryGetValue(current.ContainingTypeKey, out var containingType)
                    ? containingType
                    : null;
            }
        }

        private IReadOnlyList<UsingDirectiveSyntax> GetVisibleUsings(
            TypeSyntax syntax,
            ParsedSourceFile file)
        {
            var result = new List<UsingDirectiveSyntax>();
            if (_globalUsingsByProject.TryGetValue(file.ProjectKey, out var globalUsings))
            {
                result.AddRange(globalUsings);
            }

            result.AddRange(file.Root.Usings);
            foreach (var namespaceDeclaration in syntax.Ancestors()
                         .OfType<BaseNamespaceDeclarationSyntax>()
                         .Reverse())
            {
                result.AddRange(namespaceDeclaration.Usings);
            }

            return result;
        }

        private static bool IsNamespaceImport(UsingDirectiveSyntax usingDirective) =>
            usingDirective.Alias is null &&
            usingDirective.StaticKeyword.IsKind(SyntaxKind.None) &&
            usingDirective.Name is not null;

        private static IReadOnlyList<string> FindAliasTargets(
            IEnumerable<UsingDirectiveSyntax> visibleUsings,
            string aliasName)
            => visibleUsings
                .Where(usingDirective =>
                    usingDirective.Alias?.Name.Identifier.ValueText == aliasName &&
                    usingDirective.Name is not null)
                .Select(usingDirective => NormalizeUsingName(usingDirective.Name!))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();

        private static string NormalizeUsingName(NameSyntax name)
        {
            var normalized = GetLookupName(name) ?? NormalizeName(name);
            return normalized.StartsWith("global::", StringComparison.Ordinal)
                ? normalized[8..]
                : normalized;
        }

        private static bool IsTypeParameter(TypeSyntax syntax, string candidate)
        {
            if (candidate.Contains('.', StringComparison.Ordinal) ||
                candidate.Contains("::", StringComparison.Ordinal))
            {
                return false;
            }

            return syntax.Ancestors()
                .SelectMany(node => node switch
                {
                    TypeDeclarationSyntax declaration => declaration.TypeParameterList?.Parameters ?? default,
                    MethodDeclarationSyntax method => method.TypeParameterList?.Parameters ?? default,
                    DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.TypeParameterList?.Parameters ?? default,
                    LocalFunctionStatementSyntax localFunction => localFunction.TypeParameterList?.Parameters ?? default,
                    _ => default(SeparatedSyntaxList<TypeParameterSyntax>),
                })
                .Any(parameter => parameter.Identifier.ValueText == candidate);
        }

        private static string CombineName(string prefix, string suffix) =>
            string.IsNullOrEmpty(prefix) ? suffix : $"{prefix}.{suffix}";

        private static string FirstSegment(string candidate)
        {
            var separator = candidate.IndexOf('.');
            return separator < 0 ? candidate : candidate[..separator];
        }

        private static string? GetLookupName(TypeSyntax syntax) => syntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => TypeMetadataSegment(
                generic.Identifier.ValueText,
                generic.TypeArgumentList.Arguments.Count),
            QualifiedNameSyntax qualified => $"{GetLookupName(qualified.Left)}.{GetLookupName(qualified.Right)}",
            AliasQualifiedNameSyntax alias => $"{alias.Alias.Identifier.ValueText}::{GetLookupName(alias.Name)}",
            NullableTypeSyntax nullable => GetLookupName(nullable.ElementType),
            ArrayTypeSyntax array => GetLookupName(array.ElementType),
            PointerTypeSyntax pointer => GetLookupName(pointer.ElementType),
            RefTypeSyntax reference => GetLookupName(reference.Type),
            _ => null,
        };

        private static string NormalizeMetadataName(string value) => value.Replace('+', '.');
    }
}
