using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class ArtifactContentStoreFactoryTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", " ", "\t")]
    public void Create_ReturnsNullWithoutAzureKeyConfiguration(
        string? vaultUri,
        string? keyName,
        string? keyVersion)
    {
        var result = ArtifactContentStoreFactory.Create(
            vaultUri,
            keyName,
            keyVersion,
            null);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(
        "https://example.vault.azure.net/",
        null,
        null)]
    [InlineData(
        null,
        "emf-key",
        null)]
    [InlineData(
        null,
        null,
        "0123456789abcdef0123456789abcdef")]
    [InlineData(
        "https://example.vault.azure.net/",
        "emf-key",
        null)]
    [InlineData(
        "https://example.vault.azure.net/",
        null,
        "0123456789abcdef0123456789abcdef")]
    [InlineData(
        null,
        "emf-key",
        "0123456789abcdef0123456789abcdef")]
    public void Create_RejectsPartialAzureKeyConfiguration(
        string? vaultUri,
        string? keyName,
        string? keyVersion)
    {
        Assert.Throws<InvalidOperationException>(
            () => ArtifactContentStoreFactory.Create(
                vaultUri,
                keyName,
                keyVersion,
                null));
    }

    [Fact]
    public void Create_ReturnsEncryptedStoreWithCompleteConfiguration()
    {
        var result = ArtifactContentStoreFactory.Create(
            "https://example.vault.azure.net/",
            "emf-key",
            "0123456789abcdef0123456789abcdef",
            Path.GetTempPath());

        Assert.NotNull(result);
    }
}
