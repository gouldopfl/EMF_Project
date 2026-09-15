using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonMedicationLedgerImportService
{
    private readonly IMedicationRepository _repository;
    private readonly IIdGenerator _idGenerator;

    public VeteransBlueButtonMedicationLedgerImportService(
        IMedicationRepository repository,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(idGenerator);

        _repository = repository;
        _idGenerator = idGenerator;
    }

    public async Task<VeteransBlueButtonMedicationLedgerImportResult>
        ImportAsync(
            VeteranId veteranId,
            ArtifactId sourceArtifactId,
            VeteransBlueButtonMedicationLedgerParseResult parsed,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var existing =
            (await _repository.GetMedicationLedgersAsync(
                veteranId,
                cancellationToken))
            .Where(item => item.SourceArtifactId == sourceArtifactId)
            .ToArray();

        if (existing.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple medication ledgers already reference the same source artifact.");
        }

        if (existing.Length == 1)
        {
            var existingEntries =
                await _repository.GetMedicationLedgerEntriesAsync(
                    existing[0].Id,
                    cancellationToken);

            if (!Matches(existing[0], existingEntries, parsed))
            {
                throw new InvalidOperationException(
                    "The source artifact already has a medication ledger with different parsed content.");
            }

            return new VeteransBlueButtonMedicationLedgerImportResult
            {
                Ledger = existing[0],
                Entries = existingEntries,
                AlreadyPersisted = true
            };
        }

        var ledgerId =
            new MedicationLedgerId(_idGenerator.Generate());

        var ledger =
            new MedicationLedger
            {
                Id = ledgerId,
                VeteranId = veteranId,
                SourceArtifactId = sourceArtifactId,
                ReportDate = parsed.ReportDate,
                SourceStartPage = parsed.SourceStartPage,
                SourceEndPage = parsed.SourceEndPage,
                ReportedEntryCount = parsed.ReportedEntryCount,
                ParsedEntryCount = parsed.ParsedEntryCount,
                IsComplete = parsed.IsComplete
            };

        var entries =
            parsed.Entries
                .Select(
                    item =>
                        new MedicationLedgerEntry
                        {
                            Id =
                                new MedicationLedgerEntryId(
                                    _idGenerator.Generate()),
                            MedicationLedgerId = ledgerId,
                            EntryOrdinal = item.EntryOrdinal,
                            SourceStartPage = item.SourceStartPage,
                            SourceEndPage = item.SourceEndPage,
                            MedicationName = item.MedicationName,
                            Strength = item.Strength,
                            Status = item.Status,
                            PrescriptionNumber = item.PrescriptionNumber,
                            PrescribedDate = item.PrescribedDate,
                            LastFilledDate = item.LastFilledDate,
                            LastFilledOnText = item.LastFilledOnText,
                            ExpirationDate = item.ExpirationDate,
                            RefillsLeft = item.RefillsLeft,
                            Directions = item.Directions,
                            Indication = item.Indication,
                            Prescriber = item.Prescriber,
                            Facility = item.Facility,
                            Quantity = item.Quantity
                        })
                .ToArray();

        await _repository.AddMedicationLedgerAsync(
            ledger,
            entries,
            cancellationToken);

        return new VeteransBlueButtonMedicationLedgerImportResult
        {
            Ledger = ledger,
            Entries = entries,
            AlreadyPersisted = false
        };
    }

    private static bool Matches(
        MedicationLedger ledger,
        IReadOnlyList<MedicationLedgerEntry> entries,
        VeteransBlueButtonMedicationLedgerParseResult parsed)
    {
        if (ledger.ReportDate != parsed.ReportDate ||
            ledger.SourceStartPage != parsed.SourceStartPage ||
            ledger.SourceEndPage != parsed.SourceEndPage ||
            ledger.ReportedEntryCount != parsed.ReportedEntryCount ||
            ledger.ParsedEntryCount != parsed.ParsedEntryCount ||
            ledger.IsComplete != parsed.IsComplete ||
            entries.Count != parsed.Entries.Count)
        {
            return false;
        }

        var persistedByOrdinal =
            entries.ToDictionary(item => item.EntryOrdinal);

        foreach (var parsedEntry in parsed.Entries)
        {
            if (!persistedByOrdinal.TryGetValue(
                    parsedEntry.EntryOrdinal,
                    out var persisted) ||
                persisted.SourceStartPage != parsedEntry.SourceStartPage ||
                persisted.SourceEndPage != parsedEntry.SourceEndPage ||
                persisted.MedicationName != parsedEntry.MedicationName ||
                persisted.Strength != parsedEntry.Strength ||
                persisted.Status != parsedEntry.Status ||
                persisted.PrescriptionNumber != parsedEntry.PrescriptionNumber ||
                persisted.PrescribedDate != parsedEntry.PrescribedDate ||
                persisted.LastFilledDate != parsedEntry.LastFilledDate ||
                persisted.LastFilledOnText != parsedEntry.LastFilledOnText ||
                persisted.ExpirationDate != parsedEntry.ExpirationDate ||
                persisted.RefillsLeft != parsedEntry.RefillsLeft ||
                persisted.Directions != parsedEntry.Directions ||
                persisted.Indication != parsedEntry.Indication ||
                persisted.Prescriber != parsedEntry.Prescriber ||
                persisted.Facility != parsedEntry.Facility ||
                persisted.Quantity != parsedEntry.Quantity)
            {
                return false;
            }
        }

        return true;
    }
}
