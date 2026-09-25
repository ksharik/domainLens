using System.Xml;
using System.Xml.Linq;
using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// Reads the explicitly allowlisted <c>system.serviceModel</c> subset as inert XML. This type
/// never uses the runtime configuration system, loads extension types, or follows external input.
/// </summary>
internal sealed class WcfConfigurationAnalyzer(ManifestVerifiedFileReader fileReader)
{
    private const int MaximumNamespaceInspectionElements = 4096;
    private const int MaximumUnsupportedBehaviorElements = 256;
    private const string XmlDocumentTransformNamespace =
        "http://schemas.microsoft.com/XML-Document-Transform";

    private static readonly HashSet<string> BindingFamilies = new(StringComparer.Ordinal)
    {
        "basicHttpBinding",
        "customBinding",
        "msmqIntegrationBinding",
        "netMsmqBinding",
        "netNamedPipeBinding",
        "netTcpBinding",
        "webHttpBinding",
        "ws2007HttpBinding",
        "wsDualHttpBinding",
        "wsFederationHttpBinding",
        "wsHttpBinding",
    };

    private static readonly HashSet<string> ServiceBehaviorElements = new(StringComparer.Ordinal)
    {
        "dataContractSerializer",
        "serviceAuthorization",
        "serviceCredentials",
        "serviceDebug",
        "serviceMetadata",
        "serviceThrottling",
    };

    private static readonly HashSet<string> EndpointBehaviorElements = new(StringComparer.Ordinal)
    {
        "callbackDebug",
        "clientCredentials",
        "clientVia",
        "dataContractSerializer",
        "webHttp",
    };

    private static readonly HashSet<string> IdentityElements = new(StringComparer.Ordinal)
    {
        "certificate",
        "dns",
        "rsa",
        "servicePrincipalName",
        "userPrincipalName",
    };

    public async Task<WcfConfigurationAnalysisResult> AnalyzeAsync(
        string workspaceRepositoryRoot,
        WcfContributionBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = new List<WcfConfiguredServiceObservation>();
        var endpoints = new List<WcfConfiguredEndpointObservation>();
        var bindings = new List<WcfBindingObservation>();
        var behaviors = new List<WcfBehaviorObservation>();
        var activations = new List<WcfServiceActivationObservation>();
        var extensions = new List<WcfExtensionObservation>();

        foreach (var entry in builder.Baseline.Snapshot.Manifest
                     .Where(item => string.Equals(
                         Path.GetExtension(item.Path),
                         ".config",
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
                    $"The configuration file could not be accepted by the manifest-verified reader ({read.Status}).",
                    entry.Path,
                    properties: new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["readStatus"] = read.Status.ToString(),
                    });
                continue;
            }

            var source = WcfTextDocument.Decode(read.Content);
            if (!source.Text.Contains("system.serviceModel", StringComparison.Ordinal))
            {
                // An unrelated .config file is outside this analyzer's scope. Avoid turning its
                // syntax problems into WCF coverage diagnostics.
                continue;
            }

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
                    MaxCharactersInDocument = Math.Max(1, source.Text.Length),
                };
                using var textReader = new StringReader(source.Text);
                using var xmlReader = XmlReader.Create(textReader, settings);
                document = XDocument.Load(
                    xmlReader,
                    LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException)
            {
                var containsDtd = source.Text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
                builder.AddDiagnostic(
                    containsDtd
                        ? WcfVocabulary.Diagnostics.DtdProhibited
                        : WcfVocabulary.Diagnostics.MalformedConfiguration,
                    DiagnosticSeverity.Warning,
                    containsDtd
                        ? "The WCF configuration contains a prohibited DTD and was not parsed."
                        : $"The WCF configuration XML is malformed and was not parsed: {Sanitize(exception.Message)}",
                    entry.Path);
                continue;
            }

            var serviceModel = document.Root?.Elements()
                .FirstOrDefault(element => IsElement(element, "system.serviceModel"));
            if (serviceModel is null)
            {
                continue;
            }

            var namespaceInspection = InspectNamespaceQualifiedMetadata(
                entry.Path,
                source,
                serviceModel,
                builder);
            if (!namespaceInspection.CompletedWithinBudget)
            {
                // Do not begin any subsequent whole-section traversal after the bounded preflight
                // stops. The traversal-limit evidence and diagnostic are the complete result for
                // this section, and no external configuration references are followed or reported.
                continue;
            }

            ReportExternalReferences(entry.Path, source, serviceModel, builder);
            ReportUnsupportedAttributes(
                entry.Path,
                source,
                serviceModel,
                builder,
                "configSource");
            if (namespaceInspection.HasTransformMetadata)
            {
                // A transform document is not an effective configuration document. Likewise, if
                // transform controls are present, declarations must not be promoted. Retain
                // diagnostics only.
                continue;
            }

