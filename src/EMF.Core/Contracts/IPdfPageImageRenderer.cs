namespace EMF.Core.Contracts;

public interface IPdfPageImageRenderer
{
    Task<byte[]> RenderPageAsync(
        ReadOnlyMemory<byte> pdf,
        int pageIndex,
        CancellationToken cancellationToken = default);
}
