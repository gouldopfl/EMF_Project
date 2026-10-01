using EMF.Common;
using EMF.Core.Models;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace EMF.Orchestration.Services;

internal interface IPaddleOcrEngine
{
    string? Run(Mat image);
}

internal sealed class PaddleOcrEngineCache
{
    // Process-owned native engines: services never dispose shared engines, and
    // no shutdown callback can race active inference. The OS reclaims them at exit.
    internal static PaddleOcrEngineCache Shared { get; } =
        new(language => new NativeEngine(language));

    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal IPaddleOcrEngine? Engine;
    }

    private readonly Slot[] _slots = Enumerable.Range(0, 8)
        .Select(_ => new Slot()).ToArray();
    private readonly Func<OcrLanguage, IPaddleOcrEngine> _factory;

    internal PaddleOcrEngineCache(Func<OcrLanguage, IPaddleOcrEngine> factory)
    {
        _factory = factory;
    }

    internal async Task<string?> RunAsync(
        OcrLanguage language, Mat image, CancellationToken cancellationToken)
    {
        // Explicit mapping keeps cache cardinality fixed even for invalid enum values.
        var index = language switch
        {
            OcrLanguage.English => 0,
            OcrLanguage.Latin => 1,
            OcrLanguage.Chinese => 2,
            OcrLanguage.Korean => 3,
            OcrLanguage.Arabic => 4,
            OcrLanguage.Greek => 5,
            OcrLanguage.Thai => 6,
            OcrLanguage.Cyrillic => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
        var slot = _slots[index];
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            slot.Engine ??= _factory(language);
            cancellationToken.ThrowIfCancellationRequested();
            return slot.Engine.Run(image);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private sealed class NativeEngine : IPaddleOcrEngine
    {
        private readonly PaddleOcrAll _ocr;

        internal NativeEngine(OcrLanguage language)
        {
            var model = language switch
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
            _ocr = new PaddleOcrAll(model, PaddleDevice.OneDnn(cpuMathThreadCount: 2))
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };
        }

        public string? Run(Mat image)
        {
            using var inferenceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.PaddleTextInference);
            EmfPerformanceTiming.Count(EmfPerformanceCounter.PaddleRuns);
            return _ocr.Run(image).Text;
        }
    }
}
