using DomainLens.Core;

namespace DomainLens.Analyzer.Wcf;

internal enum WcfFrameworkProfileCompatibility
{
    SupportedNet472,
    Unsupported,
    Unknown,
    Ambiguous,
}

internal sealed record WcfFrameworkProfileAssessment(
    WcfFrameworkProfileCompatibility Compatibility,
    string DeclaredTargetFrameworks,
    string QualityDetails)
{
    public bool AllowsExactSourceObservation =>
        Compatibility == WcfFrameworkProfileCompatibility.SupportedNet472;
}

/// <summary>
/// Applies the trusted net472 profile boundary to repository-source observations. This policy is
/// intentionally scoped to C# semantic evidence; declarative .svc/configuration observations do
/// not depend on the controlled compilation and are not constrained here.
/// </summary>
internal sealed class WcfFrameworkProfilePolicy
{
    private const string Exact = "exact";
    private readonly IReadOnlyDictionary<string, EvidenceNode> _projectsById;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _projectIdsBySourcePath;
    private readonly IReadOnlySet<SourceProjectKey> _partialSourceMemberships;

    public WcfFrameworkProfilePolicy(AnalysisDocument baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        _projectsById = baseline.Nodes
            .Where(node => string.Equals(node.Kind, "Project", StringComparison.Ordinal))
            .ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        var evidenceById = baseline.Evidence.ToDictionary(
            evidence => evidence.EvidenceId,
            StringComparer.Ordinal);
        var projectIdsByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var partialMemberships = new HashSet<SourceProjectKey>();

        foreach (var node in baseline.Nodes.Where(node => node.ProjectId is not null))
        {
            foreach (var evidenceId in node.EvidenceIds)
            {
                if (!evidenceById.TryGetValue(evidenceId, out var evidence) ||
                    !string.Equals(
                        Path.GetExtension(evidence.RelativePath),
                        ".cs",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = CanonicalIdentity.NormalizeRepositoryPath(evidence.RelativePath);
                if (!projectIdsByPath.TryGetValue(path, out var projectIds))
                {
                    projectIds = new HashSet<string>(StringComparer.Ordinal);
                    projectIdsByPath.Add(path, projectIds);
                }

                projectIds.Add(node.ProjectId!);
                if (node.Properties.TryGetValue("projectMembershipResolution", out var membership) &&
                    !string.Equals(membership, Exact, StringComparison.OrdinalIgnoreCase))
                {
                    partialMemberships.Add(new SourceProjectKey(path, node.ProjectId!));
                }
            }
        }

        _projectIdsBySourcePath = projectIdsByPath.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);
        _partialSourceMemberships = partialMemberships;
    }

    public WcfFrameworkProfileAssessment AssessObservation(string relativePath)
    {
        var path = CanonicalIdentity.NormalizeRepositoryPath(relativePath);
        if (!_projectIdsBySourcePath.TryGetValue(path, out var projectIds) || projectIds.Count == 0)
        {
            return Unknown(
                "The source could not be associated with a selected project and therefore cannot establish compatibility with the trusted net472 profile.");
        }

        if (projectIds.Count != 1)
        {
            return new WcfFrameworkProfileAssessment(
                WcfFrameworkProfileCompatibility.Ambiguous,
                JoinFrameworks(projectIds),
                "The source maps to multiple selected project contexts, so one compatible target profile cannot be established.");
        }

        return AssessProject(path, projectIds.Single());
    }

    public WcfFrameworkProfileAssessment AssessProjectContext(
        string relativePath,
        string? projectId)
    {
        if (projectId is null)
        {
            return AssessObservation(relativePath);
        }

        var path = CanonicalIdentity.NormalizeRepositoryPath(relativePath);
        if (!_projectIdsBySourcePath.TryGetValue(path, out var projectIds) ||
            !projectIds.Contains(projectId))
        {
            return Unknown(
                "The source-to-project association could not be established from the structural Evidence Graph.");
        }

        return AssessProject(path, projectId);
    }

    public static ResolutionQuality Constrain(
        ResolutionQuality requested,
        WcfFrameworkProfileAssessment assessment) =>
        requested == ResolutionQuality.Exact && !assessment.AllowsExactSourceObservation
            ? ResolutionQuality.Partial
            : requested;

    private WcfFrameworkProfileAssessment AssessProject(string relativePath, string projectId)
    {
        if (!_projectsById.TryGetValue(projectId, out var project))
        {
            return Unknown(
                "The selected source project is unavailable from the structural Evidence Graph.");
        }

        project.Properties.TryGetValue("targetFrameworks", out var declaredFrameworks);
        declaredFrameworks ??= string.Empty;
        var values = declaredFrameworks
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var projectSelectionIsExact =
            HasExactProperty(project, "configurationSelectionResolution") &&
            HasExactProperty(project, "sourceSelectionResolution") &&
            !_partialSourceMemberships.Contains(new SourceProjectKey(relativePath, projectId));
        if (!projectSelectionIsExact)
        {
            return new WcfFrameworkProfileAssessment(
                WcfFrameworkProfileCompatibility.Ambiguous,
                declaredFrameworks,
                "The selected project profile or source membership depends on conditional, imported, or otherwise unevaluated project configuration.");
        }

        if (values.Length == 0 ||
            (values.Length == 1 && string.Equals(values[0], "unknown", StringComparison.OrdinalIgnoreCase)))
        {
            return Unknown(
                "No literal target framework was deterministically established for the selected source project.",
                declaredFrameworks);
        }

        if (values.Length != 1)
        {
            return new WcfFrameworkProfileAssessment(
                WcfFrameworkProfileCompatibility.Ambiguous,
                declaredFrameworks,
                "The selected source project declares multiple target frameworks, so one compatible semantic profile cannot be established.");
        }

        if (IsSupported(values[0]))
        {
            return new WcfFrameworkProfileAssessment(
                WcfFrameworkProfileCompatibility.SupportedNet472,
                declaredFrameworks,
                "The source is associated with one statically selected net472-compatible project profile.");
        }

        return new WcfFrameworkProfileAssessment(
            WcfFrameworkProfileCompatibility.Unsupported,
            declaredFrameworks,
            "The selected source project declares a target framework outside the trusted net472 semantic profile.");
    }

    private string JoinFrameworks(IEnumerable<string> projectIds) =>
        string.Join(
            ';',
            projectIds
                .Select(projectId => _projectsById.TryGetValue(projectId, out var project) &&
                                     project.Properties.TryGetValue("targetFrameworks", out var value)
                    ? value
                    : string.Empty)
                .Where(value => value.Length != 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal));

    private static WcfFrameworkProfileAssessment Unknown(
        string details,
        string declaredFrameworks = "") =>
        new(
            WcfFrameworkProfileCompatibility.Unknown,
            declaredFrameworks,
            details);

    private static bool HasExactProperty(EvidenceNode project, string name) =>
        project.Properties.TryGetValue(name, out var value) &&
        string.Equals(value, Exact, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupported(string value) =>
        string.Equals(value, "net472", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "v4.7.2", StringComparison.OrdinalIgnoreCase);

    private sealed record SourceProjectKey(string RelativePath, string ProjectId);
}
