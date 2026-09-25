namespace Partial.Sample;

public interface IRecoverableContract
{
    MissingType Transform(MissingType input);
}
public sealed class RecoverableService : IRecoverableContract
{
    public MissingType Transform(MissingType input)
    {
        return input;
    }
}
