using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Common;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class EmfAssemblyBuildIdentityTests
{
    [Fact]
    public void SourceRevision_IsReadFromBuiltAssembly()
    {
        var revision = EmfAssemblyBuildIdentity.GetSourceRevisionId(
            typeof(EmfAssemblyBuildIdentity).Assembly);

        Assert.NotNull(revision);
        Assert.True(revision.Length is 40 or 64);
        Assert.All(revision, c => Assert.True(Uri.IsHexDigit(c)));
    }

    [Fact]
    public void AssemblySha256_IsStableAndValid()
    {
        var assembly = typeof(EmfAssemblyBuildIdentity).Assembly;

        var first = EmfAssemblyBuildIdentity.GetSha256(assembly);
        var second = EmfAssemblyBuildIdentity.GetSha256(assembly);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.All(first, c => Assert.True(Uri.IsHexDigit(c)));
    }

    [Fact]
    public void Capture_BindsCompiledArtifactIdentity()
    {
        var assembly = typeof(EmfAssemblyBuildIdentity).Assembly;
        var identity = EmfAssemblyBuildIdentity.Capture(assembly);

        Assert.Equal("EMF.Common", identity.AssemblyName);
        Assert.Equal(
            EmfAssemblyBuildIdentity.GetSourceRevisionId(assembly),
            identity.SourceRevisionId);
        Assert.Equal(
            EmfAssemblyBuildIdentity.GetModuleVersionId(assembly),
            identity.ModuleVersionId);
        Assert.Equal(
            EmfAssemblyBuildIdentity.GetSha256(assembly),
            identity.Sha256);
        Assert.True(identity.ByteLength > 0);
    }

    [Fact]
    public void Capture_FileFieldsMatchSingleByteSnapshot()
    {
        var assembly = typeof(EmfAssemblyBuildIdentity).Assembly;
        var bytes = File.ReadAllBytes(assembly.Location);

        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.PrefetchMetadata);

        var metadata = peReader.GetMetadataReader();
        var module = metadata.GetModuleDefinition();

        var identity = EmfAssemblyBuildIdentity.Capture(assembly);

        Assert.Equal(bytes.LongLength, identity.ByteLength);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)),
            identity.Sha256);
        Assert.Equal(
            "mvid:" + metadata.GetGuid(module.Mvid).ToString("D"),
            identity.ModuleVersionId);
    }

    [Fact]
    public void BuildManifest_IsIndependentOfArtifactOrder()
    {
        var first = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        var second = EmfBuildManifestIdentity.Capture(
            typeof(VeteransReviewerPackageRendererIdentity).Assembly,
            typeof(EmfAssemblyBuildIdentity).Assembly);

        Assert.Equal(first.BuildId, second.BuildId);
        Assert.StartsWith("sha256:", first.BuildId);
        Assert.Equal(2, first.Artifacts.Count);
    }


    [Fact]
    public void FirstPartyClosure_IncludesReviewerDeploymentDependencies()
    {
        var manifest =
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                typeof(VeteransConsoleCommand).Assembly);

        var names = manifest.Artifacts
            .Select(artifact => artifact.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("EMF.Console", names);
        Assert.Contains("EMF.Common", names);
        Assert.Contains("EMF.Core", names);
        Assert.Contains("EMF.Orchestration", names);
        Assert.Contains("EMF.Extensions.VeteransClaims", names);
        Assert.Contains("EMF.Extensions.VeteransClaims.Orchestration", names);
        Assert.Contains("EMF.Extensions.VeteransClaims.Persistence.Sqlite", names);
        Assert.True(manifest.Artifacts.Count > 2);
    }

    [Fact]
    public void FirstPartyClosure_IsStableAcrossRepeatedCapture()
    {
        var first =
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                typeof(VeteransConsoleCommand).Assembly);

        var second =
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                typeof(VeteransConsoleCommand).Assembly);

        Assert.Equal(first.BuildId, second.BuildId);
        Assert.Equal(
            first.Artifacts.Select(artifact => artifact.AssemblyName),
            second.Artifacts.Select(artifact => artifact.AssemblyName));
    }

    [Fact]
    public void FirstPartyClosure_RejectsLegacyTwoAssemblyManifest()
    {
        var legacy = EmfBuildManifestIdentity.Capture(
            typeof(VeteransConsoleCommand).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.VerifyFirstPartyClosure(
                legacy,
                typeof(VeteransConsoleCommand).Assembly));
    }

    [Fact]
    public void BuildManifest_RejectsMixedSourceRevision()
    {
        var first = new EmfBuildArtifactIdentity(
            "A", new string('a', 40), "Debug", ".NETCoreApp,Version=v10.0",
            "mvid:11111111-1111-1111-1111-111111111111",
            new string('1', 64), 100);

        var second = first with
        {
            AssemblyName = "B",
            SourceRevisionId = new string('b', 40)
        };

        Assert.Throws<InvalidDataException>(
            () => EmfBuildManifestIdentity.Create([first, second]));
    }

    [Fact]
    public void BuildManifest_VerifiesRunningArtifacts()
    {
        var manifest = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        EmfBuildManifestIdentity.Verify(
            manifest,
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.Verify(
                manifest with { BuildId = "sha256:" + new string('0', 64) },
                typeof(EmfAssemblyBuildIdentity).Assembly,
                typeof(VeteransReviewerPackageRendererIdentity).Assembly));
    }

    [Fact]
    public void BuildManifestFile_RoundTrips()
    {
        var manifest = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-build-{Guid.NewGuid():N}.json");

        try
        {
            EmfBuildManifestFile.Save(path, manifest);
            var loaded = EmfBuildManifestFile.Load(path);

            Assert.Equal(manifest.BuildId, loaded.BuildId);
            Assert.Equal(manifest.Artifacts.Count, loaded.Artifacts.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildManifest_RejectsDifferentRunningArtifactSet()
    {
        var manifest = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.Verify(
                manifest,
                typeof(EmfAssemblyBuildIdentity).Assembly));
    }

    [Fact]
    public void BuildManifest_UsesStableCanonicalEncoding()
    {
        var artifact = new EmfBuildArtifactIdentity(
            "EMF.Console",
            "0123456789abcdef0123456789abcdef01234567",
            "Release",
            ".NETCoreApp,Version=v10.0",
            "mvid:11111111-2222-3333-4444-555555555555",
            new string('A', 64),
            123456);

        var manifest = EmfBuildManifestIdentity.Create([artifact]);

        Assert.Equal(
            "sha256:5CC6F8F3FF4EA020961262C3557327A06E8A6A6329C868307841A130571D2CA1",
            manifest.BuildId);
    }

    [Fact]
    public void BuildManifest_LengthPrefixPreventsDelimiterCollision()
    {
        var first = new EmfBuildArtifactIdentity(
            "A",
            new string('a', 40),
            "Debug|X",
            "Y",
            "mvid:11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            1);

        var second = first with
        {
            Configuration = "Debug",
            TargetFramework = "X|Y"
        };

        Assert.NotEqual(
            EmfBuildManifestIdentity.Create([first]).BuildId,
            EmfBuildManifestIdentity.Create([second]).BuildId);
    }

    [Fact]
    public void BuildManifest_RejectsDuplicateAssemblyNames()
    {
        var first = new EmfBuildArtifactIdentity(
            "A",
            new string('a', 40),
            "Debug",
            ".NETCoreApp,Version=v10.0",
            "mvid:11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            1);

        var second = first with
        {
            ModuleVersionId =
                "mvid:22222222-2222-2222-2222-222222222222",
            Sha256 = new string('B', 64)
        };

        Assert.Throws<InvalidDataException>(
            () => EmfBuildManifestIdentity.Create([first, second]));
    }

    [Fact]
    public void BuildManifest_RejectsControlCharacters()
    {
        var artifact = new EmfBuildArtifactIdentity(
            "A\nB",
            new string('a', 40),
            "Debug",
            ".NETCoreApp,Version=v10.0",
            "mvid:11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            1);

        Assert.Throws<InvalidDataException>(
            () => EmfBuildManifestIdentity.Create([artifact]));
    }
}
