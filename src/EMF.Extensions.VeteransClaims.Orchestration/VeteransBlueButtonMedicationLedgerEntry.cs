namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonMedicationLedgerEntry
{
    public required int EntryOrdinal { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public required string MedicationName { get; init; }

    public string? Strength { get; init; }

    public required string Status { get; init; }

    public string? PrescriptionNumber { get; init; }

    public DateOnly? PrescribedDate { get; init; }

    public DateOnly? LastFilledDate { get; init; }

    public string? LastFilledOnText { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public int? RefillsLeft { get; init; }

    public string? Directions { get; init; }

    public string? Indication { get; init; }

    public string? Prescriber { get; init; }

    public string? Facility { get; init; }

    public string? Quantity { get; init; }
}
