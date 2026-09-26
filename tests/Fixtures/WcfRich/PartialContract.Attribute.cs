using System.ServiceModel;

namespace Fixtures.Wcf.Rich
{
    [ServiceContract]
    public partial interface IPartialContract
    {
        [OperationContract]
        void Execute();
    }
}
