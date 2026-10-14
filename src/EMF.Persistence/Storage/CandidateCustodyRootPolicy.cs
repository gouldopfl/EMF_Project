namespace EMF.Persistence.Storage;

// The reserved component and every descendant are inaccessible to generic
// content/staging/migration writers. No caller-supplied opt-out capability exists.
internal static class CandidateCustodyRootPolicy
{
    internal const string Component="zip-candidate-custody";
    internal static string ResolveGenericTarget(string root,string relative)
    {
        var full=Path.GetFullPath(Path.Combine(root,relative));
        if(full.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Contains(Component,StringComparer.Ordinal))
            throw new InvalidOperationException("Generic resolved target enters D1 custody.");
        LinuxZipCandidateNamespace.PinExistingGenericAncestors(Path.GetDirectoryName(full)!);
        if(new FileInfo(full).LinkTarget is not null||new DirectoryInfo(full).LinkTarget is not null)
            throw new IOException("Generic target is a symbolic link.");
        return full;
    }
    internal static void RejectGenericRoot(string root)
    {
        var full=Path.GetFullPath(root);
        if(full.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
            .Contains(Component,StringComparer.Ordinal))
            throw new InvalidOperationException("D1 custody is reserved for the certified native namespace.");
        LinuxZipCandidateNamespace.PinExistingGenericAncestors(full);
        // A symlink alias must not bypass component validation. Reject existing
        // linked ancestors before a generic writer can create/admit a root.
        for(var p=full;p is not null;p=Path.GetDirectoryName(p))
            if(new DirectoryInfo(p).LinkTarget is not null||new FileInfo(p).LinkTarget is not null)
                throw new IOException("Generic storage root contains a symbolic link.");
    }
}
