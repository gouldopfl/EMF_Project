namespace EMF.Common;

public sealed class GuidIdGenerator : IIdGenerator
{
    public string Generate() =>
        Guid.NewGuid().ToString("N");
}
