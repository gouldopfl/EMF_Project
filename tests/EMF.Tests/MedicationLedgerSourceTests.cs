using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class MedicationLedgerSourceTests
{
    [Theory]
    [InlineData("Examplemed", "Example VA Clinic", "RX-1", true)]
    [InlineData("Examplemed", "Example Veterans' Administration Medical Center", "RX-1", true)]
    [InlineData("Non-VA Examplemed", "Example VA Clinic", "RX-1", false)]
    [InlineData("Examplemed", "Non-VA clinic", "RX-1", false)]
    [InlineData("Examplemed", "Community Pharmacy", "RX-1", false)]
    [InlineData("Examplemed", null, "RX-1", false)]
    [InlineData("Examplemed", "Example VA Clinic", null, false)]
    public void IsVaPrescription_RequiresPositiveVaProvenanceAndExcludesNonVa(
        string name, string? facility, string? prescription, bool expected)
    {
        var entry = new MedicationLedgerEntry
        {
            Id = new("entry"), MedicationLedgerId = new("ledger"), EntryOrdinal = 1,
            SourceStartPage = 1, SourceEndPage = 1, MedicationName = name,
            Status = "active", Facility = facility, PrescriptionNumber = prescription
        };
        Assert.Equal(expected, MedicationLedgerSource.IsVaPrescription(entry));
    }
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void VerifiedSourceCanSupplyMissingFacilityButNonVaAlwaysWins(bool nonVaEntry, bool nonVaSource, bool expected)
    {
        var entry = new MedicationLedgerEntry
        {
            Id = new("entry"), MedicationLedgerId = new("ledger"), EntryOrdinal = 1,
            SourceStartPage = 1, SourceEndPage = 1, MedicationName = nonVaEntry ? "Non-VA Example" : "Example",
            Status = "active", PrescriptionNumber = "RX-1"
        };
        Assert.Equal(expected, MedicationLedgerSource.IsVaPrescription(entry,
            new MedicationSourceEvidence(true, nonVaSource, "Verified source record")));
    }

}
