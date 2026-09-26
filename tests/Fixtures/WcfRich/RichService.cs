namespace Fixtures.Wcf.Rich
{
    public sealed class RichService : IRichService, IFullyQualifiedContract
    {
        public SubmitResponse Submit(SubmitRequest request)
        {
            return new SubmitResponse { Accepted = request != null };
        }

        public SubmitResponse Submit(string value)
        {
            return new SubmitResponse { Accepted = !string.IsNullOrEmpty(value) };
        }

        public string Echo(string value)
        {
            return value;
        }
    }
}
