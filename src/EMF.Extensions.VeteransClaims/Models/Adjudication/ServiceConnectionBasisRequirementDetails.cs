using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisRequirementDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required Requirement Requirement { get; init; }

    public required RegulatoryProvision RegulatoryProvision { get; init; }

    public required RequirementEvidenceResponsivenessAssessment
        Responsiveness { get; init; }

    public required EvidenceDevelopmentChecklist
        DevelopmentChecklist { get; init; }
}
