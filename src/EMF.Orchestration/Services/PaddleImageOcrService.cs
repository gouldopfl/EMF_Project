using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Services;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace EMF.Orchestration.Services;

public sealed class PaddleImageOcrService :
    IImageOcrService
{
    public Task<string?> RecognizeTextAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var source =
            Cv2.ImDecode(
                request.Image.ToArray(),
                ImreadModes.Color);

        if (source.Empty())
            throw new InvalidDataException(
                "The image could not be decoded.");

        var model =
            OcrLanguageResolver.Resolve(request.Language) switch
            {
                OcrLanguage.Chinese => LocalFullModels.ChineseV5,
                OcrLanguage.Korean => LocalFullModels.KoreanV5,
                OcrLanguage.Arabic => LocalFullModels.ArabicV5,
                OcrLanguage.Greek => LocalFullModels.GreekV5,
                OcrLanguage.Thai => LocalFullModels.ThaiV5,
                OcrLanguage.Cyrillic => LocalFullModels.CyrillicV5,
                OcrLanguage.Latin => LocalFullModels.LatinV5,
                _ => LocalFullModels.EnglishV5
            };

        using var ocr =
            new PaddleOcrAll(
                model,
                PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };

        var result = ocr.Run(source);

        return Task.FromResult<string?>(
            string.IsNullOrWhiteSpace(result.Text)
                ? null
                : result.Text);
    }
}
