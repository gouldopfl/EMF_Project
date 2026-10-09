using EMF.Security.Encryption.Envelope;
using EMF.Core.Contracts.Storage;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Inventory.Models;
using EMF.Inventory.Persistence;
using EMF.Inventory.Providers;
using EMF.Inventory.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
using EMF.Persistence.Storage;

namespace EMF.ConsoleApplication;

// Host-supplied protection and ingestion capabilities. No CLI-derived authorization.
public sealed record InventoryRuntimeComposition(string StateRoot, IEnvelopeEncryptionService Encryption,
    InventoryMode Mode = InventoryMode.MetadataOnly, IInventoryChildIngestionAdapter? Child = null,
    InventoryProcessingLimits? Limits = null)
{
    internal static InventoryRuntimeComposition? FromEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable("EMF_INVENTORY_CONTENT_MODE") ?? "metadata";
        if (mode == "protected") throw new UnauthorizedAccessException("Stock host has no authoritative Inventory ingestion capability.");
        if (mode != "metadata") throw new InvalidOperationException("Unknown Inventory content mode.");
        var vault = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_VAULT_URI");
        var key = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_NAME");
        var version = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_VERSION");
        if (string.IsNullOrWhiteSpace(vault) && string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(version)) return null;
        if (string.IsNullOrWhiteSpace(vault) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Retained-input protection requires complete existing Key Vault configuration.");
        var options = new AzureKeyVaultOptions { VaultUri = vault, KeyName = key, KeyVersion = version };
        var encryption = new AzureEnvelopeEncryptionService(new ConfiguredAzureKeyReferenceProvider(options), new AzureKeyCryptographyFactory(options));
        return new(Environment.GetEnvironmentVariable("EMF_INVENTORY_STATE_PATH") ?? "/var/lib/emf/inventory", encryption);
    }
    public void Validate()
    {
        (Limits ?? new()).Validate();
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        ArgumentNullException.ThrowIfNull(Encryption);
        if (Mode == InventoryMode.ProtectedContent && Child is null)
            throw new UnauthorizedAccessException("Authoritative Inventory ingestion capability is unavailable.");
    }
    internal (SqliteInventoryParentJournal Journal, SqliteInventoryRetainedSnapshotStore Snapshots,
        LinuxInventorySnapshotWorkspace Workspace) CreateRetention()
    {
        Validate(); var limits = Limits ?? new();
        var root = Path.GetFullPath(StateRoot);
        var workspace = new LinuxInventorySnapshotWorkspace(Path.Combine(root, "plaintext"));
        var journal = new SqliteInventoryParentJournal(Path.Combine(root, "journal", "inventory.sqlite"), limits);
        var physical = new FileSystemArtifactContentStore(Path.Combine(root, "retained-encrypted"), limits.MaximumProtectedBytes);
        var storage = new InventoryProtectedSnapshotStorageAdapter(physical, Encryption, limits);
        return (journal, new(journal, storage, workspace, limits), workspace);
    }
}
