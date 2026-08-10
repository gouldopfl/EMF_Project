using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class LegalAnalysisRegulatoryProvision
{
    public required LegalAnalysisId LegalAnalysisId { get; init; }

    public required RegulatoryProvisionId RegulatoryProvisionId { get; init; }
}
