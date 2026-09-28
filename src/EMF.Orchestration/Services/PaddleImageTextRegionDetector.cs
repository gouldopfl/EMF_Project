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
        using var source =
            Cv2.ImDecode(
                image.ToArray(),
                ImreadModes.Color);

        if (source.Empty())
        {
            throw new InvalidDataException(
                "OCR image could not be decoded for " +
                "text-region detection.");
        }

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

                var result = _ocr.Run(oriented);

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

            return output;
        }
    }
}
