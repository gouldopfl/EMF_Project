using System.Reflection;
using EMF.Security.Encryption.Providers;

namespace EMF.Tests;

public sealed class ContentCryptographyProviderContractTests
{
    [Fact]
    public void ContentCryptographyProvider_ExposesEncryptOperation()
    {
        var method =
            typeof(IContentCryptographyProvider)
                .GetMethod(nameof(
                    IContentCryptographyProvider.EncryptAsync));

        Assert.NotNull(method);
    }

    [Fact]
    public void ContentCryptographyProvider_ExposesDecryptOperation()
    {
        var method =
            typeof(IContentCryptographyProvider)
                .GetMethod(nameof(
                    IContentCryptographyProvider.DecryptAsync));

        Assert.NotNull(method);
    }
}
