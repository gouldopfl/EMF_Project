using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>Openly licensed fonts embedded in the DOCX for both Word and PDF rendering.</summary>
internal static class VeteransReviewerFonts
{
    public const string Body = "DejaVu Serif";
    public const string Monospace = "DejaVu Sans Mono";

    public static void Embed(MainDocumentPart mainPart)
    {
        var table = mainPart.AddNewPart<FontTablePart>();
        table.Fonts = new Fonts();
        foreach (var (family, file) in new[] { (Body, "DejaVuSerif"), (Monospace, "DejaVuSansMono") })
        {
            var font = new Font { Name = family };
            foreach (var bold in new[] { false, true })
            {
                using var input = typeof(VeteransReviewerFonts).Assembly.GetManifestResourceStream(
                    $"EMF.Extensions.VeteransClaims.Orchestration.Fonts.{file}{(bold ? "-Bold" : "")}.ttf")
                    ?? throw new InvalidDataException("Packaged reviewer font is missing.");
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var key = new Guid(SHA256.HashData(bytes).AsSpan(0, 16)).ToString("B").ToUpperInvariant();
                // ECMA-376 font obfuscation: reverse the GUID's hexadecimal byte
                // order, then XOR each of the first 32 font bytes with that key.
                var mask = Convert.FromHexString(key.Replace("{", "").Replace("}", "").Replace("-", "")).Reverse().ToArray();
                for (var i = 0; i < 32; i++) bytes[i] ^= mask[i % 16];
                var part = table.AddFontPart(FontPartType.FontOdttf);
                using var data = new MemoryStream(bytes);
                part.FeedData(data);
                if (bold) font.Append(new EmbedBoldFont { Id = table.GetIdOfPart(part), FontKey = key, Subsetted = false });
                else font.Append(new EmbedRegularFont { Id = table.GetIdOfPart(part), FontKey = key, Subsetted = false });
            }
            table.Fonts.Append(font);
        }
        mainPart.DocumentSettingsPart!.Settings!.AddChild(new EmbedTrueTypeFonts { Val = true }, true);
    }
}
