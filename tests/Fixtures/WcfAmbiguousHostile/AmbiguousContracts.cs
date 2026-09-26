using System.ServiceModel;

namespace Fixtures.Wcf.AmbiguousHostile
{
    [ServiceContract(ConfigurationName = "Fixtures.Wcf.SharedContract")]
    public interface IFirstSharedContract
    {
        [OperationContract]
        string First();
    }

    [ServiceContract(ConfigurationName = "Fixtures.Wcf.SharedContract")]
    public interface ISecondSharedContract
    {
        [OperationContract]
        string Second();
    }
}
