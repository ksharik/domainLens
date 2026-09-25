using System.Runtime.CompilerServices;
using System.ServiceModel;

namespace Fixtures.Wcf.Programmatic
{
    [ServiceContract(ConfigurationName = "Fixtures.Wcf.Programmatic.IProgrammaticService")]
    public interface IProgrammaticService
    {
        [OperationContract]
        string GetStatus(int id);
    }

    public sealed class ProgrammaticService : IProgrammaticService
    {
        public string GetStatus(int id)
        {
            return id.ToString();
        }
    }

    public sealed class DirectHost
    {
        public ServiceHost Create()
        {
            return new ServiceHost(typeof(ProgrammaticService));
        }
    }

    public sealed class DirectChannelFactory
    {
        public ChannelFactory<IProgrammaticService> FromConfiguration()
        {
            return new ChannelFactory<IProgrammaticService>("programmaticEndpoint");
        }

        public ChannelFactory<IProgrammaticService> FromAddress()
        {
            return new ChannelFactory<IProgrammaticService>(
                new BasicHttpBinding(),
                new EndpointAddress("https://example.invalid/programmatic.svc"));
        }
    }

    [CompilerGenerated]
    public sealed class GeneratedClient : ClientBase<IProgrammaticService>, IProgrammaticService
    {
        public string GetStatus(int id)
        {
            return Channel.GetStatus(id);
        }
    }
}
