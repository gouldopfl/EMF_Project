using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class CurrentMedicationServiceTests
{
    [Fact]
    public async Task GetCurrentMedicationsAsync_ReturnsActiveMedication()
    {
        var service = CreateService(
            Record("1", "Trazodone", "Active", 2026, 9, 9));

        var result =
            await service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001"));

        Assert.Equal("Trazodone", Assert.Single(result).MedicationName);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_ReturnsActiveParkedMedication()
    {
        var service = CreateService(
            Record(
                "1",
                "Bupropion",
                MedicationStatuses.ActiveParked,
                2026, 9, 9));

        var result =
            await service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001"));

        Assert.Single(result);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_ExcludesLaterDiscontinuedMedication()
    {
        var service = CreateService(
            Record("1", "Melatonin", "Active", 2025, 1, 1),
            Record("2", "Melatonin", "Discontinued", 2026, 9, 9));

        var result =
            await service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_ReturnsLaterReactivatedMedication()
    {
        var service = CreateService(
            Record("1", "Lamotrigine", "Discontinued", 2025, 1, 1),
            Record("2", "Lamotrigine", "Active", 2026, 9, 9));

        var result =
            await service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001"));

        Assert.Equal(
            "2",
            Assert.Single(result).Id.Value);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_UsesLatestDose()
    {
        var service = CreateService(
            Record("1", "Bupropion", "Active", 2025, 1, 1, "150MG"),
            Record("2", "Bupropion", "Active", 2026, 9, 9, "300MG"));

        var result = await service.GetCurrentMedicationsAsync(
            new VeteranId("veteran-001"));

        Assert.Equal("300MG", Assert.Single(result).Strength);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_CollapsesExactDuplicates()
    {
        var service = CreateService(
            Record("1", "Trazodone", "Active", 2026, 9, 9, "100MG"),
            Record("2", "Trazodone", "Active", 2026, 9, 9, "100MG"));

        var result = await service.GetCurrentMedicationsAsync(
            new VeteranId("veteran-001"));

        Assert.Single(result);
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_RejectsSameDateConflict()
    {
        var service = CreateService(
            Record("1", "Lamotrigine", "Active", 2026, 9, 9, "200MG"),
            Record("2", "Lamotrigine", "Discontinued", 2026, 9, 9, "200MG"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001")));
    }

    [Fact]
    public async Task GetCurrentMedicationsAsync_RejectsVeteranMismatch()
    {
        var record = Record(
            "1", "Trazodone", "Active", 2026, 9, 9);

        record = new MedicationRecord
        {
            Id = record.Id,
            VeteranId = new VeteranId("wrong-veteran"),
            SourceArtifactId = record.SourceArtifactId,
            RecordDate = record.RecordDate,
            SourcePage = record.SourcePage,
            MedicationName = record.MedicationName,
            Status = record.Status
        };

        var service = CreateService(record);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetCurrentMedicationsAsync(
                new VeteranId("veteran-001")));
    }

    private static CurrentMedicationService CreateService(
        params MedicationRecord[] records) =>
        new(new FakeMedicationRepository(records));

    private static MedicationRecord Record(
        string id,
        string name,
        string status,
        int year,
        int month,
        int day,
        string? strength = null) =>
        new()
        {
            Id = new MedicationRecordId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId = new ArtifactId("blue-button"),
            RecordDate = new DateOnly(year, month, day),
            SourcePage = 1,
            MedicationName = name,
            Strength = strength,
            Status = status
        };

    private sealed class FakeMedicationRepository(
        IReadOnlyList<MedicationRecord> records) :
        IMedicationRepository
    {
        public Task AddMedicationRecordAsync(
            MedicationRecord record,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MedicationRecord?> GetMedicationRecordAsync(
            MedicationRecordId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(records.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(records);

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MedicationRecord>>(
                records.Where(x =>
                    string.Equals(
                        x.MedicationName,
                        medicationName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray());
    }
}
