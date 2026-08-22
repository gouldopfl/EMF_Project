namespace EMF.Discovery.Contracts;

public interface IArtifactContentInspector
{
    bool CanInspect(string contentType);

    void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings);
}
