namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageDocumentConverterInfo(
    string Identity,
    string Version)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Identity) ||
            string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidDataException(
                "Reviewer-package PDF converter identity is incomplete.");
        }
    }
}

/// <summary>
/// Supplies the concrete converter identity used for exact PDF output provenance.
/// </summary>
public interface IVeteransReviewerPackageDocumentConverterInfoProvider
{
    Task<VeteransReviewerPackageDocumentConverterInfo> GetDocumentConverterInfoAsync(
        CancellationToken cancellationToken = default);
}
