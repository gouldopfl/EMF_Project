namespace EMF.Inventory.Models;

public sealed record InventoryProcessingLimits
{
    public long MaximumSnapshotBytes { get; init; } = 64L * 1024 * 1024;
    public long MaximumPlaintextBytes { get; init; } = 64L * 1024 * 1024;
    public long MaximumProtectedBytes { get; init; } = 96L * 1024 * 1024;
    public long MaximumRetainedBytes { get; init; } = 512L * 1024 * 1024;
    public int MaximumPlanItems { get; init; } = 128;
    public int BackupBatchPages { get; init; } = 64;
    public long MaximumPageAttempts { get; init; } = 262144;
    public int MaximumBusyRetries { get; init; } = 8;
    public int MaximumTables { get; init; } = 512;
    public int MaximumColumns { get; init; } = 4096;
    public int MaximumTextCharacters { get; init; } = 1024 * 1024;
    public int MaximumStringCharacters { get; init; } = 4096;
    public int MaximumDirectoryEntries { get; init; } = 100000;
    public int MaximumDirectories { get; init; } = 4096;
    public int MaximumDepth { get; init; } = 64;
    public int MaximumPlanJsonBytes { get; init; } = 4 * 1024 * 1024;
    public long MaximumQueryInstructions { get; init; } = 5000000;
    public TimeSpan ExecutionBudget { get; init; } = TimeSpan.FromMinutes(2);

    public void Validate()
    {
        if (MaximumSnapshotBytes <= 0 || MaximumSnapshotBytes > MaximumPlaintextBytes ||
            MaximumPlaintextBytes > 64L * 1024 * 1024 || MaximumProtectedBytes <= MaximumPlaintextBytes ||
            MaximumProtectedBytes > 96L * 1024 * 1024 || MaximumRetainedBytes < MaximumProtectedBytes ||
            MaximumPlanItems is < 1 or > 128 || BackupBatchPages is < 1 or > 1024 ||
            MaximumPageAttempts < BackupBatchPages || MaximumBusyRetries is < 0 or > 100 ||
            MaximumTables is < 1 or > 4096 || MaximumColumns is < 1 or > 65536 ||
            MaximumStringCharacters is < 1 or > 4096 || MaximumTextCharacters < MaximumStringCharacters ||
            MaximumTextCharacters > 4 * 1024 * 1024 || MaximumDirectoryEntries < 1 ||
            MaximumDirectories < 1 || MaximumDepth is < 1 or > 128 ||
            MaximumPlanJsonBytes is < 1024 or > 16 * 1024 * 1024 || MaximumQueryInstructions < 1000 ||
            ExecutionBudget <= TimeSpan.Zero || ExecutionBudget > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(InventoryProcessingLimits), "Invalid Inventory admission profile.");
    }

}
