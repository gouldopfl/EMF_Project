using EMF.Discovery.Models;

namespace EMF.Discovery.Contracts;

public interface IStreamingDiscoveryService
{
    IAsyncEnumerable<DiscoveredItem> DiscoverItemsAsync(
        string sourcePath,
        DiscoveryOptions options,
        CancellationToken cancellationToken = default);
}
