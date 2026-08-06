namespace EMF.Discovery.Contracts;

using EMF.Discovery.Models;

public interface IDiscoveryService
{
    Task<DiscoveryStatistics> DiscoverAsync(
        string sourcePath,
        DiscoveryOptions options,
        CancellationToken cancellationToken = default);
}
