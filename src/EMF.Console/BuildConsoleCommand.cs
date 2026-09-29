using EMF.Common;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.ConsoleApplication;

internal static class BuildConsoleCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 2 && args[0] == "verify")
            return VerifyAsync(args[1]);

        if (args.Length == 2 && args[0] == "manifest")
            return WriteManifestAsync(args[1]);

        if (args.Length == 1 && args[0] == "identity")
            return IdentityAsync();

        Console.Error.WriteLine(
            "Usage: emf build identity | emf build manifest <path> | emf build verify <path>");
        return Task.FromResult(2);
    }

    private static Task<int> VerifyAsync(string suppliedPath)
    {
        try
        {
            var path = Path.GetFullPath(suppliedPath);
            var expected = EmfBuildManifestFile.Load(path);

            EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
                expected,
                AppContext.BaseDirectory,
                typeof(BuildConsoleCommand).Assembly);

            Console.WriteLine($"First-party deployment files verified: {expected.BuildId}");
            return Task.FromResult(0);
        }
        catch (Exception ex) when (IsExpectedCommandFailure(ex))
        {
            Console.Error.WriteLine(
                "Build verification failed: manifest could not be loaded or verified.");
            return Task.FromResult(1);
        }
    }

    private static Task<int> WriteManifestAsync(string suppliedPath)
    {
        try
        {
            var path = Path.GetFullPath(suppliedPath);
            var manifest =
                EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                    typeof(BuildConsoleCommand).Assembly);

            EmfBuildManifestFile.Save(path, manifest);

            Console.WriteLine($"Build manifest written: {path}");
            Console.WriteLine("Observed deployment inventory; trust and immutability are established by deployment tooling.");
            Console.WriteLine($"Build ID: {manifest.BuildId}");
            return Task.FromResult(0);
        }
        catch (Exception ex) when (IsExpectedCommandFailure(ex))
        {
            Console.Error.WriteLine(
                "Build manifest failed: manifest could not be created or written.");
            return Task.FromResult(1);
        }
    }

    private static Task<int> IdentityAsync()
    {
        try
        {
            var manifest =
                EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                    typeof(BuildConsoleCommand).Assembly);

            Console.WriteLine($"Source revision : {manifest.SourceRevisionId}");
            Console.WriteLine($"Configuration   : {manifest.Configuration}");
            Console.WriteLine($"Target framework: {manifest.TargetFramework}");
            Console.WriteLine($"Build ID        : {manifest.BuildId}");
            Console.WriteLine("Observed first-party deployment files; not CLR-loaded-byte attestation.");

            foreach (var artifact in manifest.Artifacts)
            {
                Console.WriteLine();
                Console.WriteLine(artifact.AssemblyName);
                Console.WriteLine($"  MVID    : {artifact.ModuleVersionId}");
                Console.WriteLine($"  SHA-256 : {artifact.Sha256}");
                Console.WriteLine($"  Bytes   : {artifact.ByteLength}");
            }

            return Task.FromResult(0);
        }
        catch (Exception ex) when (IsExpectedCommandFailure(ex))
        {
            Console.Error.WriteLine(
                "Build identity failed: runtime build identity could not be captured.");
            return Task.FromResult(1);
        }
    }

    private static bool IsExpectedCommandFailure(Exception ex) =>
        ex is InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            BadImageFormatException or
            ArgumentException;
}
