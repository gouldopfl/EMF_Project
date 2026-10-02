namespace EMF.Persistence.Storage;

// Only OS-dependent durability, coordination, namespace watching and private permissions belong here.
// Catalog, revision, receipt and bootstrap semantics stay in the common store.
internal interface IContentStoragePlatform
{
    IGenerationNamespaceWatch CreateGenerationNamespaceWatch(string directory);
    void RequirePlatform();
    void RequireSameFileSystem(string rootPath, string stagingParentPath);
    ContentSourceIdentity InspectSourceFile(string path);
    FileStream OpenSourceFile(string path);
    Task<IDisposable> AcquireAdmissionAsync(string rootPath, CancellationToken cancellationToken);
    Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken cancellationToken);
    void FlushDirectory(string path, bool verifyFileSystem = false);
    void CreatePrivateDirectory(string path);
    FileStream CreatePrivateFile(string path, bool asynchronous = false);
    void ValidatePrivatePermissions(string path);
}

internal static class ContentStoragePlatform
{
    internal static IContentStoragePlatform Select()
    {
        if (OperatingSystem.IsLinux()) return new LinuxContentDurability();
        throw new PlatformNotSupportedException("Content storage durability is not implemented for this platform.");
    }
}
