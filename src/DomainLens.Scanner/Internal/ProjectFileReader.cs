using System.Xml;
using System.Xml.Linq;

namespace DomainLens.Scanner.Internal;

internal sealed class ProjectFileReader
{
    public ProjectDescriptor Read(RepositoryInventory inventory, RepositoryFile projectFile)
    {
        var issues = new List<ScannerIssue>();
        var text = projectFile.ReadCapturedText();
        XDocument document;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreWhitespace = false,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 32 * 1024 * 1024,
            };
            using var stringReader = new StringReader(text);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            issues.Add(new ScannerIssue(
                "DL2002",
                ScannerIssueSeverity.Error,
                $"The project file could not be parsed safely: {Sanitize(exception.Message)}",
                projectFile.RelativePath));
            return CreateFailedDescriptor(projectFile, issues);
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ScannerIssue(
                "DL2003",
                ScannerIssueSeverity.Error,
                "The project file has no Project root element.",
                projectFile.RelativePath));
            return CreateFailedDescriptor(projectFile, issues);
        }

        var isSdkStyle = root.Attribute("Sdk") is not null;
        var assemblyName = FirstLiteralProperty(document, "AssemblyName") ??
                           Path.GetFileNameWithoutExtension(projectFile.RelativePath);
        var rootNamespace = FirstLiteralProperty(document, "RootNamespace") ?? assemblyName;
        var targetFrameworks = ReadTargetFrameworks(document, projectFile.RelativePath, issues);
        var sourceSelection = ReadSourcePaths(inventory, projectFile, document, isSdkStyle, issues);
        var hasRepositoryWideBuildFile =
            HasApplicableDirectoryBuildFile(inventory, GetRelativeDirectory(projectFile.RelativePath));
        var hasNonStandardImport = HasNonStandardImport(document);
        var dependencySelectionPartial =
            hasRepositoryWideBuildFile ||
            hasNonStandardImport ||
            HasNonStaticStructuralItem(document, "ProjectReference", "Reference", "PackageReference") ||
            HasConditionalOrDynamicStaticItem(document, "ProjectReference", "Reference", "PackageReference");
        var configurationSelectionPartial =
            hasRepositoryWideBuildFile ||
            hasNonStandardImport ||
            HasUnresolvedProjectProperty(document);

        InspectIgnoredBuildLogic(document, projectFile.RelativePath, issues);
        InspectConditionalStructuralItems(document, projectFile.RelativePath, issues);
        InspectDynamicDependencyItems(document, projectFile.RelativePath, issues);
        InspectUnresolvedProjectProperties(document, projectFile.RelativePath, issues);

        return new ProjectDescriptor(
            projectFile.RelativePath,
            Path.GetFileNameWithoutExtension(projectFile.RelativePath),
            assemblyName,
            rootNamespace,
            isSdkStyle,
            targetFrameworks,
            sourceSelection.Paths,
            sourceSelection.IsPartial,
            dependencySelectionPartial,
            configurationSelectionPartial,
            ReadItems(document, text, "ProjectReference"),
            ReadItems(document, text, "Reference"),
            ReadItems(document, text, "PackageReference"),
            SpanFor(root, text),
            true,
            issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Line)
                .ToArray());
    }

    private static ProjectDescriptor CreateFailedDescriptor(
        RepositoryFile projectFile,
        IReadOnlyList<ScannerIssue> issues) =>
        new(
            projectFile.RelativePath,
            Path.GetFileNameWithoutExtension(projectFile.RelativePath),
            Path.GetFileNameWithoutExtension(projectFile.RelativePath),
            Path.GetFileNameWithoutExtension(projectFile.RelativePath),
            false,
            Array.Empty<string>(),
            Array.Empty<string>(),
            true,
            true,
            true,
            Array.Empty<ProjectItemReference>(),
            Array.Empty<ProjectItemReference>(),
            Array.Empty<ProjectItemReference>(),
            new CapturedSpan(0, 0, 1, 1, 1, 1),
            false,
            issues);

    private static IReadOnlyList<string> ReadTargetFrameworks(
        XDocument document,
        string relativePath,
        List<ScannerIssue> issues)
    {
        var values = new List<string>();
        foreach (var element in StaticProperties(document))
        {
            if (element.Name.LocalName is not ("TargetFramework" or "TargetFrameworks" or "TargetFrameworkVersion"))
            {
                continue;
            }

            if (IsConditioned(element))
            {
                issues.Add(new ScannerIssue(
                    "DL2015",
                    ScannerIssueSeverity.Warning,
                    "A conditional target framework property was not evaluated.",
                    relativePath));
                continue;
            }

            AddLiteralValues(element.Value, ';', values, relativePath, issues);
        }

        if (values.Count == 0)
        {
            issues.Add(new ScannerIssue(
                "DL2004",
                ScannerIssueSeverity.Warning,
                "No literal target framework could be established without evaluating MSBuild.",
                relativePath));
            return new[] { "unknown" };
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddLiteralValues(
        string rawValue,
        char separator,
        List<string> values,
        string relativePath,
        List<ScannerIssue> issues)
    {
        foreach (var value in rawValue.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.Contains("$(", StringComparison.Ordinal))
            {
                issues.Add(new ScannerIssue(
                    "DL2005",
                    ScannerIssueSeverity.Warning,
                    "A target framework uses an MSBuild expression and was not evaluated.",
                    relativePath));
                continue;
            }

            values.Add(value);
        }
    }

    private static SourceSelection ReadSourcePaths(
        RepositoryInventory inventory,
        RepositoryFile projectFile,
        XDocument document,
        bool isSdkStyle,
        List<ScannerIssue> issues)
    {
        var projectDirectory = GetRelativeDirectory(projectFile.RelativePath);
        var projectFullDirectory = Path.GetDirectoryName(projectFile.FullPath)!;
        var compileElements = StaticItems(document, "Compile").ToArray();
        var compileIncludes = compileElements
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var compileRemoves = compileElements
            .Select(element => element.Attribute("Remove")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Select(NormalizeProjectItemPath)
            .ToArray();
        var defaultItemsDisabled =
            string.Equals(
                FirstLiteralProperty(document, "EnableDefaultCompileItems"),
                "false",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                FirstLiteralProperty(document, "EnableDefaultItems"),
                "false",
                StringComparison.OrdinalIgnoreCase);

        var paths = new HashSet<string>(PathSafety.FileSystemPathComparer);
        var isPartial = false;
        var sourceControlProperties = StaticProperties(document)
            .Where(element => element.Name.LocalName is
                "EnableDefaultCompileItems" or
                "EnableDefaultItems" or
                "DefaultItemExcludes" or
                "DefaultExcludesInProjectFolder")
            .ToArray();
        if (sourceControlProperties.Any(element =>
                IsConditioned(element) ||
                element.Value.Contains("$(", StringComparison.Ordinal) ||
                element.Name.LocalName is "DefaultItemExcludes" or "DefaultExcludesInProjectFolder") ||
            HasNonStaticProjectProperty(
                document,
                "EnableDefaultCompileItems",
                "EnableDefaultItems",
                "DefaultItemExcludes",
                "DefaultExcludesInProjectFolder"))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2020",
                ScannerIssueSeverity.Warning,
                "A source-item control property could not be applied without evaluating MSBuild.",
                projectFile.RelativePath));
        }

        if (compileElements.Any(element => element.Attribute("Exclude") is not null))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2021",
                ScannerIssueSeverity.Warning,
                "A Compile Exclude expression was not evaluated.",
                projectFile.RelativePath));
        }

        if (compileElements.Any(IsConditioned))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2014",
                ScannerIssueSeverity.Warning,
                "A conditional Compile item was retained as partial evidence without evaluating its MSBuild condition.",
                projectFile.RelativePath));
        }

        if (HasApplicableDirectoryBuildFile(inventory, projectDirectory))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2016",
                ScannerIssueSeverity.Warning,
                "An applicable Directory.Build.props or Directory.Build.targets file was found but was not evaluated.",
                projectFile.RelativePath));
        }

        if (HasNonStandardImport(document))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2022",
                ScannerIssueSeverity.Warning,
                "A project import that can affect source membership was not evaluated.",
                projectFile.RelativePath));
        }

        if (HasNonStaticStructuralItem(document, "Compile"))
        {
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2023",
                ScannerIssueSeverity.Warning,
                "A nested or dynamically selected Compile item was not evaluated.",
                projectFile.RelativePath));
        }

        var enumerateDefaults = isSdkStyle && !defaultItemsDisabled;
        if (!isSdkStyle && compileIncludes.Length == 0)
        {
            enumerateDefaults = true;
            isPartial = true;
            issues.Add(new ScannerIssue(
                "DL2012",
                ScannerIssueSeverity.Warning,
                "A legacy project had no literal Compile items; repository-local C# files were used as a partial fallback.",
                projectFile.RelativePath));
        }

        if (enumerateDefaults)
        {
            var prefix = string.IsNullOrEmpty(projectDirectory) ? string.Empty : projectDirectory + "/";
            foreach (var sourceFile in inventory.Files.Where(file =>
                         file.RelativePath.StartsWith(prefix, PathSafety.FileSystemPathComparison) &&
                         string.Equals(
                             Path.GetExtension(file.RelativePath),
                             ".cs",
                             PathSafety.FileSystemPathComparison)))
            {
                paths.Add(sourceFile.RelativePath);
            }
        }

        foreach (var include in compileIncludes)
        {
            if (include.Contains('*') || include.Contains('?') || include.Contains("$(", StringComparison.Ordinal))
            {
                isPartial = true;
                issues.Add(new ScannerIssue(
                    "DL2006",
                    ScannerIssueSeverity.Warning,
                    "A Compile item uses a glob or MSBuild expression and was not evaluated.",
                    projectFile.RelativePath));
                continue;
            }

            if (!PathSafety.TryResolveWithinRoot(
                    inventory.RootPath,
                    projectFullDirectory,
                    include,
                    out _,
                    out var relativePath))
            {
                isPartial = true;
                issues.Add(new ScannerIssue(
                    "DL2007",
                    ScannerIssueSeverity.Warning,
                    "A Compile item points outside the repository and was ignored.",
                    projectFile.RelativePath));
                continue;
            }

            if (inventory.ByRelativePath.ContainsKey(relativePath))
            {
                paths.Add(relativePath);
            }
            else
            {
                isPartial = true;
                issues.Add(new ScannerIssue(
                    "DL2008",
                    ScannerIssueSeverity.Warning,
                    "A declared Compile item could not be found in the repository snapshot.",
                    projectFile.RelativePath));
            }
        }

        foreach (var remove in compileRemoves)
        {
            if (remove.Contains('*') || remove.Contains('?') || remove.Contains("$(", StringComparison.Ordinal))
            {
                isPartial = true;
                issues.Add(new ScannerIssue(
                    "DL2013",
                    ScannerIssueSeverity.Warning,
                    "A Compile Remove item uses a glob or MSBuild expression and was not evaluated.",
                    projectFile.RelativePath));
                continue;
            }

            if (PathSafety.TryResolveWithinRoot(
                    inventory.RootPath,
                    projectFullDirectory,
                    remove,
                    out _,
                    out var removedRelativePath))
            {
                paths.Remove(removedRelativePath);
            }
        }

        return new SourceSelection(
            paths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            isPartial);
    }

    private static string NormalizeProjectItemPath(string path) =>
        path.Replace('\\', '/').Trim();

    private static IReadOnlyList<ProjectItemReference> ReadItems(
        XDocument document,
        string originalText,
        string localName) =>
        StaticItems(document, localName)
            .Select(element => new ProjectItemReference(
                element.Attribute("Include")?.Value ?? string.Empty,
                element.Attribute("Version")?.Value ??
                element.Elements().FirstOrDefault(child =>
                    string.Equals(child.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))?.Value,
                element.Elements().FirstOrDefault(child =>
                    string.Equals(child.Name.LocalName, "HintPath", StringComparison.OrdinalIgnoreCase))?.Value.Trim(),
                ReadMetadata(element, "ReferenceOutputAssembly"),
                ReadMetadata(element, "Aliases"),
                IsConditioned(element),
                SpanFor(element, originalText)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Include))
            .OrderBy(item => item.Include, StringComparer.Ordinal)
            .ToArray();

    private static string? ReadMetadata(XElement element, string localName)
    {
        var value = element.Attributes().FirstOrDefault(attribute =>
                        string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value ??
                    element.Elements().FirstOrDefault(child =>
                        string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? FirstLiteralProperty(XDocument document, string localName)
    {
        var value = StaticProperties(document)
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase) &&
                !IsConditioned(element))
            ?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Contains("$(", StringComparison.Ordinal) ? null : value;
    }

    private static void InspectIgnoredBuildLogic(
        XDocument document,
        string relativePath,
        List<ScannerIssue> issues)
    {
        var executableElements = document.Descendants()
            .Where(element => element.Name.LocalName is "Exec" or "UsingTask" or "PreBuildEvent" or "PostBuildEvent")
            .Select(element => element.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (executableElements.Length == 0)
        {
            // Continue: a Target can mutate structural items without containing an executable task.
        }
        else
        {
            issues.Add(new ScannerIssue(
                "DL2009",
                ScannerIssueSeverity.Information,
                $"Repository-controlled build logic was treated as data and not executed: {string.Join(", ", executableElements)}.",
                relativePath));
        }

        var structuralMutation = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Target", StringComparison.OrdinalIgnoreCase))
            .SelectMany(target => target.Descendants())
            .Any(element => element.Name.LocalName is "Compile" or "ProjectReference" or "Reference" or "PackageReference");
        if (structuralMutation)
        {
            issues.Add(new ScannerIssue(
                "DL2017",
                ScannerIssueSeverity.Warning,
                "A Target can mutate structural project items; those mutations were not evaluated.",
                relativePath));
        }
    }

    private static void InspectConditionalStructuralItems(
        XDocument document,
        string relativePath,
        List<ScannerIssue> issues)
    {
        var names = new[] { "ProjectReference", "Reference", "PackageReference" };
        if (names.SelectMany(name => StaticItems(document, name)).Any(IsConditioned))
        {
            issues.Add(new ScannerIssue(
                "DL2019",
                ScannerIssueSeverity.Warning,
                "A conditional dependency item was retained as partial evidence without evaluating its MSBuild condition.",
                relativePath));
        }

        if (HasNonStaticStructuralItem(document, "ProjectReference", "Reference", "PackageReference"))
        {
            issues.Add(new ScannerIssue(
                "DL2024",
                ScannerIssueSeverity.Warning,
                "A nested or dynamically selected dependency item was not evaluated.",
                relativePath));
        }
    }

    private static void InspectDynamicDependencyItems(
        XDocument document,
        string relativePath,
        List<ScannerIssue> issues)
    {
        var names = new[] { "ProjectReference", "Reference", "PackageReference" };
        var hasDynamicValue = names
            .SelectMany(name => StaticItems(document, name))
            .Any(element =>
                element.Attributes().Any(attribute => ContainsMsBuildExpression(attribute.Value)) ||
                element.Elements().Any(metadata => ContainsMsBuildExpression(metadata.Value)));
        if (!hasDynamicValue)
        {
            return;
        }

        issues.Add(new ScannerIssue(
            "DL2025",
            ScannerIssueSeverity.Warning,
            "A dependency item uses an unevaluated MSBuild expression and was retained as partial evidence.",
            relativePath));
    }

    private static bool ContainsMsBuildExpression(string? value) =>
        value is not null &&
        (value.Contains("$(", StringComparison.Ordinal) ||
         value.Contains("@(", StringComparison.Ordinal) ||
         value.Contains("%(", StringComparison.Ordinal));

    private static void InspectUnresolvedProjectProperties(
        XDocument document,
        string relativePath,
        List<ScannerIssue> issues)
    {
        if (!HasUnresolvedProjectProperty(document))
        {
            return;
        }

        issues.Add(new ScannerIssue(
            "DL2026",
            ScannerIssueSeverity.Warning,
            "A conditional or dynamic project identity/framework property was not evaluated.",
            relativePath));
    }

    private static bool HasUnresolvedProjectProperty(XDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AssemblyName",
            "RootNamespace",
            "TargetFramework",
            "TargetFrameworks",
            "TargetFrameworkVersion",
        };
        return StaticProperties(document).Any(element =>
                   names.Contains(element.Name.LocalName) &&
                   (IsConditioned(element) || ContainsMsBuildExpression(element.Value))) ||
               document.Descendants().Any(element =>
                   names.Contains(element.Name.LocalName) && !IsStaticProperty(element));
    }

    private static IEnumerable<XElement> StaticProperties(XDocument document) =>
        document.Root?.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .SelectMany(group => group.Elements()) ?? [];

    private static IEnumerable<XElement> StaticItems(XDocument document, string localName) =>
        (document.Root?.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "ItemGroup", StringComparison.OrdinalIgnoreCase))
            .SelectMany(group => group.Elements()) ?? [])
        .Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static bool IsStaticItem(XElement element) =>
        element.Parent is { } itemGroup &&
        string.Equals(itemGroup.Name.LocalName, "ItemGroup", StringComparison.OrdinalIgnoreCase) &&
        itemGroup.Parent is { } project &&
        string.Equals(project.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase);

    private static bool IsStaticProperty(XElement element) =>
        element.Parent is { } propertyGroup &&
        string.Equals(propertyGroup.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
        propertyGroup.Parent is { } project &&
        string.Equals(project.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase);

    private static bool HasNonStaticProjectProperty(XDocument document, params string[] localNames)
    {
        var names = localNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document.Descendants().Any(element =>
            names.Contains(element.Name.LocalName) && !IsStaticProperty(element));
    }

    private static bool HasConditionalOrDynamicStaticItem(XDocument document, params string[] localNames) =>
        localNames.SelectMany(name => StaticItems(document, name)).Any(element =>
            IsConditioned(element) ||
            element.Attributes().Any(attribute => ContainsMsBuildExpression(attribute.Value)) ||
            element.Elements().Any(metadata => ContainsMsBuildExpression(metadata.Value)));

    private static bool HasNonStandardImport(XDocument document) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Project")?.Value.Replace('\\', '/').Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value =>
                !string.Equals(
                    value,
                    "$(MSBuildToolsPath)/Microsoft.CSharp.targets",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    value,
                    "$(MSBuildBinPath)/Microsoft.CSharp.targets",
                    StringComparison.OrdinalIgnoreCase));

    private static bool HasNonStaticStructuralItem(XDocument document, params string[] localNames)
    {
        var names = localNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document.Descendants().Any(element =>
            names.Contains(element.Name.LocalName) && !IsStaticItem(element));
    }

    private static bool IsConditioned(XElement element) =>
        element.Attribute("Condition") is not null || element.Parent?.Attribute("Condition") is not null;

    private static bool HasApplicableDirectoryBuildFile(
        RepositoryInventory inventory,
        string projectDirectory)
    {
        var directory = projectDirectory;
        while (true)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                var candidate = string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";
                if (inventory.ByRelativePath.ContainsKey(candidate))
                {
                    return true;
                }
            }

            var slash = directory.LastIndexOf('/');
            if (directory.Length == 0)
            {
                return false;
            }

            directory = slash < 0 ? string.Empty : directory[..slash];
        }
    }

    private static CapturedSpan SpanFor(XElement element, string text)
    {
        var lineInfo = (IXmlLineInfo)element;
        if (!lineInfo.HasLineInfo())
        {
            return new CapturedSpan(0, 0, 1, 1, 1, 1);
        }

        var start = CapturedSpan.FromLine(text, lineInfo.LineNumber, lineInfo.LinePosition, 0).StartOffset;
        var openingEnd = FindTagEnd(text, start);
        if (openingEnd < 0)
        {
            return CapturedSpan.FromOffsets(text, start, element.Name.LocalName.Length + 2);
        }

        var endExclusive = openingEnd + 1;
        if (element.HasElements)
        {
            var closingTag = $"</{element.Name.LocalName}";
            var closingStart = text.IndexOf(closingTag, endExclusive, StringComparison.Ordinal);
            if (closingStart >= 0)
            {
                var closingEnd = FindTagEnd(text, closingStart + closingTag.Length);
                if (closingEnd >= 0)
                {
                    endExclusive = closingEnd + 1;
                }
            }
        }

        return CapturedSpan.FromOffsets(text, start, endExclusive - start);
    }

    private static int FindTagEnd(string text, int start)
    {
        char? quote = null;
        for (var index = start; index < text.Length; index++)
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
            else if (character == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetRelativeDirectory(string relativePath)
    {
        var lastSlash = relativePath.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : relativePath[..lastSlash];
    }

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record SourceSelection(IReadOnlyList<string> Paths, bool IsPartial);
}
