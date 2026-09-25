using System.Runtime.Serialization;
using System.ServiceModel;

namespace Fixtures.Wcf.Basic
{
    [ServiceContract(
        Name = "CustomerContract",
        Namespace = "urn:domainlens:fixtures:basic",
        ConfigurationName = "Fixtures.Wcf.Basic.ICustomerService")]
    public interface ICustomerService
    {
        [OperationContract(
            Name = "UpdateCustomer",
            Action = "urn:domainlens:fixtures:basic/update",
            ReplyAction = "urn:domainlens:fixtures:basic/update-response")]
        [FaultContract(
            typeof(CustomerFault),
            Name = "CustomerFault",
            Namespace = "urn:domainlens:fixtures:faults",
            Action = "urn:domainlens:fixtures:basic/customer-fault")]
        CustomerResponse Update(CustomerRequest request);
    }

    [DataContract(Name = "CustomerRequest", Namespace = "urn:domainlens:fixtures:data", IsReference = true)]
    public sealed class CustomerRequest
    {
        [DataMember(Name = "customerId", Order = 1, IsRequired = true, EmitDefaultValue = false)]
        public int CustomerId { get; set; }

        [DataMember(Name = "displayName", Order = 2)]
        public string DisplayName { get; set; }
    }

    [DataContract(Name = "CustomerResponse", Namespace = "urn:domainlens:fixtures:data")]
    public sealed class CustomerResponse
    {
        [DataMember(Order = 1, IsRequired = true)]
        public bool Updated { get; set; }
    }

    [DataContract(Name = "CustomerFault", Namespace = "urn:domainlens:fixtures:faults")]
    public sealed class CustomerFault
    {
        [DataMember(Order = 1, IsRequired = true)]
        public string Code { get; set; }
    }
}
