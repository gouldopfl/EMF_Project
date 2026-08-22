namespace EMF.Discovery.Contracts;

public interface IArtifactSignatureProvider
{
    bool TryDetect(
        ReadOnlySpan<byte> content,
        out string contentType,
        out string format);
}
