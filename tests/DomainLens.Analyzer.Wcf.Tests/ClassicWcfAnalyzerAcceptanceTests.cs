using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class ClassicWcfAnalyzerAcceptanceTests
{
    [Fact]
    public async Task Basic_fixture_projects_contract_operation_fault_data_and_implementation_evidence()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var document = await AnalyzeAsync(fixture.Path);

        var contract = Node(document, "Fixtures.Wcf.Basic.ICustomerService");
        Assert.Contains(WcfVocabulary.Attributes.ServiceContract, contract.Attributes);
        Assert.Equal("CustomerContract", contract.Properties[WcfVocabulary.Properties.ServiceContractName]);
        Assert.Equal(
            "urn:domainlens:fixtures:basic",
            contract.Properties[WcfVocabulary.Properties.ServiceContractNamespace]);
        Assert.Equal(
            "Fixtures.Wcf.Basic.ICustomerService",
            contract.Properties[WcfVocabulary.Properties.ServiceContractConfigurationName]);

        var operation = Assert.Single(document.Nodes, node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.OperationContract) &&
            node.QualifiedName.Contains("ICustomerService.Update(", StringComparison.Ordinal));
        Assert.Equal("UpdateCustomer", operation.Properties[WcfVocabulary.Properties.OperationName]);
        Assert.Equal(
            "urn:domainlens:fixtures:basic/update",
            operation.Properties[WcfVocabulary.Properties.OperationAction]);
        Assert.Equal(
            "urn:domainlens:fixtures:basic/update-response",
            operation.Properties[WcfVocabulary.Properties.OperationReplyAction]);
        Assert.Contains(
            "Fixtures.Wcf.Basic.CustomerRequest",
            operation.Properties[WcfVocabulary.Properties.ParameterTypes],
            StringComparison.Ordinal);
        Assert.Equal(
            "Fixtures.Wcf.Basic.CustomerResponse",
            operation.Properties[WcfVocabulary.Properties.ReturnType]);

        AssertResolvedEdge(document, WcfVocabulary.EdgeKinds.ContractOperation, contract, operation);

        var fault = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.FaultDeclaration);
        Assert.Equal("Fixtures.Wcf.Basic.CustomerFault", fault.Properties[WcfVocabulary.Properties.DetailType]);
        AssertResolvedEdge(document, WcfVocabulary.EdgeKinds.DeclaresFault, operation, fault);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.FaultDetailType &&
            edge.FromNodeId == fault.NodeId &&
            edge.ToNodeId == Node(document, "Fixtures.Wcf.Basic.CustomerFault").NodeId);

        var request = Node(document, "Fixtures.Wcf.Basic.CustomerRequest");
        var response = Node(document, "Fixtures.Wcf.Basic.CustomerResponse");
        Assert.Contains(WcfVocabulary.Attributes.DataContract, request.Attributes);
        Assert.Contains(WcfVocabulary.Attributes.DataContract, response.Attributes);
        Assert.Equal("true", request.Properties[WcfVocabulary.Properties.DataContractIsReference]);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.UsesDataContract &&
            edge.FromNodeId == operation.NodeId &&
            edge.ToNodeId == request.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.UsesDataContract &&
            edge.FromNodeId == operation.NodeId &&
            edge.ToNodeId == response.NodeId);
        Assert.True(document.Nodes.Count(node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.DataMember)) >= 4);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.DataMember) >= 4);
        var customerId = MemberNode(document, "Fixtures.Wcf.Basic.CustomerRequest.CustomerId");
        Assert.Contains(WcfVocabulary.Attributes.DataMember, customerId.Attributes);
        Assert.Equal("customerId", customerId.Properties[WcfVocabulary.Properties.DataMemberName]);
        Assert.Equal("1", customerId.Properties[WcfVocabulary.Properties.DataMemberOrder]);
        Assert.Equal("true", customerId.Properties[WcfVocabulary.Properties.DataMemberIsRequired]);
        Assert.Equal("false", customerId.Properties[WcfVocabulary.Properties.DataMemberEmitDefaultValue]);

        var implementation = Node(document, "Fixtures.Wcf.Basic.CustomerService");
        var implementationEdge = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ImplementsContract &&
            edge.FromNodeId == implementation.NodeId &&
            edge.ToNodeId == contract.NodeId);
        Assert.Equal(ResolutionBasis.Semantic, implementationEdge.Resolution.Basis);
        Assert.Equal(ResolutionQuality.Partial, implementationEdge.Resolution.Quality);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ImplementsOperation &&
            edge.ToNodeId == operation.NodeId);

        AssertValid(document);
    }

    [Fact]
    public async Task Rich_fixture_supports_attribute_spellings_callback_overloads_and_message_contracts()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfRich");
        var document = await AnalyzeAsync(fixture.Path);

        var callback = Node(document, "Fixtures.Wcf.Rich.IProgressCallback");
        var rich = Node(document, "Fixtures.Wcf.Rich.IRichService");
        var fullyQualified = Node(document, "Fixtures.Wcf.Rich.IFullyQualifiedContract");
        var partial = Node(document, "Fixtures.Wcf.Rich.IPartialContract");
        Assert.All(
            new[] { rich, fullyQualified, partial },
            node => Assert.Contains(WcfVocabulary.Attributes.ServiceContract, node.Attributes));
        Assert.DoesNotContain(WcfVocabulary.Attributes.ServiceContract, callback.Attributes);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.CallbackContract &&
            edge.FromNodeId == rich.NodeId &&
            edge.ToNodeId == callback.NodeId);
        var reportOperation = Assert.Single(document.Nodes, node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.OperationContract) &&
            node.QualifiedName.Contains("IProgressCallback.Report(", StringComparison.Ordinal));
        Assert.Equal("true", reportOperation.Properties[WcfVocabulary.Properties.OperationIsOneWay]);

        var submitOperations = document.Nodes.Where(node =>
            node.Attributes.Contains(WcfVocabulary.Attributes.OperationContract) &&
            node.QualifiedName.Contains("IRichService.Submit(", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, submitOperations.Length);
        Assert.Contains(
            submitOperations,
            node => node.Properties[WcfVocabulary.Properties.OperationName] == "SubmitMessage");
        Assert.Contains(
            submitOperations,
            node => node.Properties[WcfVocabulary.Properties.OperationName] == "SubmitText");
        var submitText = Assert.Single(submitOperations, node =>
            node.Properties[WcfVocabulary.Properties.OperationName] == "SubmitText");
        Assert.Equal("true", submitText.Properties[WcfVocabulary.Properties.OperationIsInitiating]);
        Assert.Equal("false", submitText.Properties[WcfVocabulary.Properties.OperationIsTerminating]);

        var request = Node(document, "Fixtures.Wcf.Rich.SubmitRequest");
        var response = Node(document, "Fixtures.Wcf.Rich.SubmitResponse");
        Assert.Contains(WcfVocabulary.Attributes.MessageContract, request.Attributes);
        Assert.Contains(WcfVocabulary.Attributes.MessageContract, response.Attributes);
        Assert.Equal("true", request.Properties[WcfVocabulary.Properties.MessageIsWrapped]);
        Assert.Equal("false", response.Properties[WcfVocabulary.Properties.MessageIsWrapped]);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.MessageHeader) >= 2);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.MessageBodyMember) >= 3);
        var correlationHeader = MemberNode(document, "Fixtures.Wcf.Rich.SubmitRequest.CorrelationId");
        Assert.Contains(WcfVocabulary.Attributes.MessageHeader, correlationHeader.Attributes);
        Assert.Equal(
            "correlationId",
            correlationHeader.Properties[WcfVocabulary.Properties.MessageHeaderName]);
        Assert.Equal(
            "urn:domainlens:actors:client",
            correlationHeader.Properties[WcfVocabulary.Properties.MessageHeaderActor]);
        Assert.Equal("true", correlationHeader.Properties[WcfVocabulary.Properties.MessageHeaderMustUnderstand]);
        Assert.Equal("true", correlationHeader.Properties[WcfVocabulary.Properties.MessageHeaderRelay]);
        var valueBody = MemberNode(document, "Fixtures.Wcf.Rich.SubmitRequest.Value");
        Assert.Contains(WcfVocabulary.Attributes.MessageBodyMember, valueBody.Attributes);
        Assert.Equal("1", valueBody.Properties[WcfVocabulary.Properties.MessageBodyMemberOrder]);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.UsesMessageContract &&
            edge.ToNodeId == request.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.UsesMessageContract &&
            edge.ToNodeId == response.NodeId);

        var serviceContractEvidence = WcfEvidence(document)
            .Where(evidence => evidence.Provenance.RuleId == WcfVocabulary.Rules.ServiceContract)
            .ToArray();
        Assert.Equal(3, serviceContractEvidence.Length);
        Assert.All(serviceContractEvidence, evidence =>
        {
            Assert.Equal(ResolutionBasis.Semantic, evidence.Resolution.Basis);
            Assert.Equal(ResolutionQuality.Exact, evidence.Resolution.Quality);
        });
        Assert.DoesNotContain(
            document.Diagnostics,
            diagnostic => diagnostic.Code == WcfVocabulary.Diagnostics.AttributeIdentityUnresolved);
        var partialEvidence = Assert.Single(serviceContractEvidence, evidence =>
            evidence.RelativePath == "PartialContract.Attribute.cs");
        var partialSource = await File.ReadAllTextAsync(
            System.IO.Path.Combine(fixture.Path, partialEvidence.RelativePath));
        Assert.Contains(
            "ServiceContract",
            partialSource.Substring(partialEvidence.Span.StartOffset, partialEvidence.Span.Length),
            StringComparison.Ordinal);
        AssertValid(document);
    }

    [Fact]
    public async Task Inheritance_fixture_correlates_inherited_and_explicit_implementations_without_guessing()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfInheritance");
        var document = await AnalyzeAsync(fixture.Path);

        var baseContract = Node(document, "Fixtures.Wcf.Inheritance.IBaseContract");
        var derivedContract = Node(document, "Fixtures.Wcf.Inheritance.IDerivedContract");
        var explicitService = Node(document, "Fixtures.Wcf.Inheritance.ExplicitService");
        var inheritedService = Node(document, "Fixtures.Wcf.Inheritance.InheritedService");
        var alternativeService = Node(document, "Fixtures.Wcf.Inheritance.AlternativeService");

        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ContractOperation &&
            edge.FromNodeId == derivedContract.NodeId &&
            edge.ToNodeId is not null &&
            NodeById(document, edge.ToNodeId).QualifiedName.Contains("IBaseContract.Ping(", StringComparison.Ordinal));

        foreach (var implementation in new[] { explicitService, inheritedService, alternativeService })
        {
            Assert.Contains(document.Edges, edge =>
                edge.Kind == WcfVocabulary.EdgeKinds.ImplementsContract &&
                edge.FromNodeId == implementation.NodeId &&
                edge.ToNodeId == derivedContract.NodeId);
        }

        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ImplementsContract &&
            edge.FromNodeId == explicitService.NodeId &&
            edge.ToNodeId == baseContract.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ImplementsOperation &&
            NodeById(document, edge.FromNodeId).QualifiedName.Contains("ExplicitService.", StringComparison.Ordinal) &&
            NodeById(document, edge.FromNodeId).QualifiedName.Contains("Ping(", StringComparison.Ordinal));
        Assert.All(
            document.Edges.Where(edge =>
                edge.Kind is WcfVocabulary.EdgeKinds.ImplementsContract or
                    WcfVocabulary.EdgeKinds.ImplementsOperation),
            edge => Assert.Equal(ResolutionQuality.Partial, edge.Resolution.Quality));
        AssertValid(document);
    }

    [Fact]
    public async Task Hosting_fixture_discovers_svc_configuration_and_resolves_bounded_relationships()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfHostingConfig");
        var document = await AnalyzeAsync(fixture.Path);

        var hostingNodes = document.Nodes
            .Where(node => node.Kind == WcfVocabulary.NodeKinds.HostingDeclaration)
            .ToArray();
        Assert.Equal(2, hostingNodes.Length);
        var hosted = Assert.Single(hostingNodes, node =>
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.Service) ==
            "Fixtures.Wcf.Hosting.HostedService");
        Assert.Equal("C#", hosted.Properties[WcfVocabulary.Properties.Language]);
        Assert.Equal("HostedService.svc.cs", hosted.Properties[WcfVocabulary.Properties.CodeBehind]);
        Assert.Equal("Fixtures.Wcf.Hosting.InertFactory", hosted.Properties[WcfVocabulary.Properties.Factory]);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.HostsService &&
            edge.FromNodeId == hosted.NodeId &&
            edge.ToNodeId == Node(document, "Fixtures.Wcf.Hosting.HostedService").NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Partial);

        var unresolvedHost = Assert.Single(hostingNodes, node =>
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.Service) ==
            "Fixtures.Wcf.Hosting.MissingService");
        var unresolvedHostEdge = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.HostsService &&
            edge.FromNodeId == unresolvedHost.NodeId);
        Assert.Null(unresolvedHostEdge.ToNodeId);
        Assert.Equal("Fixtures.Wcf.Hosting.MissingService", unresolvedHostEdge.UnresolvedTarget);
        Assert.Equal(ResolutionQuality.Unresolved, unresolvedHostEdge.Resolution.Quality);

        var configuredService = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.ConfiguredService);
        Assert.Equal(2, document.Nodes.Count(node => node.Kind == WcfVocabulary.NodeKinds.Endpoint));
        var serviceEndpoint = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.Endpoint &&
                    node.Properties.GetValueOrDefault(WcfVocabulary.Properties.Direction) == "service");
        var containment = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ConfiguredServiceEndpoint &&
            edge.FromNodeId == configuredService.NodeId);
        Assert.Equal(serviceEndpoint.NodeId, containment.ToNodeId);
        Assert.Equal(ResolutionBasis.DeclarativeConfiguration, containment.Resolution.Basis);
        Assert.Equal(ResolutionQuality.Exact, containment.Resolution.Quality);
        AssertNodeEvidenceSpanContains(
            document,
            fixture.Path,
            serviceEndpoint,
            WcfVocabulary.Rules.ConfigurationEndpointIdentity,
            "dns");
        var binding = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.Binding);
        Assert.Equal("basicHttpBinding", binding.Properties[WcfVocabulary.Properties.ConfigBindingFamily]);
        Assert.Equal("secureBasic", binding.Properties[WcfVocabulary.Properties.ConfigBindingName]);
        Assert.Equal("Transport", binding.Properties[WcfVocabulary.Properties.BindingSecurityMode]);
        Assert.Equal(
            "Certificate",
            binding.Properties[WcfVocabulary.Properties.BindingTransportClientCredentialType]);
        AssertNodeEvidenceSpanContains(
            document,
            fixture.Path,
            binding,
            WcfVocabulary.Rules.ConfigurationBindingSecurity,
            "security");
        AssertNodeEvidenceSpanContains(
            document,
            fixture.Path,
            binding,
            WcfVocabulary.Rules.ConfigurationBindingSecurity,
            "transport");
        AssertNodeEvidenceSpanContains(
            document,
            fixture.Path,
            binding,
            WcfVocabulary.Rules.ConfigurationBindingSecurity,
            "message");
        var behaviors = document.Nodes
            .Where(node => node.Kind == WcfVocabulary.NodeKinds.Behavior)
            .ToArray();
        Assert.Equal(2, behaviors.Length);
        Assert.Contains(behaviors, behavior =>
            NodeEvidenceSpansContain(
                document,
                fixture.Path,
                behavior,
                WcfVocabulary.Rules.ConfigurationBehaviorElement,
                "serviceMetadata"));
        Assert.Contains(behaviors, behavior =>
            NodeEvidenceSpansContain(
                document,
                fixture.Path,
                behavior,
                WcfVocabulary.Rules.ConfigurationBehaviorElement,
                "dataContractSerializer"));
        Assert.Single(document.Nodes, node => node.Kind == WcfVocabulary.NodeKinds.ServiceActivation);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.EndpointContract) >= 2);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.EndpointBinding) >= 2);
        Assert.True(document.Edges.Count(edge => edge.Kind == WcfVocabulary.EdgeKinds.EndpointBehavior) >= 2);
        Assert.Single(document.Edges, edge => edge.Kind == WcfVocabulary.EdgeKinds.ServiceBehavior);
        Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.ServiceActivationImplementation &&
            edge.ToNodeId is not null);
        Assert.All(
            document.Edges.Where(edge =>
                edge.Kind is WcfVocabulary.EdgeKinds.EndpointContract or
                    WcfVocabulary.EdgeKinds.ConfiguredServiceImplementation or
                    WcfVocabulary.EdgeKinds.ServiceActivationImplementation),
            edge => Assert.Equal(ResolutionQuality.Partial, edge.Resolution.Quality));
        AssertValid(document);
    }

    [Fact]
    public async Task Svc_server_comment_does_not_emit_or_resolve_a_hosting_declaration()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "CommentedOut.svc"),
            """
            <%--
            <%@ ServiceHost Language="C#" Service="Fixtures.Wcf.Basic.CustomerService" %>
            --%>
            """);

        var document = await AnalyzeAsync(fixture.Path);

        Assert.DoesNotContain(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.HostingDeclaration);
        Assert.DoesNotContain(
            document.Edges,
            edge => edge.Kind == WcfVocabulary.EdgeKinds.HostsService);
        Assert.DoesNotContain(
            WcfEvidence(document),
            evidence => evidence.RelativePath == "CommentedOut.svc" &&
                        evidence.Provenance.RuleId == WcfVocabulary.Rules.SvcDirective);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedSvc &&
            diagnostic.RelativePath == "CommentedOut.svc");
        AssertValid(document);
    }

    [Fact]
    public async Task Configuration_binding_edges_cover_explicit_and_implicit_default_bindings()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfHostingConfig");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "default-binding.config"),
            """
            <configuration>
              <system.serviceModel>
                <client>
                  <endpoint name="explicitDefaultClient"
                            contract="Fixtures.Wcf.Hosting.IHostedService"
                            binding="basicHttpBinding" />
                </client>
                <bindings>
                  <basicHttpBinding>
                    <binding />
                  </basicHttpBinding>
                </bindings>
              </system.serviceModel>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "implicit-binding.config"),
            """
            <configuration>
              <system.serviceModel>
                <client>
                  <endpoint name="implicitDefaultClient"
                            contract="Fixtures.Wcf.Hosting.IHostedService"
                            binding="basicHttpBinding" />
                </client>
              </system.serviceModel>
            </configuration>
            """);

        var document = await AnalyzeAsync(fixture.Path);
        var explicitEndpoint = Assert.Single(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.Endpoint &&
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.ConfigEndpointName) ==
            "explicitDefaultClient");
        var explicitDefaultBinding = Assert.Single(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.Binding &&
            node.Name == "basicHttpBinding:(default)");
        var resolved = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.EndpointBinding &&
            edge.FromNodeId == explicitEndpoint.NodeId);
        Assert.Equal(explicitDefaultBinding.NodeId, resolved.ToNodeId);
        Assert.Null(resolved.UnresolvedTarget);
        Assert.Equal(ResolutionQuality.Exact, resolved.Resolution.Quality);

        var implicitEndpoint = Assert.Single(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.Endpoint &&
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.ConfigEndpointName) ==
            "implicitDefaultClient");
        var unresolved = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.EndpointBinding &&
            edge.FromNodeId == implicitEndpoint.NodeId);
        Assert.Null(unresolved.ToNodeId);
        Assert.Equal("basicHttpBinding:<default>", unresolved.UnresolvedTarget);
        Assert.Equal(ResolutionQuality.Unresolved, unresolved.Resolution.Quality);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.BindingUnresolved &&
            diagnostic.RelativePath == "implicit-binding.config");
        AssertValid(document);
    }

    [Fact]
    public async Task Supported_behavior_children_retain_spans_and_nested_unsupported_metadata_is_recursive()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "nested-behavior.config"),
            """
            <configuration>
              <system.serviceModel>
                <behaviors>
                  <serviceBehaviors>
                    <behavior name="nestedBehavior">
                      <serviceCredentials unsupportedOnSupported="inert">
                        <userNameAuthentication custom="inert">
                          <deeper value="inert" />
                        </userNameAuthentication>
                      </serviceCredentials>
                    </behavior>
                  </serviceBehaviors>
                </behaviors>
              </system.serviceModel>
            </configuration>
            """);

        var document = await AnalyzeAsync(fixture.Path);
        var behavior = Assert.Single(document.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.Behavior &&
            node.Properties.GetValueOrDefault(WcfVocabulary.Properties.ConfigBehaviorName) ==
            "nestedBehavior");
        AssertNodeEvidenceSpanContains(
            document,
            fixture.Path,
            behavior,
            WcfVocabulary.Rules.ConfigurationBehaviorElement,
            "serviceCredentials");
        foreach (var expected in new[]
                 {
                     "unsupportedOnSupported",
                     "userNameAuthentication",
                     "custom",
                     "deeper",
                     "value",
                 })
        {
            Assert.Contains(document.Diagnostics, diagnostic =>
                diagnostic.Code == WcfVocabulary.Diagnostics.UnsupportedConfiguration &&
                diagnostic.Message.Contains(expected, StringComparison.Ordinal));
        }

        AssertValid(document);
    }

    [Fact]
    public async Task Programmatic_fixture_discovers_direct_hosts_factories_and_client_bases_conservatively()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfProgrammatic");
        var document = await AnalyzeAsync(fixture.Path);

        var host = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.ServiceHostSite);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.HostsService &&
            edge.FromNodeId == host.NodeId &&
            edge.ToNodeId == Node(document, "Fixtures.Wcf.Programmatic.ProgrammaticService").NodeId);

        var factories = document.Nodes
            .Where(node => node.Kind == WcfVocabulary.NodeKinds.ChannelFactorySite)
            .ToArray();
        Assert.Equal(2, factories.Length);
        Assert.Contains(
            factories,
            node => node.Properties.GetValueOrDefault(WcfVocabulary.Properties.EndpointName) ==
                    "programmaticEndpoint");
        Assert.Contains(
            factories,
            node => node.Properties.GetValueOrDefault(WcfVocabulary.Properties.Address) ==
                    "https://example.invalid/programmatic.svc");
        Assert.Equal(
            2,
            document.Edges.Count(edge =>
                edge.Kind == WcfVocabulary.EdgeKinds.ClientContract &&
                factories.Any(factory => factory.NodeId == edge.FromNodeId)));

        var generated = Node(document, "Fixtures.Wcf.Programmatic.GeneratedClient");
        var handWritten = Node(document, "Fixtures.Wcf.Programmatic.HandWrittenReferenceClient");
        Assert.Contains(WcfVocabulary.Attributes.ClientBase, generated.Attributes);
        Assert.Contains(WcfVocabulary.Attributes.ClientBase, handWritten.Attributes);
        Assert.Equal("true", generated.Properties[WcfVocabulary.Properties.Generated]);
        Assert.DoesNotContain(WcfVocabulary.Properties.Generated, handWritten.Properties.Keys);
        Assert.Equal(
            2,
            document.Edges.Count(edge =>
                edge.Kind == WcfVocabulary.EdgeKinds.ClientContract &&
                edge.FromNodeId is not null &&
                (edge.FromNodeId == generated.NodeId || edge.FromNodeId == handWritten.NodeId)));
        AssertValid(document);
    }

    [Fact]
    public async Task Hostile_fixture_degrades_explicitly_and_never_executes_or_fetches_repository_content()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfAmbiguousHostile");
        var document = await AnalyzeAsync(fixture.Path);

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        var fake = Node(
            document,
            "Fixtures.Wcf.AmbiguousHostile.Lookalikes.IFakeContract");
        Assert.DoesNotContain(WcfVocabulary.Attributes.ServiceContract, fake.Attributes);
        Assert.DoesNotContain(
            document.Edges,
            edge => edge.Kind == WcfVocabulary.EdgeKinds.ContractOperation && edge.FromNodeId == fake.NodeId);

        var ambiguousEndpointEdge = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.EndpointContract &&
            edge.UnresolvedTarget == "Fixtures.Wcf.SharedContract");
        Assert.Null(ambiguousEndpointEdge.ToNodeId);
        Assert.Equal(ResolutionQuality.Ambiguous, ambiguousEndpointEdge.Resolution.Quality);
        var unresolvedEndpointEdge = Assert.Single(document.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.EndpointContract &&
            edge.UnresolvedTarget == "Missing.External.IUnresolvedContract");
        Assert.Null(unresolvedEndpointEdge.ToNodeId);
        Assert.Equal(ResolutionQuality.Unresolved, unresolvedEndpointEdge.Resolution.Quality);

        AssertDiagnostics(
            document,
            WcfVocabulary.Diagnostics.UnsupportedFrameworkProfile,
            WcfVocabulary.Diagnostics.AttributeIdentityUnresolved,
            WcfVocabulary.Diagnostics.EndpointContractAmbiguous,
            WcfVocabulary.Diagnostics.EndpointContractUnresolved,
            WcfVocabulary.Diagnostics.BindingUnresolved,
            WcfVocabulary.Diagnostics.BehaviorUnresolved,
            WcfVocabulary.Diagnostics.MalformedConfiguration,
            WcfVocabulary.Diagnostics.DtdProhibited,
            WcfVocabulary.Diagnostics.ExternalConfigurationNotResolved,
            WcfVocabulary.Diagnostics.ExtensionDetected,
            WcfVocabulary.Diagnostics.MalformedSvc,
            WcfVocabulary.Diagnostics.UnsupportedSvc);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.AttributeIdentityUnresolved &&
            diagnostic.RelativePath == "AmbiguousAttributeUse.cs");
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.AttributeIdentityUnresolved &&
            diagnostic.RelativePath == "MissingTypes.cs");

        var extension = Assert.Single(
            document.Nodes,
            node => node.Kind == WcfVocabulary.NodeKinds.ExtensionDeclaration);
        Assert.Contains(
            "MarkerBehaviorExtension",
            extension.Properties[WcfVocabulary.Properties.ExtensionType],
            StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(fixture.Path, "*.marker", SearchOption.AllDirectories));
        Assert.False(File.Exists(fixture.ExtensionMarkerPath));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "bin")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Path, "obj")));
        Assert.True(File.Exists(System.IO.Path.Combine(fixture.Path, "remote.wsdl")));
        AssertValid(document);
    }

    [Theory]
    [InlineData("WcfBasic")]
    [InlineData("WcfHostingConfig")]
    [InlineData("WcfAmbiguousHostile")]
    public async Task Repeat_analysis_is_canonical_deterministic_and_contains_no_machine_paths(
        string fixtureName)
    {
        using var fixture = TemporaryWcfFixture.Copy(fixtureName);
        var baseline = await ScanAsync(fixture.Path);
        var analyzer = new ClassicWcfAnalyzer();

        var first = await analyzer.AnalyzeAsync(fixture.Path, baseline);
        var second = await analyzer.AnalyzeAsync(fixture.Path, baseline);
        var firstJson = AnalysisJson.Serialize(first, indented: false);
        var secondJson = AnalysisJson.Serialize(second, indented: false);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
        Assert.Equal(first.Nodes.Select(node => node.NodeId), second.Nodes.Select(node => node.NodeId));
        Assert.Equal(first.Edges.Select(edge => edge.EdgeId), second.Edges.Select(edge => edge.EdgeId));
        Assert.Equal(first.Evidence.Select(evidence => evidence.EvidenceId), second.Evidence.Select(evidence => evidence.EvidenceId));
        Assert.DoesNotContain(fixture.Path, firstJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\Users\\", firstJson, StringComparison.OrdinalIgnoreCase);
        AssertValid(first);
        AssertValid(second);
    }

    [Fact]
    public async Task Representative_wcf_evidence_has_recomputable_provenance_and_claim_spans()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfHostingConfig");
        var document = await AnalyzeAsync(fixture.Path);
        var manifest = document.Snapshot.Manifest.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var evidence = WcfEvidence(document).ToArray();

        Assert.NotEmpty(evidence);
        foreach (var item in evidence)
        {
            Assert.False(System.IO.Path.IsPathRooted(item.RelativePath));
            var entry = Assert.Contains(item.RelativePath, manifest);
            Assert.Equal(entry.ContentHash, item.ContentHash);
            Assert.Equal(WcfVocabulary.ExtractorId, item.Provenance.ExtractorId);
            Assert.Equal(WcfVocabulary.ExtractorVersion, item.Provenance.ExtractorVersion);
            Assert.False(string.IsNullOrWhiteSpace(item.Provenance.RuleId));
            Assert.Equal(RecomputeEvidenceId(item), item.EvidenceId);

            var text = await File.ReadAllTextAsync(System.IO.Path.Combine(fixture.Path, item.RelativePath));
            Assert.InRange(item.Span.StartOffset, 0, text.Length - 1);
            Assert.InRange(item.Span.Length, 1, text.Length - item.Span.StartOffset);
        }

        foreach (var node in document.Nodes.Where(node =>
                     node.Kind.StartsWith("Wcf", StringComparison.Ordinal) ||
                     node.Attributes.Any(attribute => attribute.StartsWith("wcf.", StringComparison.Ordinal))))
        {
            var projectQualifiedName = node.ProjectId is null
                ? null
                : NodeById(document, node.ProjectId).QualifiedName;
            Assert.Equal(
                CanonicalIdentity.CreateLogicalNodeId(projectQualifiedName, node.Kind, node.QualifiedName),
                node.LogicalId);
            Assert.Equal(
                CanonicalIdentity.CreateNodeId(document.Snapshot.SnapshotId, node.LogicalId),
                node.NodeId);
        }

        foreach (var edge in document.Edges.Where(edge =>
                     edge.Kind.StartsWith("Wcf", StringComparison.Ordinal)))
        {
            Assert.Equal(
                CanonicalIdentity.CreateEdgeId(
                    document.Snapshot.SnapshotId,
                    edge.Kind,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.UnresolvedTarget,
                    edge.Resolution.Basis,
                    edge.Resolution.Quality,
                    edge.EvidenceIds),
                edge.EdgeId);
        }

        AssertSpanContains(document, fixture.Path, WcfVocabulary.Rules.ServiceContract, "ServiceContract");
        AssertSpanContains(document, fixture.Path, WcfVocabulary.Rules.SvcDirective, "ServiceHost");
        AssertSpanContains(document, fixture.Path, WcfVocabulary.Rules.ConfigurationEndpoint, "endpoint");
        AssertValid(document);
    }

    [Fact]
    public async Task Wcf_analysis_does_not_create_business_or_ddd_semantic_classifications()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var document = await AnalyzeAsync(fixture.Path);
        var prohibited = new[]
        {
            "Aggregate",
            "AggregateRoot",
            "BoundedContext",
            "BusinessCapability",
            "Command",
            "DomainEvent",
            "DomainService",
            "Entity",
            "Subdomain",
            "UseCase",
            "ValueObject",
        };

        var analyzerVocabulary = document.Nodes
            .Select(node => node.Kind)
            .Concat(document.Edges.Select(edge => edge.Kind))
            .Concat(WcfEvidence(document).Select(evidence => evidence.Provenance.RuleId))
            .Concat(document.Nodes.SelectMany(node => node.Attributes))
            .Concat(document.Nodes.SelectMany(node => node.Properties.Keys))
            .ToArray();
        foreach (var term in prohibited)
        {
            Assert.DoesNotContain(
                analyzerVocabulary,
                value => string.Equals(
                    value.Replace("-", string.Empty, StringComparison.Ordinal)
                        .Replace(".", string.Empty, StringComparison.Ordinal),
                    term,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await ScanAsync(repositoryPath);
        var document = await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
        Assert.True(document.Nodes.Count >= baseline.Nodes.Count);
        Assert.True(document.Edges.Count >= baseline.Edges.Count);
        Assert.True(document.Evidence.Count >= baseline.Evidence.Count);
        Assert.All(
            baseline.Nodes,
            node => Assert.Contains(document.Nodes, candidate => candidate.NodeId == node.NodeId));
        Assert.All(
            baseline.Evidence,
            evidence => Assert.Contains(
                document.Evidence,
                candidate => candidate.EvidenceId == evidence.EvidenceId));
        return document;
    }

    private static async Task<AnalysisDocument> ScanAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        AssertValid(baseline);
        return baseline;
    }

    private static EvidenceNode Node(AnalysisDocument document, string qualifiedName) =>
        Assert.Single(
            document.Nodes,
            node => string.Equals(node.QualifiedName, qualifiedName, StringComparison.Ordinal));

    private static EvidenceNode MemberNode(AnalysisDocument document, string qualifiedNamePrefix) =>
        Assert.Single(
            document.Nodes,
            node => node.QualifiedName.StartsWith(qualifiedNamePrefix + ":", StringComparison.Ordinal));

    private static EvidenceNode NodeById(AnalysisDocument document, string nodeId) =>
        Assert.Single(document.Nodes, node => node.NodeId == nodeId);

    private static void AssertResolvedEdge(
        AnalysisDocument document,
        string kind,
        EvidenceNode from,
        EvidenceNode to) =>
        Assert.Contains(document.Edges, edge =>
            edge.Kind == kind &&
            edge.FromNodeId == from.NodeId &&
            edge.ToNodeId == to.NodeId &&
            edge.UnresolvedTarget is null);

    private static IEnumerable<EvidenceRecord> WcfEvidence(AnalysisDocument document) =>
        document.Evidence.Where(evidence =>
            evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId);

    private static void AssertDiagnostics(AnalysisDocument document, params string[] codes)
    {
        foreach (var code in codes)
        {
            Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == code);
        }
    }

    private static string RecomputeEvidenceId(EvidenceRecord evidence) =>
        CanonicalIdentity.CreateEvidenceId(
            evidence.SnapshotId,
            evidence.RelativePath,
            evidence.ContentHash,
            evidence.Span.StartOffset,
            evidence.Span.Length,
            evidence.Provenance.ExtractorId,
            evidence.Provenance.ExtractorVersion,
            evidence.Provenance.RuleId,
            evidence.Resolution.Basis,
            evidence.Resolution.Quality);

    private static void AssertSpanContains(
        AnalysisDocument document,
        string repositoryPath,
        string ruleId,
        string expected)
    {
        var evidence = WcfEvidence(document).First(item => item.Provenance.RuleId == ruleId);
        var text = File.ReadAllText(System.IO.Path.Combine(repositoryPath, evidence.RelativePath));
        var claimed = text.Substring(evidence.Span.StartOffset, evidence.Span.Length);
        Assert.Contains(expected, claimed, StringComparison.Ordinal);
    }

    private static void AssertNodeEvidenceSpanContains(
        AnalysisDocument document,
        string repositoryPath,
        EvidenceNode node,
        string ruleId,
        string expected) =>
        Assert.True(
            NodeEvidenceSpansContain(document, repositoryPath, node, ruleId, expected),
            $"Node '{node.QualifiedName}' has no '{ruleId}' evidence span containing '{expected}'.");

    private static bool NodeEvidenceSpansContain(
        AnalysisDocument document,
        string repositoryPath,
        EvidenceNode node,
        string ruleId,
        string expected)
    {
        var evidenceById = document.Evidence.ToDictionary(item => item.EvidenceId, StringComparer.Ordinal);
        foreach (var evidenceId in node.EvidenceIds)
        {
            if (!evidenceById.TryGetValue(evidenceId, out var evidence) ||
                evidence.Provenance.RuleId != ruleId)
            {
                continue;
            }

            var text = File.ReadAllText(System.IO.Path.Combine(repositoryPath, evidence.RelativePath));
            var claimed = text.Substring(evidence.Span.StartOffset, evidence.Span.Length);
            if (claimed.Contains(expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
