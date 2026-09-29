using System.Reflection;
using EMF.Common;

namespace EMF.ConsoleApplication;

internal static class ReviewerDeploymentVerification
{
    internal const string ManifestEnvironmentVariable = "EMF_EXPECTED_BUILD_MANIFEST";

    public static Func<EmfVerifiedFirstPartyDeploymentIdentity> CreateFromEnvironment() =>
        Create(
            Environment.GetEnvironmentVariable(ManifestEnvironmentVariable),
            AppContext.BaseDirectory,
            typeof(VeteransConsoleCommand).Assembly);

    internal static Func<EmfVerifiedFirstPartyDeploymentIdentity> Create(
        string? expectedManifestPath,
        string deploymentDirectory,
        Assembly root)
    {
        // Deployment tooling creates this expectation from final artifacts before
        // process startup and protects it and the deployment against replacement.
        // Laziness preserves pure M91 reuse without any manifest configuration.
        var expected = new Lazy<EmfBuildManifest>(() =>
        {
            if (string.IsNullOrWhiteSpace(expectedManifestPath) ||
                !Path.IsPathFullyQualified(expectedManifestPath))
            {
                throw new InvalidDataException(
                    $"New reviewer output requires {ManifestEnvironmentVariable} " +
                    "to name an absolute path to a pre-existing expected build manifest.");
            }

            return EmfBuildManifestFile.Load(expectedManifestPath);
        });

        // Recheck deployment files for each generation against the same expectation;
        // never recapture or replace the expectation from observed runtime files.
        return () => EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            expected.Value, deploymentDirectory, root);
    }
}
