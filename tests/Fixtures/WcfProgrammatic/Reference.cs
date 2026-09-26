using System.ServiceModel;

namespace Fixtures.Wcf.Programmatic
{
    // The filename and class name intentionally resemble generated proxy source.
    // Neither is trusted evidence of compiler generation.
    public sealed class HandWrittenReferenceClient : ClientBase<IProgrammaticService>, IProgrammaticService
    {
        public string GetStatus(int id)
        {
            return Channel.GetStatus(id);
        }
    }
}
