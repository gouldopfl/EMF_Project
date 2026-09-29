namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageExistingOutput(
    byte[]? Docx,
    byte[]? Pdf);
