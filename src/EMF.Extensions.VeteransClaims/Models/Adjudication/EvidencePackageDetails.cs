namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidencePackageDetails
{
    public required EvidencePackage Package { get; init; }

    public required IReadOnlyList<EvidencePackageArtifact>
        Artifacts { get; init; }
}
