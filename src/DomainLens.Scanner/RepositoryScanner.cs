using DomainLens.Core;
using DomainLens.Scanner.Internal;

namespace DomainLens.Scanner;

public sealed class RepositoryScanner
{
    private const string RepositoryExtractor = "domainlens.repository-structure";
    private const string ExtractorVersion = "0.1.0";

    public async Task<AnalysisDocument> AnalyzeAsync(
        ScannerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        RepositoryInventory inventory;
        try
        {
            inventory = await new SafeRepositoryInventory()
                .CaptureAsync(options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or UnauthorizedAccessException or IOException or
                InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CreateFailureDocument("DL0001", Sanitize(exception.Message));
        }

        // The content manifest is the authoritative snapshot in Milestone 1. Reading .git
        // metadata would require a second, separately hardened filesystem capture boundary.
        var snapshot = RepositorySnapshot.Create(
            null,
            inventory.Files.Select(file => new ManifestEntry(file.RelativePath, file.ContentHash, file.Length)));

        try
        {
            return AnalyzeCapturedSnapshot(options, inventory, snapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = new AnalysisDocument(
                AnalysisSchema.CurrentVersion,
                AnalysisStatus.Failure,
                snapshot,
                Array.Empty<EvidenceNode>(),
                Array.Empty<EvidenceEdge>(),
                Array.Empty<EvidenceRecord>(),
                new[]
                {
                    AnalysisDiagnostic.Create(
                        "DL0002",
                        DiagnosticSeverity.Error,
                        $"The scanner could not complete safely: {Sanitize(exception.Message)}")
                });
            return AnalysisJson.WithCanonicalHash(failure);
        }
    }

    private static AnalysisDocument AnalyzeCapturedSnapshot(
        ScannerOptions options,
        RepositoryInventory inventory,
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var issues = new List<ScannerIssue>(inventory.Issues);
        var solutionReader = new SolutionFileReader();
        var solutionFiles = inventory.Files
            .Where(file => string.Equals(
                Path.GetExtension(file.RelativePath),
                ".sln",
                PathSafety.FileSystemPathComparison))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var allSolutions = solutionFiles
            .Select(solutionReader.Read)
            .ToArray();

        var selectedSolutions = SelectSolutions(options.Solution, allSolutions, out var selectionFailure);
        if (selectionFailure is not null)
        {
            return CreateFailureDocument(snapshot, selectionFailure.Value.Code, selectionFailure.Value.Message);
        }

        foreach (var issue in selectedSolutions.SelectMany(solution => solution.Issues))
        {
            issues.Add(issue);
        }

        var projectReader = new ProjectFileReader();
        var allProjects = inventory.Files
            .Where(file => string.Equals(
                Path.GetExtension(file.RelativePath),
                ".csproj",
                PathSafety.FileSystemPathComparison))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => projectReader.Read(inventory, file))
            .ToDictionary(project => project.RelativePath, PathSafety.FileSystemPathComparer);

        if (allProjects.Count == 0)
        {
            return CreateFailureDocument(snapshot, "DL0003", "No C# project files were found in the repository snapshot.");
        }

        var selectedProjectPaths = SelectProjects(options.Solution, selectedSolutions, allProjects, inventory, issues);
        var selectedProjects = selectedProjectPaths
            .Where(allProjects.ContainsKey)
            .Select(path => allProjects[path])
            .OrderBy(project => project.RelativePath, StringComparer.Ordinal)
            .ToArray();
        foreach (var issue in selectedProjects.SelectMany(project => project.Issues))
        {
            issues.Add(issue);
        }

        if (selectedProjects.Length == 0 || selectedProjects.All(project => !project.ParsedSuccessfully))
        {
            return CreateFailureDocument(
                snapshot,
                "DL0004",
                "The selected scope did not contain a safely analyzable C# project.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var graph = new EvidenceGraphBuilder(snapshot, inventory);
        const string repositoryKey = "repository";
        graph.AddNode(
            repositoryKey,
            "Repository",
            "Repository",
            "Repository",
            null,
            properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["manifestFileCount"] = snapshot.Manifest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        var projectKeys = selectedProjects.ToDictionary(
            project => project.RelativePath,
            project => $"project|{project.RelativePath}",
            PathSafety.FileSystemPathComparer);

        AddSolutionAndProjectNodes(
            selectedSolutions,
            selectedProjects,
            projectKeys,
            inventory,
            graph,
            repositoryKey,
            issues);
        AddProjectReferenceNodes(selectedProjects, projectKeys, inventory, graph, issues);

        new CSharpSyntaxExtractor().Analyze(inventory, selectedProjects, projectKeys, graph, issues);

        var (nodes, edges, evidence) = graph.Build();
        var status = issues.Any(issue => issue.Severity is ScannerIssueSeverity.Warning or ScannerIssueSeverity.Error)
            ? AnalysisStatus.PartialSuccess
            : AnalysisStatus.Success;
        var diagnostics = ConvertDiagnostics(issues, inventory);
        var document = new AnalysisDocument(
            AnalysisSchema.CurrentVersion,
            status,
            snapshot,
            nodes,
            edges,
            evidence,
            diagnostics);
        document = AnalysisJson.WithCanonicalHash(document);

        var validation = AnalysisGraphValidator.Validate(document);
        if (!validation.IsValid)
        {
            var integrityMessage = string.Join(
                "; ",
                validation.Issues
                    .Where(issue => issue.Severity == DiagnosticSeverity.Error)
                    .Select(issue => $"{issue.Code}: {issue.Message}"));
            return CreateFailureDocument(
                snapshot,
                "DL0005",
                $"Evidence Graph integrity validation failed: {integrityMessage}");
        }

        return document;
    }

    private static IReadOnlyList<SolutionDescriptor> SelectSolutions(
        string? requestedSolution,
        IReadOnlyList<SolutionDescriptor> allSolutions,
        out (string Code, string Message)? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(requestedSolution))
        {
            return allSolutions;
        }

        string normalized;
        try
        {
            normalized = CanonicalIdentity.NormalizeRepositoryPath(requestedSolution);
        }
        catch (ArgumentException)
        {
            failure = ("DL0010", "The selected solution must be a repository-relative path.");
            return Array.Empty<SolutionDescriptor>();
        }

        var exact = allSolutions
            .Where(solution => string.Equals(
                solution.RelativePath,
                normalized,
                PathSafety.FileSystemPathComparison))
            .ToArray();
        if (exact.Length == 1)
        {
            return exact;
        }

        var requestedOnlyAFileName = !normalized.Contains('/');
        var byName = requestedOnlyAFileName
            ? allSolutions
            .Where(solution => string.Equals(
                Path.GetFileName(solution.RelativePath),
                Path.GetFileName(normalized),
                PathSafety.FileSystemPathComparison))
            .ToArray()
            : [];
        if (byName.Length == 1)
        {
            return byName;
        }

        failure = byName.Length > 1
            ? ("DL0011", "The selected solution name is ambiguous; provide its repository-relative path.")
            : ("DL0012", "The selected solution was not found in the repository snapshot.");
        return Array.Empty<SolutionDescriptor>();
    }

    private static HashSet<string> SelectProjects(
        string? requestedSolution,
        IReadOnlyList<SolutionDescriptor> selectedSolutions,
        IReadOnlyDictionary<string, ProjectDescriptor> allProjects,
        RepositoryInventory inventory,
        List<ScannerIssue> issues)
    {
        var selected = string.IsNullOrWhiteSpace(requestedSolution)
            ? allProjects.Keys.ToHashSet(PathSafety.FileSystemPathComparer)
            : selectedSolutions.SelectMany(solution => solution.Projects)
                .Select(project => project.ProjectRelativePath)
                .ToHashSet(PathSafety.FileSystemPathComparer);

        var queue = new Queue<string>(selected.OrderBy(path => path, StringComparer.Ordinal));
        while (queue.Count > 0)
        {
            var projectPath = queue.Dequeue();
            if (!allProjects.TryGetValue(projectPath, out var project))
            {
                issues.Add(new ScannerIssue(
                    "DL2010",
                    ScannerIssueSeverity.Warning,
                    "A selected solution or project reference points to a missing project.",
                    FindExistingOwnerPath(projectPath, selectedSolutions, inventory)));
                continue;
            }

            foreach (var reference in project.ProjectReferences)
            {
                if (!TryResolveProjectReference(inventory, project, reference.Include, out var targetPath))
                {
                    continue;
                }

                if (selected.Add(targetPath))
                {
                    queue.Enqueue(targetPath);
                }
            }
        }

        return selected;
    }

    private static void AddSolutionAndProjectNodes(
        IReadOnlyList<SolutionDescriptor> solutions,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyDictionary<string, string> projectKeys,
        RepositoryInventory inventory,
        EvidenceGraphBuilder graph,
        string repositoryKey,
        List<ScannerIssue> issues)
    {
        foreach (var project in projects.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            var projectKey = projectKeys[project.RelativePath];
            var projectConfigurationQuality = project.ParsedSuccessfully && !project.ConfigurationSelectionPartial
                ? ResolutionQuality.Exact
                : ResolutionQuality.Partial;
            var projectConfigurationDetails = project.ConfigurationSelectionPartial
                ? "Project identity and framework configuration may depend on unevaluated MSBuild conditions or imports."
                : null;
            var projectEvidence = graph.AddEvidence(
                project.RelativePath,
                project.Span,
                RepositoryExtractor,
                ExtractorVersion,
                "project.declaration",
                ResolutionBasis.DeclarativeConfiguration,
                projectConfigurationQuality,
                projectConfigurationDetails);
            graph.AddNode(
                projectKey,
                "Project",
                project.Name,
                project.RelativePath,
                null,
                new[] { projectEvidence },
                properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["assemblyName"] = project.AssemblyName,
                    ["rootNamespace"] = project.RootNamespace,
                    ["isSdkStyle"] = project.IsSdkStyle ? "true" : "false",
                    ["targetFrameworks"] = string.Join(';', project.TargetFrameworks),
                    ["sourceSelectionResolution"] = project.SourceSelectionPartial ? "partial" : "exact",
                    ["dependencySelectionResolution"] = project.DependencySelectionPartial ? "partial" : "exact",
                    ["configurationSelectionResolution"] = project.ConfigurationSelectionPartial ? "partial" : "exact",
                });
            graph.AddEdge(
                "Contains",
                repositoryKey,
                projectKey,
                null,
                new[] { projectEvidence },
                ResolutionBasis.DeclarativeConfiguration,
                projectConfigurationQuality,
                projectConfigurationDetails);
        }

        foreach (var solution in solutions.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            var solutionKey = $"solution|{solution.RelativePath}";
            var evidenceId = graph.AddEvidence(
                solution.RelativePath,
                solution.Span,
                RepositoryExtractor,
                ExtractorVersion,
                "solution.declaration",
                ResolutionBasis.DeclarativeConfiguration,
                ResolutionQuality.Exact);
            graph.AddNode(
                solutionKey,
                "Solution",
                Path.GetFileNameWithoutExtension(solution.RelativePath),
                solution.RelativePath,
                null,
                new[] { evidenceId },
                properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["relativePath"] = solution.RelativePath,
                });
            graph.AddEdge(
                "Contains",
                repositoryKey,
                solutionKey,
                null,
                new[] { evidenceId },
                ResolutionBasis.DeclarativeConfiguration,
                ResolutionQuality.Exact);

            foreach (var entry in solution.Projects)
            {
                var entryEvidence = graph.AddEvidence(
                    solution.RelativePath,
                    entry.Span,
                    RepositoryExtractor,
                    ExtractorVersion,
                    "solution.project-entry",
                    ResolutionBasis.DeclarativeConfiguration,
                    projectKeys.ContainsKey(entry.ProjectRelativePath)
                        ? ResolutionQuality.Exact
                        : ResolutionQuality.Unresolved);
                if (projectKeys.TryGetValue(entry.ProjectRelativePath, out var projectKey))
                {
                    graph.AddEdge(
                        "Contains",
                        solutionKey,
                        projectKey,
                        null,
                        new[] { entryEvidence },
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact);
                }
                else
                {
                    graph.AddEdge(
                        "Contains",
                        solutionKey,
                        null,
                        entry.ProjectRelativePath,
                        new[] { entryEvidence },
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Unresolved);
                    issues.Add(new ScannerIssue(
                        "DL2010",
                        ScannerIssueSeverity.Warning,
                        "A selected solution or project reference points to a missing project.",
                        solution.RelativePath));
                }
            }
        }
    }

    private static void AddProjectReferenceNodes(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyDictionary<string, string> projectKeys,
        RepositoryInventory inventory,
        EvidenceGraphBuilder graph,
        List<ScannerIssue> issues)
    {
        foreach (var project in projects.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            var projectKey = projectKeys[project.RelativePath];
            foreach (var reference in project.ProjectReferences)
            {
                var dependencyPartial = IsDependencyPartial(project, reference);
                var declaredQuality = dependencyPartial
                    ? ResolutionQuality.Partial
                    : ResolutionQuality.Exact;
                var dependencyDetails = GetDependencyResolutionDetails(project, reference);
                var evidenceId = graph.AddEvidence(
                    project.RelativePath,
                    reference.Span,
                    RepositoryExtractor,
                    ExtractorVersion,
                    "project.project-reference",
                    ResolutionBasis.DeclarativeConfiguration,
                    declaredQuality,
                    dependencyDetails);
                if (TryResolveProjectReference(inventory, project, reference.Include, out var targetPath) &&
                    projectKeys.TryGetValue(targetPath, out var targetKey))
                {
                    graph.AddEdge(
                        "ReferencesProject",
                        projectKey,
                        targetKey,
                        null,
                        new[] { evidenceId },
                        ResolutionBasis.DeclarativeConfiguration,
                        declaredQuality,
                        dependencyDetails);
                }
                else
                {
                    graph.AddEdge(
                        "ReferencesProject",
                        projectKey,
                        null,
                        reference.Include,
                        new[] { evidenceId },
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Unresolved);
                    issues.Add(new ScannerIssue(
                        "DL2011",
                        ScannerIssueSeverity.Warning,
                        "A project reference could not be resolved within the repository snapshot.",
                        project.RelativePath));
                }
            }

            foreach (var reference in project.AssemblyReferences)
            {
                var assemblyName = reference.Include.Split(',')[0].Trim();
                var assemblyKey = $"assembly|{projectKey}|{reference.Include}|{reference.HintPath}";
                var declarationIdentity = string.IsNullOrWhiteSpace(reference.HintPath)
                    ? reference.Include
                    : $"{reference.Include}|HintPath={reference.HintPath.Replace('\\', '/')}";
                var quality = IsDependencyPartial(project, reference)
                    ? ResolutionQuality.Partial
                    : ResolutionQuality.Exact;
                var resolutionDetails = GetDependencyResolutionDetails(project, reference);

                if (!string.IsNullOrWhiteSpace(reference.HintPath) &&
                    !TryResolveAssemblyHintPath(inventory, project, reference.HintPath, out _))
                {
                    quality = ResolutionQuality.Partial;
                    resolutionDetails = "The declared HintPath could not be resolved to a captured repository file.";
                    issues.Add(new ScannerIssue(
                        "DL2018",
                        ScannerIssueSeverity.Warning,
                        "An assembly reference HintPath could not be resolved within the repository snapshot.",
                        project.RelativePath));
                }

                var evidenceId = graph.AddEvidence(
                    project.RelativePath,
                    reference.Span,
                    RepositoryExtractor,
                    ExtractorVersion,
                    "project.assembly-reference",
                    ResolutionBasis.DeclarativeConfiguration,
                    quality,
                    resolutionDetails);
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["declaration"] = reference.Include,
                };
                if (!string.IsNullOrWhiteSpace(reference.HintPath))
                {
                    properties["hintPath"] = reference.HintPath;
                }

                graph.AddNode(
                    assemblyKey,
                    "AssemblyReference",
                    assemblyName,
                    declarationIdentity,
                    projectKey,
                    new[] { evidenceId },
                    properties: properties);
                graph.AddEdge(
                    "ReferencesAssembly",
                    projectKey,
                    assemblyKey,
                    null,
                    new[] { evidenceId },
                    ResolutionBasis.DeclarativeConfiguration,
                    quality,
                    resolutionDetails);
            }

            foreach (var reference in project.PackageReferences)
            {
                var version = reference.Version ?? "unspecified";
                var packageKey = $"package|{reference.Include}|{version}";
                var quality = IsDependencyPartial(project, reference)
                    ? ResolutionQuality.Partial
                    : ResolutionQuality.Exact;
                var dependencyDetails = GetDependencyResolutionDetails(project, reference);
                var evidenceId = graph.AddEvidence(
                    project.RelativePath,
                    reference.Span,
                    RepositoryExtractor,
                    ExtractorVersion,
                    "project.package-reference",
                    ResolutionBasis.DeclarativeConfiguration,
                    quality,
                    dependencyDetails);
                graph.AddNode(
                    packageKey,
                    "PackageReference",
                    reference.Include,
                    $"{reference.Include}@{version}",
                    null,
                    new[] { evidenceId },
                    properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["version"] = version,
                    });
                graph.AddEdge(
                    "ReferencesPackage",
                    projectKey,
                    packageKey,
                    null,
                    new[] { evidenceId },
                    ResolutionBasis.DeclarativeConfiguration,
                    quality,
                    dependencyDetails);
            }
        }
    }

    private static bool TryResolveProjectReference(
        RepositoryInventory inventory,
        ProjectDescriptor project,
        string include,
        out string targetPath)
    {
        targetPath = string.Empty;
        if (include.Contains('*') || include.Contains('?') || include.Contains("$(", StringComparison.Ordinal))
        {
            return false;
        }

        var projectFile = inventory.ByRelativePath[project.RelativePath];
        return PathSafety.TryResolveWithinRoot(
                   inventory.RootPath,
                   Path.GetDirectoryName(projectFile.FullPath)!,
                   include,
                   out _,
                   out targetPath) &&
               inventory.ByRelativePath.ContainsKey(targetPath);
    }

    private static string? GetDependencyResolutionDetails(
        ProjectDescriptor project,
        ProjectItemReference reference)
    {
        if (HasDynamicDependencyValue(reference))
        {
            return "One or more dependency values contain an unevaluated MSBuild expression.";
        }

        if (reference.IsConditional && project.DependencySelectionPartial)
        {
            return "The item condition and effective MSBuild dependency selection were not evaluated.";
        }

        if (reference.IsConditional)
        {
            return "The MSBuild item condition was not evaluated.";
        }

        return project.DependencySelectionPartial
            ? "Effective MSBuild dependency selection was not evaluated."
            : null;
    }

    private static bool IsDependencyPartial(
        ProjectDescriptor project,
        ProjectItemReference reference) =>
        reference.IsConditional || project.DependencySelectionPartial || HasDynamicDependencyValue(reference);

    private static bool HasDynamicDependencyValue(ProjectItemReference reference) =>
        new[]
        {
            reference.Include,
            reference.Version,
            reference.HintPath,
            reference.ReferenceOutputAssembly,
            reference.Aliases,
        }.Any(value =>
            value is not null &&
            (value.Contains("$(", StringComparison.Ordinal) ||
             value.Contains("@(", StringComparison.Ordinal) ||
             value.Contains("%(", StringComparison.Ordinal)));

    private static bool TryResolveAssemblyHintPath(
        RepositoryInventory inventory,
        ProjectDescriptor project,
        string hintPath,
        out string targetPath)
    {
        targetPath = string.Empty;
        if (hintPath.Contains('*') || hintPath.Contains('?') || hintPath.Contains("$(", StringComparison.Ordinal))
        {
            return false;
        }

        var projectFile = inventory.ByRelativePath[project.RelativePath];
        return PathSafety.TryResolveWithinRoot(
                   inventory.RootPath,
                   Path.GetDirectoryName(projectFile.FullPath)!,
                   hintPath,
                   out _,
                   out targetPath) &&
               inventory.ByRelativePath.ContainsKey(targetPath);
    }

    private static string? FindExistingOwnerPath(
        string missingProjectPath,
        IReadOnlyList<SolutionDescriptor> solutions,
        RepositoryInventory inventory)
    {
        var solution = solutions.FirstOrDefault(item => item.Projects.Any(project =>
            string.Equals(
                project.ProjectRelativePath,
                missingProjectPath,
                PathSafety.FileSystemPathComparison)));
        return solution is not null && inventory.ByRelativePath.ContainsKey(solution.RelativePath)
            ? solution.RelativePath
            : null;
    }

    private static IReadOnlyList<AnalysisDiagnostic> ConvertDiagnostics(
        IEnumerable<ScannerIssue> issues,
        RepositoryInventory inventory) =>
        issues
            .Select(issue =>
            {
                var hasManifestPath = issue.RelativePath is not null &&
                                      inventory.ByRelativePath.ContainsKey(issue.RelativePath);
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (issue.RelativePath is not null && !hasManifestPath)
                {
                    properties["subjectPath"] = issue.RelativePath;
                }

                if (issue.Line is not null)
                {
                    properties["line"] = issue.Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (issue.Column is not null)
                {
                    properties["column"] = issue.Column.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return new AnalysisDiagnostic(
                    issue.Code,
                    issue.Severity switch
                    {
                        ScannerIssueSeverity.Information => DiagnosticSeverity.Information,
                        ScannerIssueSeverity.Warning => DiagnosticSeverity.Warning,
                        _ => DiagnosticSeverity.Error,
                    },
                    issue.Message,
                    hasManifestPath ? issue.RelativePath : null,
                    Array.Empty<string>(),
                    properties);
            })
            .Distinct()
            .OrderBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

    private static AnalysisDocument CreateFailureDocument(string code, string message) =>
        CreateFailureDocument(RepositorySnapshot.Create(null, Array.Empty<ManifestEntry>()), code, message);

    private static AnalysisDocument CreateFailureDocument(
        RepositorySnapshot snapshot,
        string code,
        string message)
    {
        var document = new AnalysisDocument(
            AnalysisSchema.CurrentVersion,
            AnalysisStatus.Failure,
            snapshot,
            Array.Empty<EvidenceNode>(),
            Array.Empty<EvidenceEdge>(),
            Array.Empty<EvidenceRecord>(),
            new[] { AnalysisDiagnostic.Create(code, DiagnosticSeverity.Error, message) });
        return AnalysisJson.WithCanonicalHash(document);
    }

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
