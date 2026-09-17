namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageDocumentOutput(
    byte[]? Docx,
    byte[]? Pdf);