            foreach (var child in serviceModel.Elements())
            {
                switch (child.Name.NamespaceName.Length == 0 ? child.Name.LocalName : string.Empty)
                {
                    case "services":
                        ParseServices(entry.Path, source, child, builder, services, endpoints);
                        break;
                    case "client":
                        ParseClient(entry.Path, source, child, builder, endpoints);
                        break;
                    case "bindings":
                        ParseBindings(entry.Path, source, child, builder, bindings);
                        break;
                    case "behaviors":
                        ParseBehaviors(entry.Path, source, child, builder, behaviors);
                        break;
                    case "serviceHostingEnvironment":
                        ParseHosting(entry.Path, source, child, builder, activations);
                        break;
                    case "extensions":
                        ParseExtensions(entry.Path, source, child, builder, extensions);
                        break;
                    default:
                        ReportUnsupportedElement(entry.Path, source, child, builder);
                        break;
                }
            }
        }

        ResolveDeclarativeRelationships(builder, services, endpoints, bindings, behaviors);
        return new WcfConfigurationAnalysisResult(
            services,
            endpoints,
            bindings,
            behaviors,
            activations,
            extensions);
    }

    private static void ParseServices(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfConfiguredServiceObservation> services,
        ICollection<WcfConfiguredEndpointObservation> endpoints)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        foreach (var element in container.Elements())
        {
            if (!IsElement(element, "service"))
            {
                ReportUnsupportedElement(path, source, element, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, element, builder, "name", "behaviorConfiguration");
            var name = Literal(element, "name");
            var behavior = Literal(element, "behaviorConfiguration");
            var span = source.OpeningTagSpan(element);
            var evidenceId = builder.AddEvidence(
                path,
                span,
                WcfVocabulary.Rules.ConfigurationService,
                ResolutionBasis.DeclarativeConfiguration,
                ResolutionQuality.Exact);
            var node = builder.AddNode(
                WcfVocabulary.NodeKinds.ConfiguredService,
                name ?? "(unnamed service)",
                QualifiedIdentity(path, "service", name, span.StartOffset),
                projectId: null,
                evidenceIds: [evidenceId],
                properties: Properties(
                    (WcfVocabulary.Properties.ConfigServiceName, name),
                    (WcfVocabulary.Properties.BehaviorConfiguration, behavior)));
            services.Add(new WcfConfiguredServiceObservation(
                path,
                span.StartOffset,
                node,
                evidenceId,
                name,
                behavior));

            foreach (var child in element.Elements())
            {
                if (IsElement(child, "endpoint"))
                {
                    var endpoint = ParseEndpoint(
                        path,
                        source,
                        child,
                        builder,
                        "service",
                        node.NodeId);
                    endpoints.Add(endpoint);
                    var endpointEvidence = builder.GetContributionEvidence(endpoint.EvidenceId);
                    var relationshipEvidenceId = builder.AddEvidence(
                        path,
                        endpointEvidence.Span,
                        WcfVocabulary.Rules.ConfigurationServiceEndpoint,
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact,
                        "The endpoint is lexically contained by the configured service in the captured configuration document.");
                    builder.AddEdge(
                        WcfVocabulary.EdgeKinds.ConfiguredServiceEndpoint,
                        node.NodeId,
                        endpoint.Node.NodeId,
                        unresolvedTarget: null,
                        [relationshipEvidenceId],
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact,
                        "The endpoint is lexically contained by the configured service in the captured configuration document.");
                }
                else
                {
                    ReportUnsupportedElement(path, source, child, builder);
                }
            }
        }
    }

    private static void ParseClient(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfConfiguredEndpointObservation> endpoints)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        foreach (var element in container.Elements())
        {
            if (IsElement(element, "endpoint"))
            {
                endpoints.Add(ParseEndpoint(path, source, element, builder, "client", null));
            }
            else
            {
                ReportUnsupportedElement(path, source, element, builder);
            }
        }
    }

    private static WcfConfiguredEndpointObservation ParseEndpoint(
        string path,
        WcfTextDocument source,
        XElement element,
        WcfContributionBuilder builder,
        string direction,
        string? configuredServiceNodeId)
    {
        ReportUnsupportedAttributes(
            path,
            source,
            element,
            builder,
            "address",
            "behaviorConfiguration",
            "binding",
            "bindingConfiguration",
            "contract",
            "name");
        var name = Literal(element, "name");
        var address = Literal(element, "address");
        var contract = Literal(element, "contract");
        var binding = Literal(element, "binding");
        var bindingConfiguration = Literal(element, "bindingConfiguration");
        var behaviorConfiguration = Literal(element, "behaviorConfiguration");
        var identity = ParseEndpointIdentity(path, source, element, builder);
        var span = source.OpeningTagSpan(element);
        var evidenceId = builder.AddEvidence(
            path,
            span,
            WcfVocabulary.Rules.ConfigurationEndpoint,
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Exact);
        var displayName = name ?? contract ?? "(unnamed endpoint)";
        var node = builder.AddNode(
            WcfVocabulary.NodeKinds.Endpoint,
            displayName,
            QualifiedIdentity(path, $"{direction}-endpoint", displayName, span.StartOffset),
            projectId: null,
            evidenceIds: new[] { evidenceId }.Concat(identity.EvidenceIds).ToArray(),
            properties: Properties(
                (WcfVocabulary.Properties.Direction, direction),
                (WcfVocabulary.Properties.ConfigEndpointName, name),
                (WcfVocabulary.Properties.EndpointName, direction == "client" ? name : null),
                (WcfVocabulary.Properties.Address, address),
                (WcfVocabulary.Properties.Contract, contract),
                (WcfVocabulary.Properties.Binding, binding),
                (WcfVocabulary.Properties.BindingConfiguration, bindingConfiguration),
                (WcfVocabulary.Properties.BehaviorConfiguration, behaviorConfiguration),
                (WcfVocabulary.Properties.EndpointIdentityKind,
                    identity.Kinds.Count == 0 ? null : string.Join(",", identity.Kinds))));
        return new WcfConfiguredEndpointObservation(
            path,
            span.StartOffset,
            node,
            evidenceId,
            direction,
            configuredServiceNodeId,
            name,
            address,
            contract,
            binding,
            bindingConfiguration,
            behaviorConfiguration,
            identity.Kinds);
    }

    private static EndpointIdentityMetadata ParseEndpointIdentity(
        string path,
        WcfTextDocument source,
        XElement endpoint,
        WcfContributionBuilder builder)
    {
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceIds = new List<string>();
        foreach (var child in endpoint.Elements())
        {
            if (!IsElement(child, "identity"))
            {
                ReportUnsupportedElement(path, source, child, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, child, builder);
            foreach (var identity in child.Elements())
            {
                if (identity.Name.NamespaceName.Length == 0 &&
                    IdentityElements.Contains(identity.Name.LocalName))
                {
                    kinds.Add(identity.Name.LocalName);
                    evidenceIds.Add(builder.AddEvidence(
                        path,
                        source.OpeningTagSpan(identity),
                        WcfVocabulary.Rules.ConfigurationEndpointIdentity,
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact,
                        "An allowlisted endpoint identity child kind was observed as inert metadata."));
                }
                else
                {
                    ReportUnsupportedElement(path, source, identity, builder);
                }
            }
        }

        return new EndpointIdentityMetadata(
            kinds.ToArray(),
            evidenceIds.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static void ParseBindings(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfBindingObservation> bindings)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        foreach (var family in container.Elements())
        {
            if (family.Name.NamespaceName.Length != 0 || !BindingFamilies.Contains(family.Name.LocalName))
            {
                ReportUnsupportedElement(path, source, family, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, family, builder);
            foreach (var element in family.Elements())
            {
                if (!IsElement(element, "binding"))
                {
                    ReportUnsupportedElement(path, source, element, builder);
                    continue;
                }

                ReportUnsupportedAttributes(
                    path,
                    source,
                    element,
                    builder,
                    "messageEncoding",
                    "name",
                    "textEncoding",
                    "transferMode");
                var name = Literal(element, "name");
                var messageEncoding = Literal(element, "messageEncoding");
                var textEncoding = Literal(element, "textEncoding");
                var transferMode = Literal(element, "transferMode");
                var security = ParseBindingSecurity(path, source, element, builder);
                var span = source.OpeningTagSpan(element);
                var evidenceId = builder.AddEvidence(
                    path,
                    span,
                    WcfVocabulary.Rules.ConfigurationBinding,
                    ResolutionBasis.DeclarativeConfiguration,
                    ResolutionQuality.Exact);
                var displayName = name ?? "(default)";
                var node = builder.AddNode(
                    WcfVocabulary.NodeKinds.Binding,
                    $"{family.Name.LocalName}:{displayName}",
                    QualifiedIdentity(
                        path,
                        $"binding-{family.Name.LocalName}",
                        displayName,
                        span.StartOffset),
                    projectId: null,
                    evidenceIds: new[] { evidenceId }.Concat(security.EvidenceIds).ToArray(),
                    properties: Properties(
                        (WcfVocabulary.Properties.ConfigBindingFamily, family.Name.LocalName),
                        (WcfVocabulary.Properties.ConfigBindingName, name),
                        (WcfVocabulary.Properties.BindingMessageEncoding, messageEncoding),
                        (WcfVocabulary.Properties.BindingTextEncoding, textEncoding),
                        (WcfVocabulary.Properties.BindingTransferMode, transferMode),
                        (WcfVocabulary.Properties.BindingSecurityMode, security.Mode),
                        (WcfVocabulary.Properties.BindingTransportClientCredentialType,
                            security.TransportClientCredentialType),
                        (WcfVocabulary.Properties.BindingMessageClientCredentialType,
                            security.MessageClientCredentialType)));
                bindings.Add(new WcfBindingObservation(
                    path,
                    span.StartOffset,
                    node,
                    evidenceId,
                    family.Name.LocalName,
                    name));
            }
        }
    }

    private static BindingSecurity ParseBindingSecurity(
        string path,
        WcfTextDocument source,
        XElement binding,
        WcfContributionBuilder builder)
    {
        string? mode = null;
        string? transportClientCredentialType = null;
        string? messageClientCredentialType = null;
        var evidenceIds = new List<string>();
        foreach (var child in binding.Elements())
        {
            if (!IsElement(child, "security"))
            {
                ReportUnsupportedElement(path, source, child, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, child, builder, "mode");
            evidenceIds.Add(builder.AddEvidence(
                path,
                source.OpeningTagSpan(child),
                WcfVocabulary.Rules.ConfigurationBindingSecurity,
                ResolutionBasis.DeclarativeConfiguration,
                ResolutionQuality.Exact,
                "An allowlisted binding security declaration was observed as inert configuration metadata."));
            mode ??= Literal(child, "mode");
            foreach (var securityChild in child.Elements())
            {
                if (IsElement(securityChild, "transport"))
                {
                    ReportUnsupportedAttributes(
                        path,
                        source,
                        securityChild,
                        builder,
                        "clientCredentialType");
                    evidenceIds.Add(builder.AddEvidence(
                        path,
                        source.OpeningTagSpan(securityChild),
                        WcfVocabulary.Rules.ConfigurationBindingSecurity,
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact,
                        "An allowlisted binding transport-security declaration was observed as inert configuration metadata."));
                    transportClientCredentialType ??= Literal(securityChild, "clientCredentialType");
                }
                else if (IsElement(securityChild, "message"))
                {
                    ReportUnsupportedAttributes(
                        path,
                        source,
                        securityChild,
                        builder,
                        "clientCredentialType");
                    evidenceIds.Add(builder.AddEvidence(
                        path,
                        source.OpeningTagSpan(securityChild),
                        WcfVocabulary.Rules.ConfigurationBindingSecurity,
                        ResolutionBasis.DeclarativeConfiguration,
                        ResolutionQuality.Exact,
                        "An allowlisted binding message-security declaration was observed as inert configuration metadata."));
                    messageClientCredentialType ??= Literal(securityChild, "clientCredentialType");
                }
                else
                {
                    ReportUnsupportedElement(path, source, securityChild, builder);
                }
            }
        }

        return new BindingSecurity(
            mode,
            transportClientCredentialType,
            messageClientCredentialType,
            evidenceIds.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static void ParseBehaviors(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfBehaviorObservation> behaviors)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        foreach (var family in container.Elements())
        {
            var kind = family.Name.NamespaceName.Length == 0
                ? family.Name.LocalName switch
                {
                    "serviceBehaviors" => "service",
                    "endpointBehaviors" => "endpoint",
                    _ => null,
                }
                : null;
            if (kind is null)
            {
                ReportUnsupportedElement(path, source, family, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, family, builder);
            var allowlist = kind == "service" ? ServiceBehaviorElements : EndpointBehaviorElements;
            foreach (var element in family.Elements())
            {
                if (!IsElement(element, "behavior"))
                {
                    ReportUnsupportedElement(path, source, element, builder);
                    continue;
                }

                ReportUnsupportedAttributes(path, source, element, builder, "name");
                var name = Literal(element, "name");
                var children = new SortedSet<string>(StringComparer.Ordinal);
                var childEvidenceIds = new List<string>();
                foreach (var child in element.Elements())
                {
                    if (child.Name.NamespaceName.Length == 0 && allowlist.Contains(child.Name.LocalName))
                    {
                        children.Add(child.Name.LocalName);
                        childEvidenceIds.Add(builder.AddEvidence(
                            path,
                            source.OpeningTagSpan(child),
                            WcfVocabulary.Rules.ConfigurationBehaviorElement,
                            ResolutionBasis.DeclarativeConfiguration,
                            ResolutionQuality.Exact,
                            "An allowlisted behavior child kind was observed as inert configuration metadata."));
                        ReportUnsupportedBehaviorMetadata(path, source, child, builder);
                    }
                    else
                    {
                        ReportUnsupportedElement(path, source, child, builder);
                    }
                }

                var span = source.OpeningTagSpan(element);
                var evidenceId = builder.AddEvidence(
                    path,
                    span,
                    WcfVocabulary.Rules.ConfigurationBehavior,
                    ResolutionBasis.DeclarativeConfiguration,
                    ResolutionQuality.Exact);
                var displayName = name ?? "(unnamed behavior)";
                var node = builder.AddNode(
                    WcfVocabulary.NodeKinds.Behavior,
                    $"{kind}:{displayName}",
                    QualifiedIdentity(path, $"{kind}-behavior", displayName, span.StartOffset),
                    projectId: null,
                    evidenceIds: new[] { evidenceId }.Concat(childEvidenceIds).ToArray(),
                    properties: Properties(
                        (WcfVocabulary.Properties.ConfigBehaviorKind, kind),
                        (WcfVocabulary.Properties.ConfigBehaviorName, name),
                        (WcfVocabulary.Properties.BehaviorElements,
                            children.Count == 0 ? null : string.Join(",", children))));
                behaviors.Add(new WcfBehaviorObservation(
                    path,
                    span.StartOffset,
                    node,
                    evidenceId,
                    kind,
                    name,
                    children.ToArray()));
            }
        }
    }

    private static void ParseHosting(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfServiceActivationObservation> activations)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        foreach (var child in container.Elements())
        {
            if (!IsElement(child, "serviceActivations"))
            {
                ReportUnsupportedElement(path, source, child, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, child, builder);
            foreach (var element in child.Elements())
            {
                if (!IsElement(element, "add"))
                {
                    ReportUnsupportedElement(path, source, element, builder);
                    continue;
                }

                ReportUnsupportedAttributes(
                    path,
                    source,
                    element,
                    builder,
                    "factory",
                    "relativeAddress",
                    "service");
                var relativeAddress = Literal(element, "relativeAddress");
                var service = Literal(element, "service");
                var factory = Literal(element, "factory");
                var span = source.OpeningTagSpan(element);
                var evidenceId = builder.AddEvidence(
                    path,
                    span,
                    WcfVocabulary.Rules.ConfigurationActivation,
                    ResolutionBasis.DeclarativeConfiguration,
                    ResolutionQuality.Exact);
                var displayName = relativeAddress ?? service ?? "(unnamed activation)";
                var node = builder.AddNode(
                    WcfVocabulary.NodeKinds.ServiceActivation,
                    displayName,
                    QualifiedIdentity(path, "service-activation", displayName, span.StartOffset),
                    projectId: null,
                    evidenceIds: [evidenceId],
                    properties: Properties(
                        (WcfVocabulary.Properties.ConfigRelativeAddress, relativeAddress),
                        (WcfVocabulary.Properties.Service, service),
                        (WcfVocabulary.Properties.Factory, factory)));
                activations.Add(new WcfServiceActivationObservation(
                    path,
                    span.StartOffset,
                    node,
                    evidenceId,
                    relativeAddress,
                    service,
                    factory));
            }
        }
    }

    private static void ParseExtensions(
        string path,
        WcfTextDocument source,
        XElement container,
        WcfContributionBuilder builder,
        ICollection<WcfExtensionObservation> extensions)
    {
        ReportUnsupportedAttributes(path, source, container, builder);
        var supportedContainers = new HashSet<string>(StringComparer.Ordinal)
        {
            "behaviorExtensions",
            "bindingElementExtensions",
            "bindingExtensions",
            "endpointExtensions",
        };
        foreach (var category in container.Elements())
        {
            if (category.Name.NamespaceName.Length != 0 ||
                !supportedContainers.Contains(category.Name.LocalName))
            {
                ReportUnsupportedElement(path, source, category, builder);
                continue;
            }

            ReportUnsupportedAttributes(path, source, category, builder);
            foreach (var element in category.Elements())
            {
                if (!IsElement(element, "add"))
                {
                    ReportUnsupportedElement(path, source, element, builder);
                    continue;
                }

                ReportUnsupportedAttributes(path, source, element, builder, "name", "type");
                var name = Literal(element, "name");
                var type = Literal(element, "type");
                var span = source.OpeningTagSpan(element);
                var evidenceId = builder.AddEvidence(
                    path,
                    span,
                    WcfVocabulary.Rules.ConfigurationExtension,
                    ResolutionBasis.DeclarativeConfiguration,
                    ResolutionQuality.Exact,
                    "The extension declaration was observed as inert metadata; its type was not loaded.");
                var displayName = name ?? type ?? "(unnamed extension)";
                var node = builder.AddNode(
                    WcfVocabulary.NodeKinds.ExtensionDeclaration,
                    displayName,
                    QualifiedIdentity(
                        path,
                        $"extension-{category.Name.LocalName}",
                        displayName,
                        span.StartOffset),
                    projectId: null,
                    evidenceIds: [evidenceId],
                    properties: Properties(
                        (WcfVocabulary.Properties.ConfigExtensionName, name),
                        (WcfVocabulary.Properties.ExtensionType, type)));
                extensions.Add(new WcfExtensionObservation(
                    path,
                    span.StartOffset,
                    node,
                    evidenceId,
                    category.Name.LocalName,
                    name,
                    type));
                builder.AddDiagnostic(
                    WcfVocabulary.Diagnostics.ExtensionDetected,
                    DiagnosticSeverity.Warning,
                    "A custom WCF extension declaration was detected as inert metadata and was not interpreted or instantiated.",
                    path,
                    [evidenceId],
                    Properties(
                        (WcfVocabulary.Properties.ConfigExtensionName, name),
                        (WcfVocabulary.Properties.ExtensionType, type)));
            }
        }
    }

    private static void ResolveDeclarativeRelationships(
        WcfContributionBuilder builder,
        IEnumerable<WcfConfiguredServiceObservation> services,
        IEnumerable<WcfConfiguredEndpointObservation> endpoints,
        IEnumerable<WcfBindingObservation> bindings,
        IEnumerable<WcfBehaviorObservation> behaviors)
    {
        var bindingIndex = bindings
            .Where(item => item.Name is not null)
            .GroupBy(
                item => new ConfigKey(item.RelativePath, item.Family, item.Name!),
                ConfigKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.OrderBy(ItemOrder).ToArray(), ConfigKeyComparer.Instance);
        var defaultBindingIndex = bindings
            .Where(item => item.Name is null)
            .GroupBy(
                item => new ConfigKey(item.RelativePath, item.Family, string.Empty),
                ConfigKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.OrderBy(ItemOrder).ToArray(), ConfigKeyComparer.Instance);
        var behaviorIndex = behaviors
            .Where(item => item.Name is not null)
            .GroupBy(
                item => new ConfigKey(item.RelativePath, item.Kind, item.Name!),
                ConfigKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.OrderBy(ItemOrder).ToArray(), ConfigKeyComparer.Instance);

        foreach (var endpoint in endpoints.OrderBy(ItemOrder))
        {
            if (endpoint.Binding is not null || endpoint.BindingConfiguration is not null)
            {
                var target = endpoint.BindingConfiguration is null
                    ? $"{endpoint.Binding ?? "<unspecified-binding>"}:<default>"
                    : $"{endpoint.Binding ?? "<unspecified-binding>"}:{endpoint.BindingConfiguration}";
                IReadOnlyList<WcfBindingObservation> candidates = endpoint.Binding is null
                    ? []
                    : endpoint.BindingConfiguration is null
                        ? defaultBindingIndex.GetValueOrDefault(
                            new ConfigKey(endpoint.RelativePath, endpoint.Binding, string.Empty),
                            [])
                        : bindingIndex.GetValueOrDefault(
                            new ConfigKey(endpoint.RelativePath, endpoint.Binding, endpoint.BindingConfiguration),
                            []);
                AddConfigurationRelationship(
                    builder,
                    endpoint.RelativePath,
                    endpoint.Node.NodeId,
                    candidates.Select(item => item.Node.NodeId).ToArray(),
                    target,
                    WcfVocabulary.EdgeKinds.EndpointBinding,
                    WcfVocabulary.Rules.ConfigurationEndpointBinding,
                    WcfVocabulary.Diagnostics.BindingUnresolved,
                    "binding configuration",
                    endpoint.EvidenceId);
            }

            if (endpoint.BehaviorConfiguration is not null)
            {
                var candidates = behaviorIndex.GetValueOrDefault(
                    new ConfigKey(endpoint.RelativePath, "endpoint", endpoint.BehaviorConfiguration),
                    []);
                AddConfigurationRelationship(
                    builder,
                    endpoint.RelativePath,
                    endpoint.Node.NodeId,
                    candidates.Select(item => item.Node.NodeId).ToArray(),
                    endpoint.BehaviorConfiguration,
                    WcfVocabulary.EdgeKinds.EndpointBehavior,
                    WcfVocabulary.Rules.ConfigurationEndpointBehavior,
                    WcfVocabulary.Diagnostics.BehaviorUnresolved,
                    "endpoint behavior configuration",
                    endpoint.EvidenceId);
            }
        }

        foreach (var service in services.OrderBy(ItemOrder))
        {
            if (service.BehaviorConfiguration is null)
            {
                continue;
            }

            var candidates = behaviorIndex.GetValueOrDefault(
                new ConfigKey(service.RelativePath, "service", service.BehaviorConfiguration),
                []);
            AddConfigurationRelationship(
                builder,
                service.RelativePath,
                service.Node.NodeId,
                candidates.Select(item => item.Node.NodeId).ToArray(),
                service.BehaviorConfiguration,
                WcfVocabulary.EdgeKinds.ServiceBehavior,
                WcfVocabulary.Rules.ConfigurationServiceBehavior,
                WcfVocabulary.Diagnostics.BehaviorUnresolved,
                "service behavior configuration",
                service.EvidenceId);
        }
    }

    private static void AddConfigurationRelationship(
        WcfContributionBuilder builder,
        string path,
        string fromNodeId,
        IReadOnlyList<string> candidateNodeIds,
        string textualTarget,
        string edgeKind,
        string ruleId,
        string diagnosticCode,
        string targetDescription,
        string sourceEvidenceId)
    {
        var quality = candidateNodeIds.Count switch
        {
            0 => ResolutionQuality.Unresolved,
            1 => ResolutionQuality.Exact,
            _ => ResolutionQuality.Ambiguous,
        };
        var details = candidateNodeIds.Count switch
        {
            0 => "No matching declaration was found in the same captured configuration file.",
            1 => "One declaration in the same captured configuration file matched exactly.",
            _ => "Multiple matching declarations were found in the same captured configuration file.",
        };
        var declarationEvidence = builder.GetContributionEvidence(sourceEvidenceId);
        var relationshipEvidenceId = builder.AddEvidence(
            path,
            declarationEvidence.Span,
            ruleId,
            ResolutionBasis.DeclarativeConfiguration,
            quality,
            details);
        if (candidateNodeIds.Count == 1)
        {
            builder.AddEdge(
                edgeKind,
                fromNodeId,
                candidateNodeIds[0],
                unresolvedTarget: null,
                [relationshipEvidenceId],
                ResolutionBasis.DeclarativeConfiguration,
                quality,
                details);
            return;
        }

        builder.AddEdge(
            edgeKind,
            fromNodeId,
            toNodeId: null,
            textualTarget,
            [relationshipEvidenceId],
            ResolutionBasis.DeclarativeConfiguration,
            quality,
            details);
        builder.AddDiagnostic(
            diagnosticCode,
            DiagnosticSeverity.Warning,
            candidateNodeIds.Count == 0
                ? $"The {targetDescription} '{textualTarget}' could not be resolved in the same configuration file."
                : $"The {targetDescription} '{textualTarget}' matched multiple declarations in the same configuration file.",
            path,
            [relationshipEvidenceId],
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidateCount"] = candidateNodeIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["target"] = textualTarget,
            });
    }

    private static void ReportExternalReferences(
        string path,
        WcfTextDocument source,
        XElement serviceModel,
        WcfContributionBuilder builder)
    {
        foreach (var attribute in serviceModel.DescendantsAndSelf()
                     .Attributes()
                     .Where(attribute =>
                         attribute.Name.NamespaceName.Length == 0 &&
                         attribute.Name.LocalName is "configSource" or "file")
                     .OrderBy(attribute => source.AttributeSpan(attribute).StartOffset))
        {
            var span = source.AttributeSpan(attribute);
            var evidenceId = builder.AddEvidence(
                path,
                span,
                WcfVocabulary.Rules.ConfigurationExternalReference,
                ResolutionBasis.DeclarativeConfiguration,
                ResolutionQuality.Partial,
                "An external configuration reference was retained as inert text and was not followed.");
            builder.AddDiagnostic(
                WcfVocabulary.Diagnostics.ExternalConfigurationNotResolved,
                DiagnosticSeverity.Warning,
                $"External WCF configuration reference '{attribute.Value.Trim()}' was not resolved or fetched.",
                path,
                [evidenceId],
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["attribute"] = attribute.Name.LocalName,
                    ["target"] = attribute.Value.Trim(),
                });
        }
    }

    private static void ReportUnsupportedBehaviorMetadata(
        string path,
        WcfTextDocument source,
        XElement supportedChild,
        WcfContributionBuilder builder)
    {
        foreach (var attribute in supportedChild.Attributes()
                     .Where(attribute =>
                         !attribute.IsNamespaceDeclaration &&
                         attribute.Name.NamespaceName.Length == 0)
                     .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            var message = IsElement(supportedChild, "serviceMetadata") &&
                          string.Equals(
                              attribute.Name.LocalName,
                              "externalMetadataLocation",
                              StringComparison.Ordinal)
                ? "Remote metadata locations are retained as inert text and are not fetched."
                : $"Behavior attribute '{attribute.Name.LocalName}' is outside the allowlisted metadata-only subset.";
            ReportUnsupportedAttribute(path, source, attribute, builder, message);
        }

        foreach (var nested in supportedChild.Elements())
        {
            ReportUnsupportedSubtree(path, source, nested, builder);
        }
    }

    private static void ReportUnsupportedSubtree(
        string path,
        WcfTextDocument source,
        XElement element,
        WcfContributionBuilder builder)
    {
        var pending = new Stack<XElement>();
        pending.Push(element);
        var visited = 0;
        while (pending.Count > 0)
        {
            if (visited >= MaximumUnsupportedBehaviorElements)
            {
                ReportTraversalBudgetExceeded(
                    path,
                    source,
                    pending.Peek(),
                    builder,
                    "unsupported behavior metadata",
                    MaximumUnsupportedBehaviorElements);
                return;
            }

            var current = pending.Pop();
            visited++;
            ReportUnsupportedElement(path, source, current, builder);
            foreach (var attribute in current.Attributes()
                         .Where(attribute =>
                             !attribute.IsNamespaceDeclaration &&
                             attribute.Name.NamespaceName.Length == 0)
                         .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
            {
                ReportUnsupportedAttribute(
                    path,
                    source,
                    attribute,
                    builder,
                    $"Nested behavior attribute '{attribute.Name.LocalName}' is outside the allowlisted metadata-only subset.");
            }

            var children = current.Elements().ToArray();
            for (var index = children.Length - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    private static NamespaceMetadataInspection InspectNamespaceQualifiedMetadata(
        string path,
        WcfTextDocument source,
        XElement serviceModel,
        WcfContributionBuilder builder)
    {
        var pending = new Stack<XElement>();
        pending.Push(serviceModel);
        var visited = 0;
        var hasTransformMetadata = false;

        while (pending.Count > 0)
        {
            if (visited >= MaximumNamespaceInspectionElements)
            {
                ReportTraversalBudgetExceeded(
                    path,
                    source,
                    pending.Peek(),
                    builder,
                    "namespace-qualified configuration metadata",
                    MaximumNamespaceInspectionElements);
                return new NamespaceMetadataInspection(
                    HasTransformMetadata: hasTransformMetadata,
                    CompletedWithinBudget: false);
            }

            var current = pending.Pop();
            visited++;
            if (string.Equals(
                    current.Name.NamespaceName,
                    XmlDocumentTransformNamespace,
                    StringComparison.Ordinal) &&
                !hasTransformMetadata)
            {
                hasTransformMetadata = true;
                ReportTransformNotApplied(
                    path,
                    source,
                    source.OpeningTagSpan(current),
                    $"element '{current.Name.LocalName}'",
                    builder);
            }

            foreach (var attribute in current.Attributes()
                         .Where(attribute =>
                             !attribute.IsNamespaceDeclaration &&
                             attribute.Name.NamespaceName.Length != 0)
                         .OrderBy(attribute => source.AttributeSpan(attribute).StartOffset))
            {
                ReportUnsupportedAttribute(
                    path,
                    source,
                    attribute,
                    builder,
                    $"Namespace-qualified configuration attribute '{attribute.Name}' is unsupported inert metadata.");
                if (string.Equals(
                        attribute.Name.NamespaceName,
                        XmlDocumentTransformNamespace,
                        StringComparison.Ordinal) &&
                    !hasTransformMetadata)
                {
                    hasTransformMetadata = true;
                    ReportTransformNotApplied(
                        path,
                        source,
                        source.AttributeSpan(attribute),
                        $"attribute '{attribute.Name.LocalName}'",
                        builder);
                }
            }

            var children = current.Elements().ToArray();
            for (var index = children.Length - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }

        return new NamespaceMetadataInspection(
            HasTransformMetadata: hasTransformMetadata,
            CompletedWithinBudget: true);
    }

    private static void ReportTransformNotApplied(
        string path,
        WcfTextDocument source,
        SourceSpan span,
        string metadataDescription,
        WcfContributionBuilder builder)
    {
        var evidenceId = builder.AddEvidence(
            path,
            span,
            WcfVocabulary.Rules.ConfigurationTransform,
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Partial,
            $"XML Document Transform {metadataDescription} was observed as inert metadata and was not applied.");
        builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.ConfigurationTransformNotApplied,
            DiagnosticSeverity.Warning,
            "An XML Document Transform control was retained as inert metadata; the transform was not applied and declarations from this system.serviceModel section were not promoted.",
            path,
            [evidenceId]);
    }

    private static void ReportTraversalBudgetExceeded(
        string path,
        WcfTextDocument source,
        XElement firstUnvisited,
        WcfContributionBuilder builder,
        string scope,
        int limit)
    {
        var evidenceId = builder.AddEvidence(
            path,
            source.OpeningTagSpan(firstUnvisited),
            WcfVocabulary.Rules.ConfigurationTraversalLimit,
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Partial,
            $"The bounded {scope} traversal stopped after {limit.ToString(System.Globalization.CultureInfo.InvariantCulture)} elements.");
        builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.ConfigurationTraversalLimitExceeded,
            DiagnosticSeverity.Warning,
            $"The bounded {scope} traversal reached its {limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}-element limit; remaining metadata was not interpreted.",
            path,
            [evidenceId],
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["scope"] = scope,
            });
    }

    private static void ReportUnsupportedAttributes(
        string path,
        WcfTextDocument source,
        XElement element,
        WcfContributionBuilder builder,
        params string[] allowed)
    {
        var allowlist = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes()
                     .Where(attribute =>
                         !attribute.IsNamespaceDeclaration &&
                         attribute.Name.NamespaceName.Length == 0 &&
                         !allowlist.Contains(attribute.Name.LocalName) &&
                         attribute.Name.LocalName is not ("configSource" or "file"))
                     .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            ReportUnsupportedAttribute(
                path,
                source,
                attribute,
                builder,
                $"Configuration attribute '{attribute.Name.LocalName}' is outside the allowlisted WCF subset.");
        }
    }

    private static void ReportUnsupportedAttribute(
        string path,
        WcfTextDocument source,
        XAttribute attribute,
        WcfContributionBuilder builder,
        string message)
    {
        var evidenceId = builder.AddEvidence(
            path,
            source.AttributeSpan(attribute),
            WcfVocabulary.Rules.ConfigurationUnsupportedAttribute,
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Partial,
            "The configuration syntax is outside the allowlisted WCF subset.");
        builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.UnsupportedConfiguration,
            DiagnosticSeverity.Warning,
            message,
            path,
            [evidenceId]);
    }

    private static void ReportUnsupportedElement(
        string path,
        WcfTextDocument source,
        XElement element,
        WcfContributionBuilder builder)
    {
        var evidenceId = builder.AddEvidence(
            path,
            source.OpeningTagSpan(element),
            WcfVocabulary.Rules.ConfigurationUnsupportedElement,
            ResolutionBasis.DeclarativeConfiguration,
            ResolutionQuality.Partial,
            "The configuration element is outside the allowlisted WCF subset.");
        builder.AddDiagnostic(
            WcfVocabulary.Diagnostics.UnsupportedConfiguration,
            DiagnosticSeverity.Warning,
            $"Configuration element '{element.Name}' is outside the allowlisted WCF subset.",
            path,
            [evidenceId]);
    }

    private static bool IsElement(XElement element, string localName) =>
        element.Name.NamespaceName.Length == 0 &&
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string? Literal(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string QualifiedIdentity(string path, string kind, string? name, int startOffset) =>
        $"wcf-config|{path}|{kind}|{name ?? "<unnamed>"}|{startOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

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

    private static (string RelativePath, int StartOffset, string NodeId) ItemOrder(
        WcfConfigurationObservation item) =>
        (item.RelativePath, item.StartOffset, item.Node.NodeId);

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record BindingSecurity(
        string? Mode,
        string? TransportClientCredentialType,
        string? MessageClientCredentialType,
        IReadOnlyList<string> EvidenceIds);

    private sealed record EndpointIdentityMetadata(
        IReadOnlyList<string> Kinds,
        IReadOnlyList<string> EvidenceIds);

    private sealed record NamespaceMetadataInspection(
        bool HasTransformMetadata,
        bool CompletedWithinBudget);

    private sealed record ConfigKey(string Path, string Kind, string Name);

    private sealed class ConfigKeyComparer : IEqualityComparer<ConfigKey>
    {
        public static ConfigKeyComparer Instance { get; } = new();

        public bool Equals(ConfigKey? x, ConfigKey? y) =>
            ReferenceEquals(x, y) ||
            (x is not null && y is not null &&
             string.Equals(x.Path, y.Path, StringComparison.Ordinal) &&
             string.Equals(x.Kind, y.Kind, StringComparison.Ordinal) &&
             string.Equals(x.Name, y.Name, StringComparison.Ordinal));

        public int GetHashCode(ConfigKey obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Path),
                StringComparer.Ordinal.GetHashCode(obj.Kind),
                StringComparer.Ordinal.GetHashCode(obj.Name));
    }
}

internal abstract record WcfConfigurationObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId);

internal sealed record WcfConfiguredServiceObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string? Name,
    string? BehaviorConfiguration)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed record WcfConfiguredEndpointObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string Direction,
    string? ConfiguredServiceNodeId,
    string? Name,
    string? Address,
    string? Contract,
    string? Binding,
    string? BindingConfiguration,
    string? BehaviorConfiguration,
    IReadOnlyList<string> IdentityKinds)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed record WcfBindingObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string Family,
    string? Name)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed record WcfBehaviorObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string Kind,
    string? Name,
    IReadOnlyList<string> Elements)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed record WcfServiceActivationObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string? RelativeAddress,
    string? Service,
    string? Factory)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed record WcfExtensionObservation(
    string RelativePath,
    int StartOffset,
    EvidenceNode Node,
    string EvidenceId,
    string Category,
    string? Name,
    string? Type)
    : WcfConfigurationObservation(RelativePath, StartOffset, Node, EvidenceId);

