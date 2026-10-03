namespace EMF.Security.Storage.Models;

public enum ArtifactEnvelopeRewrappingOutcome
{
    NotFound = 0,
    AlreadyCurrent = 1,
    Updated = 2,
    VersionConflict = 3,
    RequiresReview = 4
}
