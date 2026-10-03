using System.Text;
using EMF.Core.Models.Identities;
namespace EMF.Security.Storage;

public static class ArtifactEnvelopeContext
{
    public static byte[] Create(ArtifactId id) => Encoding.UTF8.GetBytes($"EMF-ARTIFACT-ID\0{id.Value}");
}
