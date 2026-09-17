namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IVeteransReviewerPackageDocumentConverter
{
    Task<byte[]> ConvertDocxToPdfAsync(
        ReadOnlyMemory<byte> docx,
        CancellationToken cancellationToken = default);
}
