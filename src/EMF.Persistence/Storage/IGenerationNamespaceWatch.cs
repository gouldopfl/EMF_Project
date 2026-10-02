namespace EMF.Persistence.Storage;

// Provider-private epoch coverage, never catalog authority. Create before the
// first enumeration and retain the same instance until the sweep is discarded.
internal interface IGenerationNamespaceWatch : IDisposable
{
    bool Changed();
    void RequireUnchanged();
    void AcknowledgeOwnDeletion(string name);
}
