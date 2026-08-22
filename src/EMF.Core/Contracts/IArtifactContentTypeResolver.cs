using EMF.Core.Models;

namespace EMF.Core.Contracts;

public interface IArtifactContentTypeResolver
{
    string? ResolveContentType(
        Artifact artifact);
}
