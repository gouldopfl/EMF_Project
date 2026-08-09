using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryWorkflowActivity : IWorkflowActivity
{
    private readonly IInventoryOrchestrationService _service;
    private readonly string _sourcePath;
    private readonly DiscoveryOptions _options;

    public InventoryWorkflowActivity(
        IInventoryOrchestrationService service,
        string sourcePath,
        DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        _service = service;
        _sourcePath = sourcePath;
        _options = options;
    }

    public string Id => "inventory";

    public string Name => "Inventory";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var total = 0;
        var failed = 0;

        await foreach (var result in _service.ExecuteAsync(
            _sourcePath,
            _options,
            cancellationToken))
        {
            total++;

            if (!result.Success)
            {
                failed++;
            }
        }

        return new WorkflowActivityResult
        {
            Succeeded = failed == 0,
            Message = $"Inventory processed {total} item(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
