using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class ZipSignatureProvider :
    IArtifactSignatureProvider
{
    public bool TryDetect(
        ReadOnlySpan<byte> content,
        out string contentType,
        out string format)
    {
        contentType = string.Empty;
        format = string.Empty;

        if (content.Length < 4)
            return false;

        var isZip =
            content[0] == 0x50 &&
            content[1] == 0x4B &&
            content[2] is 0x03 or 0x05 or 0x07 &&
            content[3] is 0x04 or 0x06 or 0x08;

        if (!isZip)
            return false;

        contentType = "application/zip";
        format = "ZIP";

        return true;
    }
}
