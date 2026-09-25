using DomainLens.Core;
using Microsoft.CodeAnalysis;
using DiagnosticSeverity = DomainLens.Core.DiagnosticSeverity;

namespace DomainLens.Analyzer.Wcf;

internal static class WcfSourceRelationshipResolver
{
    private const string UniqueSourceMatchDetails =
        "One supported source candidate matched in the repository-wide synthetic net472 compilation; effective project, configuration, and runtime activation remain unknown.";

    public static void ResolveConfiguration(
        WcfContributionBuilder builder,
        WcfSourceAnalysisResult source,
        WcfConfigurationAnalysisResult configuration)
    {
        foreach (var service in configuration.Services)
        {
            if (service.Name is null)
            {
                continue;
            }

            var candidates = SourceClasses(source, service.Name);
            AddRelationship(
                builder,
                service.RelativePath,
                service.EvidenceId,
                service.Node.NodeId,
                candidates,
                service.Name,
                WcfVocabulary.EdgeKinds.ConfiguredServiceImplementation,
                WcfVocabulary.Rules.ConfigurationServiceLink,
                WcfVocabulary.Diagnostics.ImplementationUnresolved,
                WcfVocabulary.Diagnostics.ImplementationAmbiguous,
                "configured WCF service implementation");
        }

        foreach (var endpoint in configuration.Endpoints)
        {
            if (endpoint.Contract is null)
            {
                continue;
            }

            var candidates = source.ServiceContracts
                .Where(candidate =>
                    string.Equals(candidate.ClrIdentity, endpoint.Contract, StringComparison.Ordinal) ||
                    string.Equals(candidate.ConfigurationName, endpoint.Contract, StringComparison.Ordinal))
                .Select(candidate => candidate.Node)
                .DistinctBy(node => node.NodeId, StringComparer.Ordinal)
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)
                .ToArray();
            AddRelationship(
                builder,
                endpoint.RelativePath,
                endpoint.EvidenceId,
                endpoint.Node.NodeId,
                candidates,
                endpoint.Contract,
                WcfVocabulary.EdgeKinds.EndpointContract,
                WcfVocabulary.Rules.ConfigurationEndpointContract,
                WcfVocabulary.Diagnostics.EndpointContractUnresolved,
                WcfVocabulary.Diagnostics.EndpointContractAmbiguous,
                "WCF endpoint contract");
        }

        foreach (var activation in configuration.Activations)
        {
            if (activation.Service is null)
            {
                continue;
            }

            var candidates = SourceClasses(source, activation.Service);
            AddRelationship(
                builder,
                activation.RelativePath,
                activation.EvidenceId,
                activation.Node.NodeId,
                candidates,
                activation.Service,
                WcfVocabulary.EdgeKinds.ServiceActivationImplementation,
                WcfVocabulary.Rules.ConfigurationActivationLink,
                WcfVocabulary.Diagnostics.ImplementationUnresolved,
                WcfVocabulary.Diagnostics.ImplementationAmbiguous,
                "WCF service activation implementation");
        }
    }

    public static void ResolveSvc(
        WcfContributionBuilder builder,
        WcfSourceAnalysisResult source,
        WcfSvcAnalysisResult svc)
    {
        foreach (var host in svc.HostingDeclarations)
        {
            if (host.Service is null)
            {
                continue;
            }

            var candidates = SourceClasses(source, host.Service);
            AddRelationship(
                builder,
                host.RelativePath,
                host.EvidenceId,
                host.Node.NodeId,
                candidates,
                host.Service,
                WcfVocabulary.EdgeKinds.HostsService,
                WcfVocabulary.Rules.SvcServiceLink,
                WcfVocabulary.Diagnostics.SvcServiceUnresolved,
                WcfVocabulary.Diagnostics.SvcServiceUnresolved,
                ".svc ServiceHost implementation");
        }
    }

    private static IReadOnlyList<EvidenceNode> SourceClasses(
        WcfSourceAnalysisResult source,
        string target) =>
        source.SourceTypes
            .Where(candidate => candidate.Symbol.TypeKind == TypeKind.Class &&
                                string.Equals(candidate.ClrIdentity, target, StringComparison.Ordinal))
            .Select(candidate => candidate.Node)
            .DistinctBy(node => node.NodeId, StringComparer.Ordinal)
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

    private static void AddRelationship(
        WcfContributionBuilder builder,
        string relativePath,
        string declarationEvidenceId,
        string fromNodeId,
        IReadOnlyList<EvidenceNode> candidates,
        string textualTarget,
        string edgeKind,
        string ruleId,
        string unresolvedDiagnosticCode,
        string ambiguousDiagnosticCode,
        string description)
    {
        textualTarget = Bound(textualTarget);
        var declarationEvidence = builder.GetContributionEvidence(declarationEvidenceId);
        var quality = candidates.Count switch
        {
            0 => ResolutionQuality.Unresolved,
            1 => ResolutionQuality.Partial,
            _ => ResolutionQuality.Ambiguous,
        };
        var details = candidates.Count switch
        {
            0 => "No supported source candidate matched the declarative target.",
            1 => UniqueSourceMatchDetails,
            _ => "Multiple supported source candidates matched the declarative target; DomainLens did not guess.",
        };
        var relationshipEvidenceId = builder.AddEvidence(
            relativePath,
            declarationEvidence.Span,
            ruleId,
            ResolutionBasis.Composite,
            quality,
            details);
        builder.AddEdge(
            edgeKind,
            fromNodeId,
            candidates.Count == 1 ? candidates[0].NodeId : null,
            candidates.Count == 1 ? null : textualTarget,
            [relationshipEvidenceId],
            ResolutionBasis.Composite,
            quality,
            details);

        if (candidates.Count == 1)
        {
            return;
        }

        builder.AddDiagnostic(
            candidates.Count == 0 ? unresolvedDiagnosticCode : ambiguousDiagnosticCode,
            DiagnosticSeverity.Warning,
            candidates.Count == 0
                ? $"The {description} '{textualTarget}' could not be correlated to a supported source candidate."
                : $"The {description} '{textualTarget}' matched multiple supported source candidates.",
            relativePath,
            [relationshipEvidenceId],
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidateCount"] = candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["target"] = textualTarget,
            });
    }

    private static string Bound(string value) =>
        value.Length <= 512 ? value : value[..512];
}
