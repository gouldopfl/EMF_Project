using Azure.Security.KeyVault.Keys;

namespace EMF.Security.Azure.Clients;

public interface IAzureKeyVaultClientFactory
{
    KeyClient CreateKeyClient();
}
