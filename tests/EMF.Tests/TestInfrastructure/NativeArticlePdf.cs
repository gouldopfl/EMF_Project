using System.Globalization;
using System.Text;

namespace EMF.Tests.TestInfrastructure;

internal static class NativeArticlePdf
{
    public static byte[] CreateSideways(int clockwiseCorrection)
    {
        var pdf = Encoding.ASCII.GetString(Create((612, 792, clockwiseCorrection)));
        // Keep byte offsets intact while removing the viewer's rotation. This
        // leaves the table sideways inside an otherwise portrait source page.
        return Encoding.ASCII.GetBytes(pdf.Replace($"/Rotate {clockwiseCorrection}",
            "/Rotate " + "0".PadLeft(clockwiseCorrection.ToString().Length), StringComparison.Ordinal));
    }

    // Deliberately writes /Rotate independently of MediaBox, including pages
    // whose raw portrait bounds display as landscape in a conforming viewer.
    public static byte[] Create(params (int Width, int Height, int Rotation)[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Length} /Kids [{string.Join(" ", pages.Select((_, i) => $"{4 + i * 2} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        foreach (var (width, height, rotation) in pages)
        {
            var contentId = objects.Count + 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Rotate {rotation} /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>");
            var content = new StringBuilder("1 0 0 rg 0 0 20 20 re f\n0 0 0 rg\n");
            // Counter-rotate the article's content so it reads upright when
            // /Rotate is honored; the red marker stays at the raw lower left.
            content.Append(rotation switch
            {
                90 => $"0 1 -1 0 {width} 0 cm\n",
                180 => $"-1 0 0 -1 {width} {height} cm\n",
                270 => $"0 -1 1 0 0 {height} cm\n",
                _ => ""
            });
            var displayWidth = rotation is 90 or 270 ? height : width;
            var displayHeight = rotation is 90 or 270 ? width : height;
            content.Append($"BT /F1 12 Tf 30 {displayHeight - 35} Td (Synthetic article - table and chart) Tj ET\n");
            for (var row = 0; row < 16; row++)
            {
                var y = displayHeight - 65 - row * 16;
                content.Append($"BT /F1 10 Tf 30 {y} Td (Group {row:D2}     123.45     0.987     Table values) Tj ET\n");
                content.Append($"30 {y - 3} m {displayWidth - 30} {y - 3} l S\n");
            }
            content.Append($"0.2 0.4 0.8 rg 30 60 {displayWidth - 60} 50 re f\n");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        }
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
