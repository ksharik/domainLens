using System;

namespace Fixtures.Wcf.AmbiguousHostile.Lookalikes
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
    public sealed class ServiceContractAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OperationContractAttribute : Attribute
    {
    }

    [ServiceContract]
    public interface IFakeContract
    {
        [OperationContract]
        void NotWcf();
    }
}

namespace Fixtures.Wcf.AmbiguousHostile.FakeOne
{
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class ServiceContractAttribute : Attribute
    {
    }
}

namespace Fixtures.Wcf.AmbiguousHostile.FakeTwo
{
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class ServiceContractAttribute : Attribute
    {
    }
}
