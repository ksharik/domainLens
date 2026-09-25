using System.ServiceModel;

namespace Fixtures.Wcf.Hosting
{
    [ServiceContract(
        Name = "HostedContract",
        Namespace = "urn:domainlens:fixtures:hosting",
        ConfigurationName = "Fixtures.Wcf.Hosting.IHostedService")]
    public interface IHostedService
    {
        [OperationContract]
        string GetStatus(int id);
    }

    public sealed class HostedService : IHostedService
    {
        public string GetStatus(int id)
        {
            return id.ToString();
        }
    }

    public sealed class InertFactory
    {
    }
}
