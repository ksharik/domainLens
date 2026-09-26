using System.ServiceModel;

namespace Fixtures.Wcf.Shared
{
    [ServiceContract]
    public interface ISharedContract
    {
        [OperationContract]
        string Ping();
    }

    public sealed class SharedService : ISharedContract
    {
        public string Ping()
        {
            return "pong";
        }
    }
}
