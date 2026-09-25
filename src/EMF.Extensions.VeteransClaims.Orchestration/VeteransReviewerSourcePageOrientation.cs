using EMF.Core.Models;
using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerSourcePageOrientation
{
    public static ReadOnlyMemory<byte> Orient(PrintableArtifactPage page)
    {
        if (page.SuggestedClockwiseRotation == 0) return page.Content;
        if (page.SuggestedClockwiseRotation is not (90 or 180 or 270))
            throw new InvalidDataException("Source page presentation rotation must be a quarter turn.");
        using var source = SKBitmap.Decode(page.Content.Span)
            ?? throw new InvalidDataException("Printable page is not a decodable image.");
        var swap = page.SuggestedClockwiseRotation != 180;
        using var rotated = new SKBitmap(swap ? source.Height : source.Width,
            swap ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
            canvas.RotateDegrees(page.SuggestedClockwiseRotation);
            canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f,
                new SKSamplingOptions(SKFilterMode.Nearest));
        }
        // Rotate every source pixel together; no crop, resampling, reflow, or
        // reconstruction of the article's text, figures, or tables.
        using var image = SKImage.FromBitmap(rotated);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