internal sealed class WcfConfigurationAnalysisResult
{
    public WcfConfigurationAnalysisResult(
        IEnumerable<WcfConfiguredServiceObservation> services,
        IEnumerable<WcfConfiguredEndpointObservation> endpoints,
        IEnumerable<WcfBindingObservation> bindings,
        IEnumerable<WcfBehaviorObservation> behaviors,
        IEnumerable<WcfServiceActivationObservation> activations,
        IEnumerable<WcfExtensionObservation> extensions)
    {
        Services = Sort(services);
        Endpoints = Sort(endpoints);
        Bindings = Sort(bindings);
        Behaviors = Sort(behaviors);
        Activations = Sort(activations);
        Extensions = Sort(extensions);
        ServicesByName = Index(Services, item => item.Name);
        EndpointsByContract = Index(Endpoints, item => item.Contract);
        ActivationsByService = Index(Activations, item => item.Service);
    }

    public IReadOnlyList<WcfConfiguredServiceObservation> Services { get; }
    public IReadOnlyList<WcfConfiguredEndpointObservation> Endpoints { get; }
    public IReadOnlyList<WcfBindingObservation> Bindings { get; }
    public IReadOnlyList<WcfBehaviorObservation> Behaviors { get; }
    public IReadOnlyList<WcfServiceActivationObservation> Activations { get; }
    public IReadOnlyList<WcfExtensionObservation> Extensions { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<WcfConfiguredServiceObservation>> ServicesByName { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<WcfConfiguredEndpointObservation>> EndpointsByContract { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<WcfServiceActivationObservation>> ActivationsByService { get; }

    private static IReadOnlyList<T> Sort<T>(IEnumerable<T> items)
        where T : WcfConfigurationObservation =>
        items.OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.StartOffset)
            .ThenBy(item => item.Node.NodeId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<T>> Index<T>(
        IEnumerable<T> items,
        Func<T, string?> keySelector)
        where T : WcfConfigurationObservation
    {
        var result = new SortedDictionary<string, IReadOnlyList<T>>(StringComparer.Ordinal);
        foreach (var group in items
                     .Select(item => (Item: item, Key: keySelector(item)))
                     .Where(pair => pair.Key is not null)
                     .GroupBy(pair => pair.Key!, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            result[group.Key] = group.Select(pair => pair.Item).ToArray();
        }

        return result;
    }
}
