namespace Fixtures.Wcf.AmbiguousHostile
{
    [Missing.Framework.ServiceContract]
    public interface IUnavailableAttributeContract
    {
        Missing.External.Response Invoke(Missing.External.Request request);
    }

    [System.ServiceModel.ServiceContract]
    public interface IExternalFaultContract
    {
        [System.ServiceModel.OperationContract]
        [System.ServiceModel.FaultContract(typeof(Missing.External.FaultDetail))]
        void Invoke();
    }
}
