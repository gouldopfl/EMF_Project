namespace EMF.Discovery.Models;

public sealed class DiscoveryOptions
{
    public bool Recursive { get; init; } = true;

    public bool FollowSymbolicLinks { get; init; }

    public bool IncludeHiddenFiles { get; init; }
}