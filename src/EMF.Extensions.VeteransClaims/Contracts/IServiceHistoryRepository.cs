using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IServiceHistoryRepository
{
    Task AddServiceEventAsync(
        ServiceEvent serviceEvent,
        CancellationToken cancellationToken = default);

    Task<ServiceEvent?> GetServiceEventAsync(
        ServiceEventId serviceEventId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceEvent>>
        GetServiceEventsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default);

    Task AddExposureAsync(
        Exposure exposure,
        CancellationToken cancellationToken = default);

    Task<Exposure?> GetExposureAsync(
        ExposureId exposureId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Exposure>> GetExposuresAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default);

    Task AddServiceEventExposureAsync(
        ServiceEventExposure association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            ServiceEventId serviceEventId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceEventId>>
        GetServiceEventIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default);
}
