namespace DomainLens.Analyzer.Wcf;

/// <summary>
/// Stable analyzer-owned identifiers used by the deterministic classic-WCF extractor.
/// These identifiers describe implementation evidence only; none are DDD concepts.
/// </summary>
public static class WcfVocabulary
{
    public const string ExtractorId = "domainlens.classic-wcf";
    public const string ExtractorVersion = "0.1.0";

    public static class NodeKinds
    {
        public const string FaultDeclaration = "WcfFaultDeclaration";
        public const string ConfiguredService = "WcfConfiguredService";
        public const string Endpoint = "WcfEndpoint";
        public const string Binding = "WcfBinding";
        public const string Behavior = "WcfBehavior";
        public const string HostingDeclaration = "WcfHostingDeclaration";
        public const string ServiceActivation = "WcfServiceActivation";
        public const string ExtensionDeclaration = "WcfExtensionDeclaration";
        public const string ServiceHostSite = "WcfServiceHostSite";
        public const string ChannelFactorySite = "WcfChannelFactorySite";
    }

    public static class EdgeKinds
    {
        public const string ContractOperation = "WcfContractOperation";
        public const string CallbackContract = "WcfCallbackContract";
        public const string DeclaresFault = "WcfDeclaresFault";
        public const string FaultDetailType = "WcfFaultDetailType";
        public const string UsesDataContract = "WcfUsesDataContract";
        public const string UsesMessageContract = "WcfUsesMessageContract";
        public const string DataMember = "WcfDataMember";
        public const string MessageHeader = "WcfMessageHeader";
        public const string MessageBodyMember = "WcfMessageBodyMember";
        public const string ImplementsContract = "WcfImplementsContract";
        public const string ImplementsOperation = "WcfImplementsOperation";
        public const string HostsService = "WcfHostsService";
        public const string ConfiguredServiceImplementation = "WcfConfiguredServiceImplementation";
        public const string ConfiguredServiceEndpoint = "WcfConfiguredServiceEndpoint";
        public const string EndpointContract = "WcfEndpointContract";
        public const string EndpointBinding = "WcfEndpointBinding";
        public const string EndpointBehavior = "WcfEndpointBehavior";
        public const string ServiceBehavior = "WcfServiceBehavior";
        public const string ServiceActivationImplementation = "WcfServiceActivationImplementation";
        public const string ClientContract = "WcfClientContract";
        public const string ChannelFactoryContract = "WcfChannelFactoryContract";
        public const string ClientBaseContract = "WcfClientBaseContract";
    }

    public static class Attributes
    {
        public const string ServiceContract = "wcf.service-contract";
        public const string OperationContract = "wcf.operation-contract";
        public const string DataContract = "wcf.data-contract";
        public const string DataMember = "wcf.data-member";
        public const string MessageContract = "wcf.message-contract";
        public const string MessageHeader = "wcf.message-header";
        public const string MessageBodyMember = "wcf.message-body-member";
        public const string ClientBase = "wcf.client-base";
    }

