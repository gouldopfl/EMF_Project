using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteServiceHistoryRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsServiceHistory()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            var veteranRepository =
                new SqliteVeteranRepository(
                    databasePath);

            await veteranRepository.InitializeAsync();

            await veteranRepository.AddVeteranAsync(
                veteran);

            var serviceEvent = new ServiceEvent
            {
                Id =
                    new ServiceEventId(
                        "service-event-001"),
                VeteranId = veteran.Id,
                Description =
                    "Documented duty assignment"
            };

            var exposure = new Exposure
            {
                Id =
                    new ExposureId(
                        "exposure-001"),
                VeteranId = veteran.Id,
                ExposureType = "Environmental"
            };

            IServiceHistoryRepository repository =
                new SqliteServiceHistoryRepository(
                    databasePath);

            await repository.AddServiceEventAsync(
                serviceEvent);

            await repository.AddExposureAsync(exposure);

            await repository
                .AddServiceEventExposureAsync(
                    new ServiceEventExposure
                    {
                        ServiceEventId =
                            serviceEvent.Id,
                        ExposureId =
                            exposure.Id
                    });

            var storedEvent =
                await repository.GetServiceEventAsync(
                    serviceEvent.Id);

            var storedExposure =
                await repository.GetExposureAsync(
                    exposure.Id);

            var veteranEvents =
                await repository.GetServiceEventsAsync(
                    veteran.Id);

            var veteranExposures =
                await repository.GetExposuresAsync(
                    veteran.Id);

            var exposureIds =
                await repository.GetExposureIdsAsync(
                    serviceEvent.Id);

            var serviceEventIds =
                await repository
                    .GetServiceEventIdsAsync(
                        exposure.Id);

            Assert.NotNull(storedEvent);
            Assert.Equal(
                serviceEvent.Description,
                storedEvent!.Description);

            Assert.NotNull(storedExposure);
            Assert.Equal(
                exposure.ExposureType,
                storedExposure!.ExposureType);

            Assert.Equal(
                serviceEvent.Id,
                Assert.Single(veteranEvents).Id);

            Assert.Equal(
                exposure.Id,
                Assert.Single(veteranExposures).Id);

            Assert.Equal(
                exposure.Id,
                Assert.Single(exposureIds));

            Assert.Equal(
                serviceEvent.Id,
                Assert.Single(serviceEventIds));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsCrossVeteranAssociation()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var firstVeteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            var secondVeteran = new Veteran
            {
                Id = new VeteranId("veteran-002")
            };

            var veteranRepository =
                new SqliteVeteranRepository(
                    databasePath);

            await veteranRepository.InitializeAsync();

            await veteranRepository.AddVeteranAsync(
                firstVeteran);

            await veteranRepository.AddVeteranAsync(
                secondVeteran);

            var serviceEvent = new ServiceEvent
            {
                Id =
                    new ServiceEventId(
                        "service-event-001"),
                VeteranId = firstVeteran.Id,
                Description =
                    "First veteran service event"
            };

            var exposure = new Exposure
            {
                Id =
                    new ExposureId(
                        "exposure-001"),
                VeteranId = secondVeteran.Id,
                ExposureType = "Environmental"
            };

            var repository =
                new SqliteServiceHistoryRepository(
                    databasePath);

            await repository.AddServiceEventAsync(
                serviceEvent);

            await repository.AddExposureAsync(exposure);

            var association =
                new ServiceEventExposure
                {
                    ServiceEventId =
                        serviceEvent.Id,
                    ExposureId =
                        exposure.Id
                };

            var exception =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => repository
                            .AddServiceEventExposureAsync(
                                association));

            Assert.Contains(
                "belong to the same veteran",
                exception.Message);

            Assert.Empty(
                await repository.GetExposureIdsAsync(
                    serviceEvent.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
