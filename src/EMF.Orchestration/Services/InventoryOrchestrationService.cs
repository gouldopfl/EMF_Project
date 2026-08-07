using System.Diagnostics;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryOrchestrationService : IInventoryOrchestrationService
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IInventoryRoutingService _routing;
    private readonly IArtifactFactory _artifactFactory;
    private readonly IArtifactIdGenerator _artifactIdGenerator;

    public InventoryOrchestrationStatistics Statistics { get; private set; }
        = new();

    public InventoryOrchestrationService(
        IStreamingDiscoveryService discovery,
        IInventoryRoutingService routing,
        IArtifactFactory artifactFactory,
        IArtifactIdGenerator artifactIdGenerator)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(artifactFactory);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);

        _discovery = discovery;
        _routing = routing;
        _artifactFactory = artifactFactory;
        _artifactIdGenerator = artifactIdGenerator;
    }

    public async IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(
        string sourcePath,
        DiscoveryOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        Statistics = new InventoryOrchestrationStatistics();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var item in _discovery.DiscoverItemsAsync(
                sourcePath,
                options,
                cancellationToken))
            {
                Statistics.ItemsDiscovered++;

                var provider = _routing.SelectProvider(item);

                if (provider is null)
                {
                    Statistics.ItemsSkipped++;
                    continue;
                }

                Statistics.ItemsHandled++;

                var artifactResult = _artifactFactory.Create(
                    item,
                    _artifactIdGenerator.Generate());

                InventoryOrchestrationResult result;

                try
                {
                    var inventory = await provider.CreateInventoryAsync(
                        item.SourcePath,
                        cancellationToken);

                    Statistics.InventoriesCompleted++;

                    result = new InventoryOrchestrationResult
                    {
                        DiscoveredItem = item,
                        Artifact = artifactResult.Artifact,
                        Provenance = artifactResult.Provenance,
                        Success = true,
                        Inventory = inventory
                    };
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException)
                {
                    Statistics.ItemsFailed++;

                    result = new InventoryOrchestrationResult
                    {
                        DiscoveredItem = item,
                        Artifact = artifactResult.Artifact,
                        Provenance = artifactResult.Provenance,
                        Success = false,
                        Message = ex.Message,
                        Inventory = null
                    };
                }

                yield return result;
            }
        }
        finally
        {
            stopwatch.Stop();
            Statistics.Elapsed = stopwatch.Elapsed;
        }
    }
}
