namespace Partial.Sample;

public sealed class PartiallyParsedType
{
    public string StillDiscoverable { get; } = "yes";

    public string MissingSemicolon()
    {
        return StillDiscoverable
    }
}
