using System.Text;
using EMF.Core.Models;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Only reviewer copies reach this boundary. Never write the returned pixels or
// geometry to the source content store or the persisted package snapshot.
internal static class VeteransReviewerPagePrivacy
{
    private static readonly object OcrLock = new();
    private static PaddleOcrAll? _ocr;

    public static PrintableArtifactPage Mask(PrintableArtifactPage page)
    {
        if (!page.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)) return page;
        using var encoded = SKData.CreateCopy(page.Content.Span);
        using var codec = SKCodec.Create(encoded)
            ?? throw new InvalidDataException("Reviewer image could not be decoded for privacy masking.");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("Reviewer image could not be decoded for privacy masking.");
        using var canvas = new SKCanvas(bitmap);
        var changed = false;
        var geometry = page.TextGeometry;
        var glyphs = geometry?.Glyphs.ToList();
        if (geometry is { Width: > 0, Height: > 0 })
        {
            var sx = bitmap.Width / geometry.Width;
            var sy = bitmap.Height / geometry.Height;
            var text = new StringBuilder();
            var map = new List<PrintableArtifactGlyph?>();
            foreach (var group in geometry.Glyphs.GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key))
            {
                PrintableArtifactGlyph? previous = null;
                foreach (var glyph in group.OrderBy(g => g.X))
                {
                    if (previous is not null && glyph.X - previous.EndX > glyph.FontSize * .15)
                    { text.Append(' '); map.Add(null); }
                    text.Append(glyph.Text);
                    map.AddRange(Enumerable.Repeat<PrintableArtifactGlyph?>(glyph, glyph.Text.Length));
                    previous = glyph;
                }
                text.Append('\n'); map.Add(null);
            }
            foreach (var span in VeteransReviewerPackagePrivacySanitizer.Redactions(text.ToString()))
            {
                var removed = map.Skip(span.Start).Take(span.Length).OfType<PrintableArtifactGlyph>().Distinct().ToArray();
                var firstRow = true;
                foreach (var row in removed.GroupBy(g => Math.Round(g.Baseline, 1)))
                {
                    var rect = new SKRect((float)(row.Min(g => g.Left) * sx - 1),
                        (float)(row.Min(g => g.Top) * sy - 1),
                        (float)(row.Max(g => g.Right) * sx + 1),
                        (float)(row.Max(g => g.Bottom) * sy + 1));
                    Paint(canvas, rect, firstRow ? span.Replacement : "");
                    foreach (var glyph in row) glyphs!.Remove(glyph);
                    if (firstRow)
                        glyphs!.Add(row.First() with { Text = span.Replacement,
                            EndX = row.Max(g => g.EndX), Right = row.Max(g => g.Right) });
                    firstRow = false;
                    changed = true;
                }
            }
        }

        // Native geometry cannot account for text baked into scans, screenshots,
        // figures, or rotated PDFs. Inspect these locally with the existing OCR
        // engine as well; never send reviewer content to an external service.
        if (geometry is null || geometry.ContainsGraphics)
        {
            using var source = Cv2.ImDecode(page.Content.ToArray(), ImreadModes.Color);
            lock (OcrLock)
            {
                // Keep scan inspection local, with a serialized CPU predictor.
                _ocr ??= new PaddleOcrAll(LocalFullModels.EnglishV5, PaddleDevice.OneDnn(cpuMathThreadCount: 2))
                    { AllowRotateDetection = false, Enable180Classification = false };
                // The bundled legacy 180-degree classifier has incompatible
                // predictor layouts on some CPU runtimes. Inspect both directions
                // explicitly, including quarter-turn text regions.
                var maskedBoxes = new List<SKRect>();
                for (var turn = 0; turn < 4; turn++)
                {
                    using var oriented = new Mat();
                    if (turn == 0) source.CopyTo(oriented);
                    else Cv2.Rotate(source, oriented, turn switch
                    { 1 => RotateFlags.Rotate90Clockwise, 2 => RotateFlags.Rotate180, _ => RotateFlags.Rotate90Counterclockwise });
                    var result = _ocr.Run(oriented);
                    canvas.Save();
                    if (turn == 1) { canvas.Translate(0, bitmap.Height); canvas.RotateDegrees(-90); }
                    if (turn == 2) { canvas.Translate(bitmap.Width, bitmap.Height); canvas.RotateDegrees(180); }
                    if (turn == 3) { canvas.Translate(bitmap.Width, 0); canvas.RotateDegrees(90); }
                    var regions = result.Regions.OrderBy(r => r.Rect.Center.Y).ThenBy(r => r.Rect.Center.X).ToArray();
                    var text = string.Join("\n", regions.Select(r => r.Text));
                    var spans = VeteransReviewerPackagePrivacySanitizer.Redactions(text);
                    var start = 0;
                    foreach (var region in regions)
                    {
                        var redacted = new StringBuilder(region.Text);
                        foreach (var span in spans.Where(s => s.Start < start + region.Text.Length && s.Start + s.Length > start).Reverse())
                        {
                            var begin = Math.Max(0, span.Start - start);
                            var length = Math.Min(start + region.Text.Length, span.Start + span.Length) - start - begin;
                            redacted.Remove(begin, length).Insert(begin, span.Start >= start ? span.Replacement : "");
                        }
                        start += region.Text.Length + 1;
                        if (redacted.ToString() == region.Text) continue;
                        var box = region.Rect.BoundingRect();
                        var rect = new SKRect(box.Left - 2, box.Top - 2, box.Right + 2, box.Bottom + 2);
                        var originalRect = turn switch
                        {
                            1 => new SKRect(rect.Top, bitmap.Height - rect.Right, rect.Bottom, bitmap.Height - rect.Left),
                            2 => new SKRect(bitmap.Width - rect.Right, bitmap.Height - rect.Bottom, bitmap.Width - rect.Left, bitmap.Height - rect.Top),
                            3 => new SKRect(bitmap.Width - rect.Bottom, rect.Left, bitmap.Width - rect.Top, rect.Right),
                            _ => rect
                        };
                        // A second orientation must not overwrite a replacement
                        // already painted in the first orientation.
                        if (maskedBoxes.Any(b => b.IntersectsWith(originalRect))) continue;
                        Paint(canvas, rect, redacted.ToString());
                        maskedBoxes.Add(originalRect);
                        changed = true;
                    }
                    canvas.Restore();
                }
            }
        }
        if (!changed) return page;
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new() { PageNumber = page.PageNumber, ContentType = page.ContentType,
            Content = png.ToArray(), SuggestedClockwiseRotation = page.SuggestedClockwiseRotation,
            TextGeometry = geometry is null ? null : geometry with { Glyphs = glyphs! } };
    }

    private static void Paint(SKCanvas canvas, SKRect rect, string text)
    {
        using var white = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, white);
        using var stream = typeof(VeteransReviewerFonts).Assembly.GetManifestResourceStream(
            "EMF.Extensions.VeteransClaims.Orchestration.Fonts.DejaVuSansMono.ttf")!;
        using var typeface = SKTypeface.FromStream(stream);
        using var font = new SKFont(typeface, Math.Max(1, rect.Height * .85f));
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var length = font.MeasureText(text);
        if (length > rect.Width) font.Size *= rect.Width / length;
        canvas.DrawText(text, rect.Left, rect.Bottom - Math.Max(1, rect.Height * .12f), SKTextAlign.Left, font, ink);
    }
}
