using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Contracts;

// Trusted composition must supply a current decision. Neither a retained hash nor
// a lifecycle owner token grants permission to consume the captured material.
public interface IReviewerCurrentConsumptionAuthorization
{
    Task<bool> AuthorizeAsync(ReviewerConsumptionAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
