using System.Runtime.Serialization;
using System.ServiceModel;

namespace Fixtures.Wcf.AmbiguousHostile
{
    public sealed class NotADataContract
    {
        [DataMember]
        public string Value { get; set; }
    }

    public sealed class NotAMessageContract
    {
        [MessageHeader]
        public string Header { get; set; }

        [MessageBodyMember]
        public string Body { get; set; }
    }

    public sealed class NotAServiceContract
    {
        [OperationContract]
        [FaultContract(typeof(string))]
        public void Execute()
        {
        }
    }

    [ServiceContract]
    public enum InvalidServiceContractPlacement
    {
        Value
    }
}
