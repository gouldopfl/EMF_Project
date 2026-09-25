using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed record VeteransReviewerSourcePageCrop(
    ReadOnlyMemory<byte> Content, int Left, int Top, int Width, int Height)
{
    public static VeteransReviewerSourcePageCrop Crop(ReadOnlyMemory<byte> content)
    {
        using var source = SKBitmap.Decode(content.Span)
            ?? throw new InvalidDataException("Printable page is not a decodable image.");
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;
        var pixels = source.Pixels;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            // Only exact, opaque white is expendable. Faint antialiasing,
            // colored links, isolated marks and transparency are all retained.
            if (pixels[y * source.Width + x] == SKColors.White) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        if (right < 0)
            return new(content, 0, 0, source.Width, source.Height);

        // One percent of the shorter source dimension (about 0.085 inches
        // on a Letter page), with at least two pixels on small fixtures.
        var margin = Math.Max(2, (int)Math.Ceiling(Math.Min(source.Width, source.Height) * 0.01));
        left = Math.Max(0, left - margin);
        top = Math.Max(0, top - margin);
        right = Math.Min(source.Width - 1, right + margin);
        bottom = Math.Min(source.Height - 1, bottom + margin);
        var width = right - left + 1;
        var height = bottom - top + 1;
        if (width == source.Width && height == source.Height)
            return new(content, 0, 0, width, height);

        using var subset = new SKBitmap();
        if (!source.ExtractSubset(subset, SKRectI.Create(left, top, width, height)))
            throw new InvalidDataException("Could not preserve the source page crop.");
        using var image = SKImage.FromBitmap(subset);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(encoded.ToArray(), left, top, width, height);
    }
}
