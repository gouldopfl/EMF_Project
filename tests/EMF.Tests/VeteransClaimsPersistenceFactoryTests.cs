using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence;

namespace EMF.Tests;

public sealed class VeteransClaimsPersistenceFactoryTests
{
    [Fact]
    public async Task Factory_SelectsConfiguredSqliteProvider()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var options =
                new VeteransClaimsPersistenceOptions
                {
                    Provider =
                        VeteransClaimsPersistenceProviders
                            .Sqlite,
                    Settings =
                        new Dictionary<string, string>
                        {
                            [
                                VeteransClaimsPersistenceSettings
                                    .DatabasePath
                            ] = databasePath
                        }
                };

            IVeteransClaimsPersistence persistence =
                VeteransClaimsPersistenceFactory.Create(
                    options);

            await persistence.InitializeAsync();

            Assert.NotNull(persistence.ServiceHistory);
            Assert.NotNull(persistence.Conditions);
            Assert.NotNull(persistence.Regulatory);
            Assert.NotNull(persistence.ServiceConnections);

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await persistence.Veterans.AddVeteranAsync(
                veteran);

            var stored =
                await persistence.Veterans
                    .GetVeteranAsync(veteran.Id);

            Assert.NotNull(stored);
            Assert.Equal(veteran.Id, stored!.Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Factory_RejectsUnsupportedProvider()
    {
        var options =
            new VeteransClaimsPersistenceOptions
            {
                Provider = "UnsupportedDatabase",
                Settings =
                    new Dictionary<string, string>()
            };

        Assert.Throws<NotSupportedException>(
            () =>
                VeteransClaimsPersistenceFactory.Create(
                    options));
    }

    [Fact]
    public void Factory_RejectsMissingSqliteDatabasePath()
    {
        var options =
            new VeteransClaimsPersistenceOptions
            {
                Provider =
                    VeteransClaimsPersistenceProviders
                        .Sqlite,
                Settings =
                    new Dictionary<string, string>()
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                VeteransClaimsPersistenceFactory.Create(
                    options));
    }
}
