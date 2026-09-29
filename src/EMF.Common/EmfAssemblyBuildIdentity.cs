using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace EMF.Common;

public static class EmfAssemblyBuildIdentity
{
    public static string GetModuleVersionId(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return "mvid:" +
            assembly.ManifestModule.ModuleVersionId.ToString("D");
    }

    public static EmfBuildArtifactIdentity Capture(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var loadedName = assembly.GetName().Name;
        var loadedRevision = GetSourceRevisionId(assembly);
        var loadedConfiguration =
            assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        var loadedFramework =
            assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
                ?.FrameworkName;
        var loadedModuleVersionId = GetModuleVersionId(assembly);

        if (string.IsNullOrWhiteSpace(loadedName) ||
            string.IsNullOrWhiteSpace(loadedRevision) ||
            string.IsNullOrWhiteSpace(loadedConfiguration) ||
            string.IsNullOrWhiteSpace(loadedFramework) ||
            string.IsNullOrWhiteSpace(assembly.Location))
        {
            throw new InvalidDataException(
                "Compiled assembly identity is incomplete.");
        }

        var snapshot = ReadStableFileSnapshot(assembly.Location);

        if (!string.Equals(
                loadedName,
                snapshot.AssemblyName,
                StringComparison.Ordinal) ||
            !string.Equals(
                loadedRevision,
                snapshot.SourceRevisionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                loadedConfiguration,
                snapshot.Configuration,
                StringComparison.Ordinal) ||
            !string.Equals(
                loadedFramework,
                snapshot.TargetFramework,
                StringComparison.Ordinal) ||
            !string.Equals(
                loadedModuleVersionId,
                snapshot.ModuleVersionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Compiled assembly file does not match the loaded assembly.");
        }

        return new EmfBuildArtifactIdentity(
            snapshot.AssemblyName,
            snapshot.SourceRevisionId,
            snapshot.Configuration,
            snapshot.TargetFramework,
            snapshot.ModuleVersionId,
            snapshot.Sha256,
            snapshot.ByteLength);
    }

    public static string GetSha256(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(assembly.Location))
        {
            throw new InvalidOperationException(
                "Assembly location is required for exact build hashing.");
        }

        var bytes = File.ReadAllBytes(assembly.Location);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }


    public static string? GetSourceRevisionId(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return ParseSourceRevisionId(version);
    }

    private static StableAssemblyFileSnapshot ReadStableFileSnapshot(
        string location)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(location);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "Compiled assembly file could not be read.",
                ex);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.PrefetchMetadata);

        if (!peReader.HasMetadata)
        {
            throw new InvalidDataException(
                "Compiled assembly file does not contain metadata.");
        }

        var metadata = peReader.GetMetadataReader();
        var assemblyDefinition = metadata.GetAssemblyDefinition();
        var moduleDefinition = metadata.GetModuleDefinition();

        var assemblyName =
            metadata.GetString(assemblyDefinition.Name);

        var informationalVersion =
            ReadAssemblyStringAttribute(
                metadata,
                assemblyDefinition,
                "System.Reflection",
                "AssemblyInformationalVersionAttribute");

        var configuration =
            ReadAssemblyStringAttribute(
                metadata,
                assemblyDefinition,
                "System.Reflection",
                "AssemblyConfigurationAttribute");

        var targetFramework =
            ReadAssemblyStringAttribute(
                metadata,
                assemblyDefinition,
                "System.Runtime.Versioning",
                "TargetFrameworkAttribute");

        var sourceRevisionId =
            ParseSourceRevisionId(informationalVersion);

        var moduleVersionId =
            "mvid:" +
            metadata
                .GetGuid(moduleDefinition.Mvid)
                .ToString("D");

        if (string.IsNullOrWhiteSpace(assemblyName) ||
            string.IsNullOrWhiteSpace(sourceRevisionId) ||
            string.IsNullOrWhiteSpace(configuration) ||
            string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new InvalidDataException(
                "Compiled assembly file metadata is incomplete.");
        }

        return new StableAssemblyFileSnapshot(
            assemblyName,
            sourceRevisionId,
            configuration,
            targetFramework,
            moduleVersionId,
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength);
    }

    private static string? ReadAssemblyStringAttribute(
        MetadataReader metadata,
        AssemblyDefinition assemblyDefinition,
        string attributeNamespace,
        string attributeName)
    {
        foreach (var handle in assemblyDefinition.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MemberReference)
                continue;

            var constructor =
                metadata.GetMemberReference(
                    (MemberReferenceHandle)attribute.Constructor);

            if (constructor.Parent.Kind != HandleKind.TypeReference)
                continue;

            var type =
                metadata.GetTypeReference(
                    (TypeReferenceHandle)constructor.Parent);

            if (!string.Equals(
                    metadata.GetString(type.Namespace),
                    attributeNamespace,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    metadata.GetString(type.Name),
                    attributeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var value = metadata.GetBlobReader(attribute.Value);

            if (value.ReadUInt16() != 1)
            {
                throw new InvalidDataException(
                    $"Assembly attribute '{attributeName}' has invalid metadata.");
            }

            return value.ReadSerializedString();
        }

        return null;
    }

    private static string? ParseSourceRevisionId(
        string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;

        var separator = informationalVersion.LastIndexOf('+');
        if (separator < 0 ||
            separator == informationalVersion.Length - 1)
        {
            return null;
        }

        var revision = informationalVersion[(separator + 1)..];

        if (revision.Length is not (40 or 64) ||
            revision.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        return revision.ToLowerInvariant();
    }

    private sealed record StableAssemblyFileSnapshot(
        string AssemblyName,
        string SourceRevisionId,
        string Configuration,
        string TargetFramework,
        string ModuleVersionId,
        string Sha256,
        long ByteLength);

}
