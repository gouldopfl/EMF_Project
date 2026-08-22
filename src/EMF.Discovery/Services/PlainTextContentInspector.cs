using System.Text;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class PlainTextContentInspector :
    IArtifactContentInspector
{
    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "text/plain",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        try
        {
            var text =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetString(content);

            var printableCount =
                text.Count(
                    character =>
                        !char.IsControl(character) ||
                        character is '\r' or '\n' or '\t');

            var ratio =
                text.Length == 0
                    ? 1.0
                    : (double)printableCount / text.Length;

            metadata["textCharacterCount"] = text.Length;
            metadata["textPrintableRatio"] = ratio;

            if (ratio >= 0.95)
            {
                findings.Add(
                    "Content appears to be readable plain text.");
            }
            else
            {
                findings.Add(
                    "Content is valid UTF-8 but contains substantial control characters.");
            }
        }
        catch (DecoderFallbackException)
        {
            findings.Add(
                "Content is not valid UTF-8 text.");
        }
    }
}