    public static class Properties
    {
        public const string Prefix = "wcf.";
        public const string ServiceContractName = Prefix + "serviceContract.name";
        public const string ServiceContractNamespace = Prefix + "serviceContract.namespace";
        public const string ServiceContractConfigurationName = Prefix + "serviceContract.configurationName";
        public const string ContractClrIdentity = Prefix + "serviceContract.clrIdentity";
        public const string OperationName = Prefix + "operationContract.name";
        public const string OperationAction = Prefix + "operationContract.action";
        public const string OperationReplyAction = Prefix + "operationContract.replyAction";
        public const string OperationIsOneWay = Prefix + "operationContract.isOneWay";
        public const string OperationAsyncPattern = Prefix + "operationContract.asyncPattern";
        public const string OperationIsInitiating = Prefix + "operationContract.isInitiating";
        public const string OperationIsTerminating = Prefix + "operationContract.isTerminating";
        public const string ParameterTypes = Prefix + "operationContract.parameterTypes";
        public const string ReturnType = Prefix + "operationContract.returnType";
        public const string FaultName = Prefix + "faultContract.name";
        public const string FaultNamespace = Prefix + "faultContract.namespace";
        public const string FaultAction = Prefix + "faultContract.action";
        public const string DetailType = Prefix + "faultContract.detailType";
        public const string DataContractName = Prefix + "dataContract.name";
        public const string DataContractNamespace = Prefix + "dataContract.namespace";
        public const string DataContractIsReference = Prefix + "dataContract.isReference";
        public const string DataMemberName = Prefix + "dataMember.name";
        public const string DataMemberOrder = Prefix + "dataMember.order";
        public const string DataMemberIsRequired = Prefix + "dataMember.isRequired";
        public const string DataMemberEmitDefaultValue = Prefix + "dataMember.emitDefaultValue";
        public const string MessageIsWrapped = Prefix + "messageContract.isWrapped";
        public const string MessageWrapperName = Prefix + "messageContract.wrapperName";
        public const string MessageWrapperNamespace = Prefix + "messageContract.wrapperNamespace";
        public const string MessageHeaderName = Prefix + "messageHeader.name";
        public const string MessageHeaderNamespace = Prefix + "messageHeader.namespace";
        public const string MessageBodyMemberName = Prefix + "messageBodyMember.name";
        public const string MessageBodyMemberNamespace = Prefix + "messageBodyMember.namespace";
        public const string MessageBodyMemberOrder = Prefix + "messageBodyMember.order";
        public const string MessageHeaderActor = Prefix + "messageHeader.actor";
        public const string MessageHeaderMustUnderstand = Prefix + "messageHeader.mustUnderstand";
        public const string MessageHeaderRelay = Prefix + "messageHeader.relay";
        public const string Inherited = Prefix + "relationship.inherited";
        public const string Direction = Prefix + "endpoint.direction";
        public const string Address = Prefix + "endpoint.address";
        public const string Contract = Prefix + "endpoint.contract";
        public const string Binding = Prefix + "endpoint.binding";
        public const string BindingConfiguration = Prefix + "endpoint.bindingConfiguration";
        public const string BehaviorConfiguration = Prefix + "endpoint.behaviorConfiguration";
        public const string Service = Prefix + "hosting.service";
        public const string Factory = Prefix + "hosting.factory";
        public const string CodeBehind = Prefix + "hosting.codeBehind";
        public const string Language = Prefix + "hosting.language";
        public const string EndpointName = Prefix + "client.endpointName";
        public const string ExtensionType = Prefix + "config.extensionType";
        public const string Generated = Prefix + "client.compilerGenerated";
        public const string ConfigEndpointName = Prefix + "endpoint.name";
        public const string ConfigServiceName = Prefix + "hosting.configuredServiceName";
        public const string ConfigBindingFamily = Prefix + "binding.family";
        public const string ConfigBindingName = Prefix + "binding.name";
        public const string ConfigBehaviorKind = Prefix + "behavior.kind";
        public const string ConfigBehaviorName = Prefix + "behavior.name";
        public const string ConfigRelativeAddress = Prefix + "hosting.relativeAddress";
        public const string ConfigExtensionName = Prefix + "config.extensionName";
        public const string BindingMessageEncoding = Prefix + "binding.messageEncoding";
        public const string BindingTextEncoding = Prefix + "binding.textEncoding";
        public const string BindingTransferMode = Prefix + "binding.transferMode";
        public const string BindingSecurityMode = Prefix + "binding.securityMode";
        public const string BindingTransportClientCredentialType = Prefix + "binding.transportClientCredentialType";
        public const string BindingMessageClientCredentialType = Prefix + "binding.messageClientCredentialType";
        public const string BehaviorElements = Prefix + "behavior.elements";
        public const string EndpointIdentityKind = Prefix + "endpoint.identityKind";
    }

