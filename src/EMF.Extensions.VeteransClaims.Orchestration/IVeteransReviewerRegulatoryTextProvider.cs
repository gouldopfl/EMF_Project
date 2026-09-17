namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IVeteransReviewerRegulatoryTextProvider
{
    Task<IReadOnlyList<VeteransReviewerApplicableRegulation>>
        GetCurrentAsync(
            IReadOnlyList<string> citations,
            CancellationToken cancellationToken = default);
}
