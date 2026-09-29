using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReviewerDeploymentEnvironmentCollection
{
    public const string Name = "Reviewer deployment environment";
}

internal static class ReviewerDeploymentTestSupport
{
    public static EmfBuildManifest ExpectedManifest() =>
        EmfBuildManifestIdentity.CaptureFirstPartyClosure(typeof(ConsoleCommandRouter).Assembly);

    public static VeteransReviewerPackageDocumentOutputService CreateService(
        IVeteransReviewerPackageDocumentConverter? converter = null,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider = null,
        IEvidencePackageRepository? snapshotRepository = null)
    {
        // Fixture setup establishes the expectation before invoking generation.
        var expected = ExpectedManifest();
        return VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
            () => EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
                expected, AppContext.BaseDirectory, typeof(ConsoleCommandRouter).Assembly),
            converter, regulatoryTextProvider, snapshotRepository);
    }
}

internal sealed class ExpectedReviewerDeployment : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable);
    public string PathValue { get; } = Path.Combine(Path.GetTempPath(), "emf-expected-" + Guid.NewGuid().ToString("N") + ".json");

    public ExpectedReviewerDeployment()
    {
        EmfBuildManifestFile.Save(PathValue, ReviewerDeploymentTestSupport.ExpectedManifest());
        Environment.SetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable, PathValue);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable, _previous);
        File.Delete(PathValue);
    }
}
