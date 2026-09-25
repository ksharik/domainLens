using System.ServiceModel;
using SC = System.ServiceModel.ServiceContractAttribute;

namespace Fixtures.Wcf.Rich
{
    public interface IProgressCallback
    {
        [System.ServiceModel.OperationContractAttribute(IsOneWay = true)]
        void Report(ProgressMessage message);
    }

    [SC(
        Name = "RichContract",
        Namespace = "urn:domainlens:fixtures:rich",
        ConfigurationName = "Fixtures.Wcf.Rich.IRichService",
        CallbackContract = typeof(IProgressCallback))]
    public interface IRichService
    {
        [OperationContractAttribute(Name = "SubmitMessage")]
        SubmitResponse Submit(SubmitRequest request);

        [OperationContract(Name = "SubmitText", IsInitiating = true, IsTerminating = false)]
        SubmitResponse Submit(string value);
    }

    [System.ServiceModel.ServiceContractAttribute(
        ConfigurationName = "Fixtures.Wcf.Rich.IFullyQualifiedContract")]
    public interface IFullyQualifiedContract
    {
        [System.ServiceModel.OperationContract]
        string Echo(string value);
    }

    [MessageContract(
        IsWrapped = true,
        WrapperName = "Submit",
        WrapperNamespace = "urn:domainlens:fixtures:messages")]
    public sealed class SubmitRequest
    {
        [MessageHeader(
            Name = "correlationId",
            Namespace = "urn:domainlens:fixtures:headers",
            Actor = "urn:domainlens:actors:client",
            MustUnderstand = true,
            Relay = true)]
        public string CorrelationId;

        [MessageBodyMember(Name = "value", Namespace = "urn:domainlens:fixtures:bodies", Order = 1)]
        public string Value;
    }

    [MessageContract(
        IsWrapped = false,
        WrapperName = "IgnoredWhenUnwrapped",
        WrapperNamespace = "urn:domainlens:fixtures:messages")]
    public sealed class SubmitResponse
    {
        [MessageHeader(Name = "server", Namespace = "urn:domainlens:fixtures:headers")]
        public string Server;

        [MessageBodyMember(Name = "accepted", Namespace = "urn:domainlens:fixtures:bodies", Order = 1)]
        public bool Accepted;
    }

    [MessageContract(IsWrapped = false)]
    public sealed class ProgressMessage
    {
        [MessageBodyMember(Order = 1)]
        public int Percent;
    }
}
