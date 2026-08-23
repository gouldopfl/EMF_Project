using EMF.Core.Models;

namespace EMF.Core.Contracts;

public interface IImageOcrService
{
    Task<string?> RecognizeTextAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default);
}
