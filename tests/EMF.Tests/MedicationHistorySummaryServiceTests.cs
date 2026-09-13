using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class MedicationHistorySummaryServiceTests
{
    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_ReturnsEarliestRelease()
    {
        var service = CreateService(
            Event("2", 2025, 5, 22),
            Event("1", 2022, 12, 29));

        var result =
            await service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "Trazodone HCl");

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2022, 12, 29), result!.EventDate);
        Assert.Equal("1", result.Id.Value);
    }

    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_IgnoresOtherEventTypes()
    {
        var service = CreateService(
            Event(
                "1",
                2020, 1, 1,
                MedicationHistoryEventTypes.DocumentedUse),
            Event("2", 2022, 12, 29));

        var result =
            await service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "Trazodone HCl");

        Assert.Equal(
            new DateOnly(2022, 12, 29),
            Assert.IsType<MedicationHistoryEvent>(result).EventDate);
    }

    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_MatchesNameCaseInsensitively()
    {
        var service = CreateService(
            Event("1", 2022, 12, 29, medicationName: "TRAZODONE HCL"));

        var result =
            await service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "trazodone hcl");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_ReturnsNullWithoutRelease()
    {
        var service = CreateService(
            Event(
                "1",
                2022, 12, 29,
                MedicationHistoryEventTypes.DocumentedUse));

        var result =
            await service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "Trazodone HCl");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_RejectsVeteranMismatch()
    {
        var historyEvent =
            Event("1", 2022, 12, 29);

        historyEvent =
            new MedicationHistoryEvent
            {
                Id = historyEvent.Id,
                VeteranId = new VeteranId("wrong-veteran"),
                SourceArtifactId = historyEvent.SourceArtifactId,
                EventDate = historyEvent.EventDate,
                SourcePage = historyEvent.SourcePage,
                MedicationName = historyEvent.MedicationName,
                EventType = historyEvent.EventType
            };

        var service = CreateService(historyEvent);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "Trazodone HCl"));
    }

    [Fact]
    public async Task GetEarliestDocumentedReleaseAsync_RejectsMedicationMismatch()
    {
        var service = CreateService(
            Event("1", 2022, 12, 29, medicationName: "Bupropion HCl"),
            filterByName: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetEarliestDocumentedReleaseAsync(
                new VeteranId("veteran-001"),
                "Trazodone HCl"));
    }

    private static MedicationHistorySummaryService CreateService(
        MedicationHistoryEvent historyEvent,
        bool filterByName = true) =>
        CreateService([historyEvent], filterByName);

    private static MedicationHistorySummaryService CreateService(
        MedicationHistoryEvent first,
        MedicationHistoryEvent second) =>
        CreateService([first, second]);

    private static MedicationHistorySummaryService CreateService(
        IReadOnlyList<MedicationHistoryEvent> events,
        bool filterByName = true) =>
        new(new FakeMedicationRepository(events, filterByName));

    private static MedicationHistoryEvent Event(
        string id,
        int year,
        int month,
        int day,
        string eventType = MedicationHistoryEventTypes.LastReleased,
        string medicationName = "Trazodone HCl") =>
        new()
        {
            Id = new MedicationHistoryEventId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId = new ArtifactId("blue-button"),
            EventDate = new DateOnly(year, month, day),
            SourcePage = 2448,
            MedicationName = medicationName,
            EventType = eventType
        };

    private sealed class FakeMedicationRepository(
        IReadOnlyList<MedicationHistoryEvent> events,
        bool filterByName) :
        IMedicationRepository
    {
        public Task AddMedicationRecordAsync(
            MedicationRecord medicationRecord,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MedicationRecord?> GetMedicationRecordAsync(
            MedicationRecordId medicationRecordId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationHistoryEvent>>
            GetMedicationHistoryEventsAsync(
                VeteranId veteranId,
                string medicationName,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MedicationHistoryEvent>>(
                filterByName
                    ? events.Where(
                            item =>
                                string.Equals(
                                    item.MedicationName,
                                    medicationName,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                    : events);
    }
}
