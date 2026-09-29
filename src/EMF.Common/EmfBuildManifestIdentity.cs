using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace EMF.Common;

public static class EmfBuildManifestIdentity
{
    private static readonly UTF8Encoding CanonicalUtf8 = new(false, true);

    public static EmfBuildManifest Capture(params Assembly[] assemblies) =>
        Create(assemblies.Select(EmfAssemblyBuildIdentity.Capture));

    public static EmfBuildManifest CaptureFirstPartyClosure(
        params Assembly[] roots) =>
        Capture(ResolveFirstPartyClosure(roots).ToArray());

    /// <summary>
    /// Verifies deployment files against an expectation established before this
    /// process starts. The caller owns the trust and immutability of that expectation
    /// and deployment. Matching loaded metadata is not proof of loaded PE bytes.
    /// </summary>
    public static EmfVerifiedFirstPartyDeploymentIdentity VerifyFirstPartyDeployment(
        EmfBuildManifest expected,
        string deploymentDirectory,
        params Assembly[] roots)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Artifacts is null)
            throw new InvalidDataException("Build manifest requires artifacts.");

        // Freeze before validation so subsequent caller mutations cannot change
        // either the comparison or the identity later used for attribution.
        var frozen = expected with
        {
            Artifacts = Array.AsReadOnly(expected.Artifacts.ToArray())
        };
        Validate(frozen);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentDirectory);
        var directory = Path.GetFullPath(deploymentDirectory);
        var assemblies = ResolveFirstPartyClosure(roots);
        foreach (var assembly in assemblies)
        {
            if (string.IsNullOrWhiteSpace(assembly.Location))
                throw new InvalidDataException("Deployment assemblies require file locations.");

            var relative = Path.GetRelativePath(directory, assembly.Location);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "First-party assembly is outside the configured deployment directory.");
            }
        }

        // File hashes and metadata come from the same per-file byte snapshot.
        // Directory/path immutability is a deployment prerequisite, not something
        // this lexical location check or sequential file reads can establish.
        var actual = Capture(assemblies.ToArray());
        if (!string.Equals(frozen.BuildId, actual.BuildId, StringComparison.Ordinal))
            throw new InvalidDataException(
                "First-party deployment files do not match the expected build manifest.");

        return new EmfVerifiedFirstPartyDeploymentIdentity(frozen, assemblies);
    }

    public static void VerifyFirstPartyClosure(
        EmfBuildManifest expected,
        params Assembly[] roots) =>
        _ = VerifyFirstPartyClosureRuntime(expected, roots);

    public static EmfVerifiedRuntimeIdentity VerifyFirstPartyClosureRuntime(
        EmfBuildManifest expected,
        params Assembly[] roots)
    {
        Validate(expected);

        var actual = CaptureFirstPartyClosure(roots);
        if (!string.Equals(
            expected.BuildId,
            actual.BuildId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Running first-party deployment does not match expected build manifest.");
        }

        return new EmfVerifiedRuntimeIdentity(expected);
    }

    public static EmfBuildManifest Create(
        IEnumerable<EmfBuildArtifactIdentity> identities)
    {
        if (identities is null)
            throw new InvalidDataException("Build manifest requires artifacts.");

        var artifacts = identities.ToArray();

        if (artifacts.Length == 0)
            throw new InvalidDataException("Build manifest requires artifacts.");

        foreach (var artifact in artifacts)
            ValidateArtifact(artifact);

        Array.Sort(artifacts, (left, right) =>
            StringComparer.Ordinal.Compare(left.AssemblyName, right.AssemblyName));

        if (artifacts
            .GroupBy(x => x.AssemblyName, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException(
                "Build manifest contains duplicate assembly names.");
        }

        var source = artifacts[0].SourceRevisionId;
        var configuration = artifacts[0].Configuration;
        var framework = artifacts[0].TargetFramework;

        if (artifacts.Any(x =>
            !string.Equals(
                x.SourceRevisionId,
                source,
                StringComparison.Ordinal) ||
            !string.Equals(
                x.Configuration,
                configuration,
                StringComparison.Ordinal) ||
            !string.Equals(
                x.TargetFramework,
                framework,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Build artifacts have mixed build identity.");
        }

        var canonical = new StringBuilder("emf-build-manifest-v1\n");
        Append(canonical, source);
        Append(canonical, configuration);
        Append(canonical, framework);
        Append(
            canonical,
            artifacts.Length.ToString(CultureInfo.InvariantCulture));

        foreach (var artifact in artifacts)
        {
            Append(canonical, artifact.AssemblyName);
            Append(canonical, artifact.SourceRevisionId);
            Append(canonical, artifact.Configuration);
            Append(canonical, artifact.TargetFramework);
            Append(canonical, artifact.ModuleVersionId);
            Append(canonical, artifact.Sha256);
            Append(
                canonical,
                artifact.ByteLength.ToString(CultureInfo.InvariantCulture));
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(
                CanonicalUtf8.GetBytes(canonical.ToString())));

        return new EmfBuildManifest(
            source,
            configuration,
            framework,
            artifacts,
            "sha256:" + hash);
    }

    public static void Verify(
        EmfBuildManifest expected,
        params Assembly[] assemblies) =>
        _ = VerifyRuntime(expected, assemblies);

    public static EmfVerifiedRuntimeIdentity VerifyRuntime(
        EmfBuildManifest expected,
        params Assembly[] assemblies)
    {
        Validate(expected);

        var actual = Capture(assemblies);
        if (!string.Equals(
            expected.BuildId,
            actual.BuildId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Running build does not match expected build manifest.");
        }

        return new EmfVerifiedRuntimeIdentity(expected);
    }

    public static void Validate(EmfBuildManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var canonical = Create(manifest.Artifacts);
        if (!string.Equals(
                manifest.BuildId,
                canonical.BuildId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.SourceRevisionId,
                canonical.SourceRevisionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Configuration,
                canonical.Configuration,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.TargetFramework,
                canonical.TargetFramework,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Build manifest failed integrity validation.");
        }
    }

    private static IReadOnlyList<Assembly> ResolveFirstPartyClosure(
        IReadOnlyCollection<Assembly> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        if (roots.Count == 0)
            throw new InvalidDataException(
                "First-party deployment inventory requires root assemblies.");

        var resolved = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(roots);

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            ArgumentNullException.ThrowIfNull(assembly);

            var assemblyName = assembly.GetName();
            var name = assemblyName.Name;

            if (!IsFirstPartyAssemblyName(name))
            {
                throw new InvalidDataException(
                    "Deployment inventory roots must be first-party EMF assemblies.");
            }

            if (resolved.TryGetValue(name!, out var existing))
            {
                if (!ReferenceEquals(existing, assembly))
                {
                    throw new InvalidDataException(
                        $"Conflicting first-party assembly identity '{name}'.");
                }

                continue;
            }

            resolved.Add(name!, assembly);

            foreach (var reference in assembly
                .GetReferencedAssemblies()
                .Where(reference =>
                    IsFirstPartyAssemblyName(reference.Name))
                .OrderBy(
                    reference => reference.Name,
                    StringComparer.Ordinal))
            {
                pending.Enqueue(ResolveAssembly(assembly, reference));
            }
        }

        return resolved.Values
            .OrderBy(
                assembly => assembly.GetName().Name,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static Assembly ResolveAssembly(Assembly referencingAssembly, AssemblyName reference)
    {
        try
        {
            var context = AssemblyLoadContext.GetLoadContext(referencingAssembly)
                ?? throw new InvalidDataException("First-party assembly load context is unavailable.");
            // Ask the referencing context, never choose a process-wide candidate
            // by load order. Conflicting instances across roots fail in the closure.
            return context.LoadFromAssemblyName(reference);
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or
                FileLoadException or
                BadImageFormatException)
        {
            throw new InvalidDataException(
                $"First-party deployment assembly '{reference.Name}' could not be loaded.",
                ex);
        }
    }

    private static bool IsFirstPartyAssemblyName(string? name) =>
        name is not null &&
        (string.Equals(name, "EMF", StringComparison.Ordinal) ||
         name.StartsWith("EMF.", StringComparison.Ordinal));

    private static void ValidateArtifact(EmfBuildArtifactIdentity artifact)
    {
        if (artifact is null)
            throw new InvalidDataException("Build manifest contains a null artifact.");

        if (!IsSafeText(artifact.AssemblyName) ||
            !IsLowerHexRevision(artifact.SourceRevisionId) ||
            !IsSafeText(artifact.Configuration) ||
            !IsSafeText(artifact.TargetFramework) ||
            !IsModuleVersionId(artifact.ModuleVersionId) ||
            !IsUpperHexSha256(artifact.Sha256) ||
            artifact.ByteLength <= 0)
        {
            throw new InvalidDataException(
                "Build artifact identity contains invalid fields.");
        }
    }

    private static void Append(
        StringBuilder builder,
        string value)
    {
        var byteLength = CanonicalUtf8.GetByteCount(value);

        builder
            .Append(
                byteLength.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }

    private static bool IsSafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            return false;
        try
        {
            _ = CanonicalUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsLowerHexRevision(string? value) =>
        value is { Length: 40 or 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsModuleVersionId(string? value) =>
        value is not null &&
        value.StartsWith("mvid:", StringComparison.Ordinal) &&
        Guid.TryParseExact(value["mvid:".Length..], "D", out _);

    private static bool IsUpperHexSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
