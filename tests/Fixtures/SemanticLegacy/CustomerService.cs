using System.Runtime.Serialization;
using System.ServiceModel;
using Missing.External;

namespace Legacy.Semantic
{
    [ServiceContract]
    public interface ICustomerService
    {
        [OperationContract]
        CustomerDto GetCustomer(int id);
    }

    public interface IRepository
    {
        CustomerDto Get(int id);

        void Save(CustomerDto customer);
    }

    public abstract class ServiceBase
    {
        protected void Track(string operation)
        {
        }
    }

    public sealed class CustomerService : ServiceBase, ICustomerService, ILegacyDependency
    {
        private readonly IRepository _repository;

        public CustomerService(IRepository repository)
        {
            _repository = repository;
        }

        public CustomerDto GetCustomer(int id)
        {
            Track(nameof(GetCustomer));
            var customer = _repository.Get(id);
            _repository.Save(customer);
            return customer;
        }
    }

    // The first position on a class can be either a base class or an interface. When the type is
    // missing, semantic analysis must not guess which relationship was intended.
    public sealed class AmbiguousMissingFirst : MissingFirstPosition
    {
    }

    // A struct base list can contain only interfaces, so this unresolved relationship is safe to
    // retain as an unresolved implemented-interface observation.
    public struct MissingStructContract : IMissingStructContract
    {
    }

    [DataContract]
    public sealed class CustomerDto
    {
        [DataMember]
        public string Name { get; set; }
    }
}
