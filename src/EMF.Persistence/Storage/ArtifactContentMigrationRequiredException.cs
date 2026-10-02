namespace EMF.Persistence.Storage;

// Admission boundary for the later explicit offline migration utility. The
// legacy bytes are not damaged and are never imported as an implicit side effect.
public sealed class ArtifactContentMigrationRequiredException : IOException
{
    public ArtifactContentMigrationRequiredException()
        : base("Legacy content layout requires explicit offline migration; automatic migration is disabled.") { }
}
