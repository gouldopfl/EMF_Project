using EMF.Security.Encryption;

namespace EMF.Tests;

public sealed class ContentEncryptionContractTests
{
    [Fact]
    public void EncryptedContent_RequiresCryptographicComponents()
    {
        var encryptedContent = new EncryptedContent
        {
            Ciphertext = [1, 2, 3],
            Nonce = [4, 5, 6],
            AuthenticationTag = [7, 8, 9],
            KeyId = "key-001"
        };

        Assert.NotEmpty(encryptedContent.Ciphertext);
        Assert.NotEmpty(encryptedContent.Nonce);
        Assert.NotEmpty(encryptedContent.AuthenticationTag);
        Assert.Equal("key-001", encryptedContent.KeyId);
    }

    [Fact]
    public void EncryptionService_ExposesEncryptAndDecryptOperations()
    {
        var encryptMethod =
            typeof(IContentEncryptionService)
                .GetMethod(nameof(
                    IContentEncryptionService.EncryptAsync));

        var decryptMethod =
            typeof(IContentEncryptionService)
                .GetMethod(nameof(
                    IContentEncryptionService.DecryptAsync));

        Assert.NotNull(encryptMethod);
        Assert.NotNull(decryptMethod);
    }
}
