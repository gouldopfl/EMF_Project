using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

internal static class IntelligenceEvidencePromotionValidator
{
    public static void Validate<TOutput>(
        IntelligenceEvidencePromotionRequest<TOutput> request)
        where TOutput : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Artifact);
        ArgumentNullException.ThrowIfNull(
            request.IntelligenceResult);

        var result = request.IntelligenceResult;

        if (!result.Success || result.Output is null)
            Fail("Only successful intelligence output can be promoted.");

        if (string.IsNullOrWhiteSpace(request.PromotedBy) ||
            request.PromotedUtc == default)
            Fail("Promotion identity and time are required.");

        if (result.SourceArtifactIds is null ||
            result.SourceArtifactIds.Count == 0)
            Fail("Promotion requires source artifact lineage.");

        if (result.CapabilityExecutions is null ||
            result.CapabilityExecutions.Count == 0)
            Fail("Promotion requires provider execution metadata.");

        var hasReviewer =
            !string.IsNullOrWhiteSpace(request.ReviewedBy);
        var hasReviewTime = request.ReviewedUtc.HasValue;

        if (hasReviewer != hasReviewTime)
            Fail("Reviewer identity and time must be supplied together.");

        if (result.RequiresReview && !hasReviewer)
            Fail("Required human review has not been recorded.");

        if (hasReviewTime &&
            (request.ReviewedUtc == default ||
             request.ReviewedUtc < result.CompletedUtc))
            Fail("The review time is invalid.");
    }

    private static void Fail(string reason)
    {
        throw new IntelligenceEvidencePromotionException(
            reason);
    }
}
