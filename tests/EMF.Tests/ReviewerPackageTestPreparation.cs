using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

// Synthetic fixtures have an explicit, stable preparation date. Production render
// APIs must never invent today's visible date; see the determinism contract.
internal static class ReviewerPackageTestPreparation
{
    internal static byte[] Render(VeteransReviewerPackageDetails details,
        IReadOnlyList<VeteransReviewerApplicableRegulation>? applicableRegulations = null,
        DateOnly? sourceReviewDate = null, ReviewerPackageCover? resolvedCover = null) =>
        VeteransReviewerPackageDocxRenderer.Render(details, applicableRegulations,
            sourceReviewDate ?? new DateOnly(2026, 9, 29), resolvedCover);
}
