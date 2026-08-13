using System.Diagnostics;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
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
private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactContentStore? _contentStore;

    public InventoryOrchestrationStatistics Statistics { get; private set; }
        = new();

    public InventoryOrchestrationService(
        IStreamingDiscoveryService discovery,
        IInventoryRoutingService routing,
        IArtifactFactory artifactFactory,
        IArtifactIdGenerator artifactIdGenerator,
    IContentFingerprintService fingerprintService)
        : this(
            discovery,
            routing,
            artifactFactory,
            artifactIdGenerator,
            fingerprintService,
            null)
    {
    }

    public InventoryOrchestrationService(
        IStreamingDiscoveryService discovery,
        IInventoryRoutingService routing,
        IArtifactFactory artifactFactory,
        IArtifactIdGenerator artifactIdGenerator,
        IContentFingerprintService fingerprintService,
        IArtifactContentStore? contentStore)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(artifactFactory);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
    ArgumentNullException.ThrowIfNull(fingerprintService);

        _discovery = discovery;
        _routing = routing;
        _artifactFactory = artifactFactory;
        _artifactIdGenerator = artifactIdGenerator;
    _fingerprintService = fingerprintService;
        _contentStore = contentStore;
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

                var artifactId = _artifactIdGenerator.Generate();

var fingerprint = await _fingerprintService.ComputeAsync(
    item.SourcePath,
    cancellationToken);

var artifactResult = _artifactFactory.Create(
    item,
    artifactId,
    fingerprint);

                if (_contentStore is not null)
                {
                    var content =
                        await File.ReadAllBytesAsync(
                            item.SourcePath,
                            cancellationToken);

                    await _contentStore.WriteAsync(
                        artifactId,
                        content,
                        cancellationToken);
                }

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
