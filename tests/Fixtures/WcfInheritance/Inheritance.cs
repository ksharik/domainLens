using System.ServiceModel;

namespace Fixtures.Wcf.Inheritance
{
    [ServiceContract(ConfigurationName = "Fixtures.Wcf.Inheritance.IBaseContract")]
    public interface IBaseContract
    {
        [OperationContract]
        string Ping(string value);
    }

    [ServiceContract(ConfigurationName = "Fixtures.Wcf.Inheritance.IDerivedContract")]
    public interface IDerivedContract : IBaseContract
    {
        [OperationContract]
        int Add(int left, int right);
    }

    public abstract class BaseImplementation
    {
        public string Ping(string value)
        {
            return value;
        }
    }

    public sealed class InheritedService : BaseImplementation, IDerivedContract
    {
        public int Add(int left, int right)
        {
            return left + right;
        }
    }

    public sealed class ExplicitService : IDerivedContract
    {
        string IBaseContract.Ping(string value)
        {
            return value;
        }

        int IDerivedContract.Add(int left, int right)
        {
            return left + right;
        }
    }

    public sealed class AlternativeService : IDerivedContract
    {
        public string Ping(string value)
        {
            return value;
        }

        public int Add(int left, int right)
        {
            return left + right;
        }
    }
}
