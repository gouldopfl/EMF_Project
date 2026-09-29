using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class EmfFirstPartyDeploymentIdentityTests
{
    private static Assembly Root => typeof(ConsoleCommandRouter).Assembly;

    [Fact]
    public void CompleteClosure_VerifiesAndBindsActualRenderer()
    {
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(Root);
        var verified = EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            expected, AppContext.BaseDirectory, Root);
        Assert.Equal(expected.BuildId, verified.BuildId);
        Assert.Equal(expected.SourceRevisionId, verified.SourceRevisionId);
        VeteransReviewerPackageRendererIdentity.ValidateVerifiedDeployment(verified);
        Assert.Empty(typeof(EmfVerifiedFirstPartyDeploymentIdentity).GetConstructors());
        Assert.False(typeof(EmfVerifiedRuntimeIdentity).IsAssignableTo(
            typeof(EmfVerifiedFirstPartyDeploymentIdentity)));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("unrelated")]
    [InlineData("stale")]
    [InlineData("extra")]
    public void UnexpectedInventory_FailsClosed(string change)
    {
        var renderer = typeof(VeteransReviewerPackageDocxRenderer).Assembly;
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(Root);
        expected = change switch
        {
            "incomplete" => EmfBuildManifestIdentity.Capture(renderer),
            "unrelated" => EmfBuildManifestIdentity.Capture(typeof(EmfBuildManifest).Assembly),
            "stale" => EmfBuildManifestIdentity.Create(expected.Artifacts.Select(
                a => a with { Sha256 = new string('A', 64) })),
            "extra" => EmfBuildManifestIdentity.Create(expected.Artifacts.Append(
                expected.Artifacts[0] with { AssemblyName = "EMF.Unrelated" })),
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.VerifyFirstPartyDeployment(expected, AppContext.BaseDirectory, Root));
    }

    [Fact]
    public void VerifiedInventory_IsDefensivelyFrozen()
    {
        var captured = EmfBuildManifestIdentity.CaptureFirstPartyClosure(Root);
        var callerArtifacts = captured.Artifacts.ToArray();
        var expected = captured with { Artifacts = callerArtifacts };
        var verified = EmfBuildManifestIdentity.VerifyFirstPartyDeployment(expected, AppContext.BaseDirectory, Root);
        var original = callerArtifacts[0];
        callerArtifacts[0] = original with { Sha256 = new string('A', 64) };
        Assert.Equal(original, verified.Manifest.Artifacts[0]);
        Assert.Equal(captured.BuildId, verified.BuildId);
        EmfBuildManifestIdentity.Validate(verified.Manifest);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EmfBuildArtifactIdentity>)verified.Manifest.Artifacts)[0] = callerArtifacts[0]);
    }

    [Fact]
    public void FilesOutsideConfiguredDeployment_AreRejected()
    {
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(Root);
        Assert.Throws<InvalidDataException>(() => EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            expected, Path.Combine(AppContext.BaseDirectory, "different-deployment"), Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinctInstancesOfSameIdentity_AreRejectedInBothRootOrders(bool changeMvid)
    {
        using var copy = new CommonCopy(changeMvid: changeMvid);
        var original = typeof(EmfBuildManifest).Assembly;
        Assert.Equal(original.FullName, copy.Assembly.FullName);
        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(original, copy.Assembly));
        Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(copy.Assembly, original));
        Assert.Equal(
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(original).BuildId,
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(original, original).BuildId);
    }

    [Fact]
    public void SameMvidReplacement_FailsAgainstPreExistingExpectedManifest()
    {
        using var copy = new CommonCopy();
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(copy.Assembly);
        var before = copy.InvokeCaptureError();
        copy.ReplaceFile(CommonCopy.ChangeMessage(copy.OriginalBytes));
        Assert.Equal(before, copy.InvokeCaptureError()); // CLR still uses the original content.
        var error = Assert.Throws<InvalidDataException>(() =>
            EmfBuildManifestIdentity.VerifyFirstPartyDeployment(expected, copy.DirectoryPath, copy.Assembly));
        Assert.Contains("do not match", error.Message);
    }

    [Fact]
    public void RestoredExpectedFile_IsNotProofOfOriginallyLoadedBytes()
    {
        // Deliberately violates immutable deployment. Documents the limit of this
        // API, so a future test cannot mistake file verification for PE attestation.
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(typeof(EmfBuildManifest).Assembly);
        using var copy = new CommonCopy(changeMessage: true);
        var loadedMessage = copy.InvokeCaptureError();
        Assert.StartsWith("Tampered", loadedMessage);
        copy.ReplaceFile(copy.OriginalBytes);
        var verified = EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            expected, copy.DirectoryPath, copy.Assembly);
        Assert.Equal(expected.BuildId, verified.BuildId);
        Assert.Equal(loadedMessage, copy.InvokeCaptureError());
    }

    [Fact]
    public void RendererFromAnotherLoadContext_CannotAuthorizeActualRenderer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "emf-shadow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var context = new DeploymentContext(directory);
        try
        {
            foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "EMF.*.dll"))
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
            var renderer = typeof(VeteransReviewerPackageDocxRenderer).Assembly;
            var shadow = context.LoadFromAssemblyPath(Path.Combine(directory, Path.GetFileName(renderer.Location)));
            Assert.Equal(renderer.ManifestModule.ModuleVersionId, shadow.ManifestModule.ModuleVersionId);
            var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(shadow);
            var verified = EmfBuildManifestIdentity.VerifyFirstPartyDeployment(expected, directory, shadow);
            Assert.Throws<InvalidDataException>(() =>
                VeteransReviewerPackageRendererIdentity.ValidateVerifiedDeployment(verified));

            // Context-local dependencies conflict with the default-context roots;
            // resolving by process-wide load order would silently hide this.
            Assert.Throws<InvalidDataException>(() =>
                EmfBuildManifestIdentity.CaptureFirstPartyClosure(typeof(EmfBuildManifest).Assembly, shadow));
            Assert.Throws<InvalidDataException>(() =>
                EmfBuildManifestIdentity.CaptureFirstPartyClosure(shadow, typeof(EmfBuildManifest).Assembly));
        }
        finally { context.Unload(); Directory.Delete(directory, recursive: true); }
    }

    public static IEnumerable<object[]> InvalidUtf16Fields()
    {
        foreach (var field in new[] { "AssemblyName", "Configuration", "TargetFramework" })
        foreach (var codeUnit in new[] { 0xd800, 0xdc00 })
        foreach (var embedded in new[] { false, true })
            yield return [field, codeUnit, embedded];
    }

    [Theory]
    [MemberData(nameof(InvalidUtf16Fields))]
    public void LoneSurrogates_AreRejectedBeforeEncoding(string field, int codeUnit, bool embedded)
    {
        // Keep malformed UTF-16 out of xUnit's serialized test IDs.
        var invalid = embedded ? "A" + (char)codeUnit + "B" : new string((char)codeUnit, 1);
        var artifact = EmfAssemblyBuildIdentity.Capture(typeof(EmfBuildManifest).Assembly);
        artifact = field switch
        {
            "AssemblyName" => artifact with { AssemblyName = invalid },
            "Configuration" => artifact with { Configuration = invalid },
            _ => artifact with { TargetFramework = invalid }
        };
        Assert.Throws<InvalidDataException>(() => EmfBuildManifestIdentity.Create([artifact]));
    }

    [Fact]
    public void ValidUnicode_RetainsExactStringSemantics()
    {
        var artifact = EmfAssemblyBuildIdentity.Capture(typeof(EmfBuildManifest).Assembly);
        var astral = EmfBuildManifestIdentity.Create([artifact with { AssemblyName = "EMF.\U0001F680" }]);
        EmfBuildManifestIdentity.Validate(astral);
        var composed = EmfBuildManifestIdentity.Create([artifact with { AssemblyName = "EMF.\u00e9" }]);
        var decomposed = EmfBuildManifestIdentity.Create([artifact with { AssemblyName = "EMF.e\u0301" }]);
        Assert.NotEqual(composed.BuildId, decomposed.BuildId);
    }

    private sealed class DeploymentContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = Path.Combine(directory, assemblyName.Name + ".dll");
            return assemblyName.Name?.StartsWith("EMF.", StringComparison.Ordinal) == true && File.Exists(path)
                ? LoadFromAssemblyPath(path) : null;
        }
    }

    private sealed class CommonCopy : IDisposable
    {
        private readonly AssemblyLoadContext _context = new("deployment-test", isCollectible: true);
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "emf-common-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(DirectoryPath, "EMF.Common.dll");
        public byte[] OriginalBytes { get; }
        public Assembly Assembly { get; }

        public CommonCopy(bool changeMessage = false, bool changeMvid = false)
        {
            var original = typeof(EmfBuildManifest).Assembly;
            OriginalBytes = File.ReadAllBytes(original.Location);
            var bytes = changeMessage ? ChangeMessage(OriginalBytes) : (byte[])OriginalBytes.Clone();
            if (changeMvid)
            {
                var offset = bytes.AsSpan().IndexOf(original.ManifestModule.ModuleVersionId.ToByteArray());
                Assert.True(offset >= 0);
                Guid.NewGuid().ToByteArray().CopyTo(bytes, offset);
            }
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllBytes(FilePath, bytes);
            Assembly = _context.LoadFromAssemblyPath(FilePath);
        }

        public static byte[] ChangeMessage(byte[] original)
        {
            var bytes = (byte[])original.Clone();
            var needle = Encoding.Unicode.GetBytes("Compiled assembly file metadata is incomplete.");
            var offset = bytes.AsSpan().IndexOf(needle);
            Assert.True(offset >= 0);
            Encoding.Unicode.GetBytes("Tampered assembly file metadata is incomplete.").CopyTo(bytes, offset);
            return bytes;
        }

        public string InvokeCaptureError()
        {
            var capture = Assembly.GetType("EMF.Common.EmfAssemblyBuildIdentity")!.GetMethod("Capture")!;
            var error = Assert.Throws<TargetInvocationException>(() => capture.Invoke(null, [typeof(string).Assembly]));
            return error.InnerException!.Message;
        }

        public void ReplaceFile(byte[] bytes)
        {
            File.WriteAllBytes(FilePath + ".replacement", bytes);
            File.Move(FilePath + ".replacement", FilePath, overwrite: true);
        }

        public void Dispose()
        {
            _context.Unload();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
