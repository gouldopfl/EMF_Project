using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class ArtifactInspectionFactoryTests
{
    [Fact]
    public async Task Create_DetectsOfficePackageBeforeGenericZip()
    {
        var service =
            ArtifactInspectionFactory.Create();

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xlsx");

        var result =
            await service.InspectAsync(path);

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.DetectedContentType);

        Assert.Equal(
            "XLSX",
            result.DetectedFormat);
    }
}
