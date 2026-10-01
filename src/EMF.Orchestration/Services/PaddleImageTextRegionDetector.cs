using EMF.Common;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace EMF.Orchestration.Services;

public sealed record PaddleImageTextRegion(
    string Text,
    float Left,
    float Top,
    float Right,
    float Bottom,
    int QuarterTurnsClockwise);

public static class PaddleImageTextRegionDetector
{
    private static readonly object OcrLock = new();
    private static PaddleOcrAll? _ocr;

    public static IReadOnlyList<PaddleImageTextRegion> Detect(
        ReadOnlyMemory<byte> image)
    {
        using var detectionTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.PaddleRegionDetection);
        using var decodeTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.PaddleDecode);
        using var source =
            Cv2.ImDecode(
                image.ToArray(),
                ImreadModes.Color);

        decodeTiming.Dispose();

        if (source.Empty())
        {
            throw new InvalidDataException(
                "OCR image could not be decoded for " +
                "text-region detection.");
        }

        var rasterRequest = EmfPerformanceTiming.RecordPrivacyRaster(image);
        EmfPerformanceTiming.Count(EmfPerformanceCounter.PrivacyOcrPages);
        lock (OcrLock)
        {
            _ocr ??=
                new PaddleOcrAll(
                    LocalFullModels.EnglishV5,
                    PaddleDevice.OneDnn(
                        cpuMathThreadCount: 2))
                {
                    AllowRotateDetection = false,
                    Enable180Classification = false
                };

            var output =
                new List<PaddleImageTextRegion>();

            for (var turn = 0;
                 turn < 4;
                 turn++)
            {
                using var oriented = new Mat();

                if (turn == 0)
                {
                    source.CopyTo(oriented);
                }
                else
                {
                    Cv2.Rotate(
                        source,
                        oriented,
                        turn switch
                        {
                            1 =>
                                RotateFlags.Rotate90Clockwise,
                            2 =>
                                RotateFlags.Rotate180,
                            _ =>
                                RotateFlags.Rotate90Counterclockwise
                        });
                }

                using var inferenceTiming = EmfPerformanceTiming.Measure(turn switch
                {
                    0 => EmfPerformancePhase.Ocr0,
                    1 => EmfPerformancePhase.Ocr90,
                    2 => EmfPerformancePhase.Ocr180,
                    _ => EmfPerformancePhase.Ocr270
                });
                EmfPerformanceTiming.Count(EmfPerformanceCounter.PaddleRuns);
                if (rasterRequest.PreviouslyCompleted)
                    EmfPerformanceTiming.Count(EmfPerformanceCounter.PotentialPaddleRunsAvoided);
                var result = _ocr.Run(oriented);
                inferenceTiming.Dispose();

                foreach (var region in
                         result.Regions
                             .OrderBy(
                                 region =>
                                     region.Rect.Center.Y)
                             .ThenBy(
                                 region =>
                                     region.Rect.Center.X))
                {
                    var box =
                        region.Rect.BoundingRect();

                    output.Add(
                        new PaddleImageTextRegion(
                            region.Text,
                            box.Left,
                            box.Top,
                            box.Right,
                            box.Bottom,
                            turn));
                }
            }

            rasterRequest.Complete();
            return output;
        }
    }
}
