namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Explicit presentation inputs outside the frozen V1 snapshot. PackagePreparedDate
/// supplies the source-review date used by visible source checks; it is not an
/// output-generation timestamp. A preserved package retains its recorded date.
/// Typography and page geometry belong to the versioned shared renderer.
/// See docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md for the architectural guarantee.
/// </summary>
public sealed record VeteransReviewerPackageRenderSettings(DateOnly PackagePreparedDate)
{
    internal void Validate()
    {
        if (PackagePreparedDate == default)
            throw new ArgumentException("An explicit package prepared date is required for deterministic rendering.");
    }
}
