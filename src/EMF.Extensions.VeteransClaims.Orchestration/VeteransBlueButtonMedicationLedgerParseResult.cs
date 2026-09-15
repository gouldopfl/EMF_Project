namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonMedicationLedgerParseResult
{
    public required DateOnly ReportDate { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public int? ReportedEntryCount { get; init; }

    public required IReadOnlyList<VeteransBlueButtonMedicationLedgerEntry>
        Entries { get; init; }

    public int ParsedEntryCount => Entries.Count;

    public bool IsComplete =>
        ReportedEntryCount.HasValue &&
        ReportedEntryCount.Value == ParsedEntryCount;
}
