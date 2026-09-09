namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementMedicalLiteratureDetails
{
    public required RequirementMedicalLiterature Association { get; init; }

    public required MedicalLiteratureSource Source { get; init; }
}
