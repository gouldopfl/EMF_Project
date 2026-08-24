using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class ArtifactContentStoreFactoryTests
{
    [Fact]
    public void Create_ReturnsNullWithoutAzureKeyConfiguration()
    {
        var vault = Environment.GetEnvironmentVariable(
            "EMF_AZURE_KEY_VAULT_URI");
        var key = Environment.GetEnvironmentVariable(
            "EMF_AZURE_KEY_NAME");
        var version = Environment.GetEnvironmentVariable(
            "EMF_AZURE_KEY_VERSION");

        try
        {
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_VAULT_URI",
                null);
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_NAME",
                null);
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_VERSION",
                null);

            Assert.Null(
                ArtifactContentStoreFactory.Create());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_VAULT_URI",
                vault);
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_NAME",
                key);
            Environment.SetEnvironmentVariable(
                "EMF_AZURE_KEY_VERSION",
                version);
        }
    }
}
