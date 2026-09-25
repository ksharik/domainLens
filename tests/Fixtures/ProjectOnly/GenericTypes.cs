namespace Standalone.Inventory;

public sealed class Box<T>
{
}

public sealed class Box<TFirst, TSecond>
{
}

public sealed class GenericConsumer
{
    public Box<int> One { get; } = new();

    public Box<int, string> Two { get; } = new();
}

public sealed class Initialization
{
    static Initialization()
    {
    }

    public Initialization()
    {
    }
}
