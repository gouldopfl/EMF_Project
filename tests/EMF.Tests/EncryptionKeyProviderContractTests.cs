using EMF.Security.Encryption;

namespace EMF.Tests;

public sealed class EncryptionKeyProviderContractTests
{
    [Fact]
    public void EncryptionKeyProvider_ExposesCurrentKeyIdentity()
    {
        var method =
            typeof(IEncryptionKeyProvider)
                .GetMethod(
                    nameof(IEncryptionKeyProvider.GetCurrentKeyIdAsync));

        Assert.NotNull(method);
    }
}
