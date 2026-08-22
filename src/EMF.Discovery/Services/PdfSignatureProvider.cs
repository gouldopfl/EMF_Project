using System.Text;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class PdfSignatureProvider :
    IArtifactSignatureProvider
{
    private static readonly byte[] Signature =
        Encoding.ASCII.GetBytes("%PDF-");

    public bool TryDetect(
        ReadOnlySpan<byte> content,
        out string contentType,
        out string format)
    {
        contentType = string.Empty;
        format = string.Empty;

        if (content.Length < Signature.Length)
            return false;

        if (!content[..Signature.Length]
            .SequenceEqual(Signature))
        {
            return false;
        }

        contentType = "application/pdf";
        format = "PDF";
        return true;
    }
}