    public static class Rules
    {
        public const string ServiceContract = "wcf.source.service-contract";
        public const string OperationContract = "wcf.source.operation-contract";
        public const string FaultContract = "wcf.source.fault-contract";
        public const string DataContract = "wcf.source.data-contract";
        public const string DataMember = "wcf.source.data-member";
        public const string MessageContract = "wcf.source.message-contract";
        public const string MessageHeader = "wcf.source.message-header";
        public const string MessageBodyMember = "wcf.source.message-body-member";
        public const string ContractOperation = "wcf.source.contract-operation";
        public const string CallbackContract = "wcf.source.callback-contract";
        public const string OperationParameterContract = "wcf.source.operation-parameter-contract";
        public const string OperationReturnContract = "wcf.source.operation-return-contract";
        public const string ImplementsContract = "wcf.source.implements-contract";
        public const string ImplementsOperation = "wcf.source.implements-operation";
        public const string ServiceHost = "wcf.source.service-host";
        public const string ChannelFactory = "wcf.source.channel-factory";
        public const string ClientBase = "wcf.source.client-base";
        public const string SvcDirective = "wcf.svc.service-host-directive";
        public const string SvcServiceLink = "wcf.svc.service-link";
        public const string ConfigurationService = "wcf.config.service";
        public const string ConfigurationEndpoint = "wcf.config.endpoint";
        public const string ConfigurationServiceEndpoint = "wcf.config.service-endpoint";
        public const string ConfigurationEndpointIdentity = "wcf.config.endpoint-identity";
        public const string ConfigurationBinding = "wcf.config.binding";
        public const string ConfigurationBindingSecurity = "wcf.config.binding-security";
        public const string ConfigurationBehavior = "wcf.config.behavior";
        public const string ConfigurationBehaviorElement = "wcf.config.behavior-element";
        public const string ConfigurationTraversalLimit = "wcf.config.traversal-limit";
        public const string ConfigurationTransform = "wcf.config.transform";
        public const string ConfigurationExternalReference = "wcf.config.external-reference";
        public const string ConfigurationUnsupportedAttribute = "wcf.config.unsupported-attribute";
        public const string ConfigurationUnsupportedElement = "wcf.config.unsupported-element";
        public const string ConfigurationActivation = "wcf.config.service-activation";
        public const string ConfigurationExtension = "wcf.config.extension";
        public const string ConfigurationServiceLink = "wcf.config.service-link";
        public const string ConfigurationEndpointContract = "wcf.config.endpoint-contract";
        public const string ConfigurationEndpointBinding = "wcf.config.endpoint-binding";
        public const string ConfigurationEndpointBehavior = "wcf.config.endpoint-behavior";
        public const string ConfigurationServiceBehavior = "wcf.config.service-behavior";
        public const string ConfigurationActivationLink = "wcf.config.activation-link";
    }

    public static class Diagnostics
    {
        public const string UnsupportedFrameworkProfile = "DL4001";
        public const string AttributeIdentityUnresolved = "DL4002";
        public const string UnsupportedAttributeValue = "DL4003";
        public const string CallbackContractUnresolved = "DL4004";
        public const string GeneratedTypeUnavailable = "DL4005";
        public const string ImplementationUnresolved = "DL4101";
        public const string ImplementationAmbiguous = "DL4102";
        public const string EndpointContractUnresolved = "DL4103";
        public const string EndpointContractAmbiguous = "DL4104";
        public const string BindingUnresolved = "DL4105";
        public const string BehaviorUnresolved = "DL4106";
        public const string MalformedSvc = "DL4201";
        public const string UnsupportedSvc = "DL4202";
        public const string SvcServiceUnresolved = "DL4203";
        public const string MalformedConfiguration = "DL4301";
        public const string DtdProhibited = "DL4302";
        public const string UnsupportedConfiguration = "DL4303";
        public const string ExternalConfigurationNotResolved = "DL4304";
        public const string ExtensionDetected = "DL4305";
        public const string ConfigurationTraversalLimitExceeded = "DL4306";
        public const string ConfigurationTransformNotApplied = "DL4307";
        public const string UnsupportedSourcePattern = "DL4401";
        public const string ManifestReadFailed = "DL4501";
    }

    public static class FrameworkTypes
    {
        public const string ServiceContractAttribute = "System.ServiceModel.ServiceContractAttribute";
        public const string OperationContractAttribute = "System.ServiceModel.OperationContractAttribute";
        public const string FaultContractAttribute = "System.ServiceModel.FaultContractAttribute";
        public const string DataContractAttribute = "System.Runtime.Serialization.DataContractAttribute";
        public const string DataMemberAttribute = "System.Runtime.Serialization.DataMemberAttribute";
        public const string MessageContractAttribute = "System.ServiceModel.MessageContractAttribute";
        public const string MessageHeaderAttribute = "System.ServiceModel.MessageHeaderAttribute";
        public const string MessageBodyMemberAttribute = "System.ServiceModel.MessageBodyMemberAttribute";
        public const string ServiceHost = "System.ServiceModel.ServiceHost";
        public const string ChannelFactory = "System.ServiceModel.ChannelFactory`1";
        public const string ClientBase = "System.ServiceModel.ClientBase`1";
        public const string EndpointAddress = "System.ServiceModel.EndpointAddress";
        public const string CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
        public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
    }
}
