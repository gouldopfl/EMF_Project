using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class LibreOfficeConverterSecurityTests
{
    [Fact]
    public void PresentationEnvironmentOverridesCannotEscapeTheSharedProfile()
    {
        var start = new System.Diagnostics.ProcessStartInfo("synthetic-converter");
        foreach (var key in new[] { "SAL_FORCEDPI", "SAL_OVERRIDE_LOCALE", "FONTCONFIG_FILE",
            "QT_SCALE_FACTOR", "GDK_DPI_SCALE", "DISPLAY", "LD_LIBRARY_PATH" })
            start.Environment[key] = "synthetic-ambient-override";
        LibreOfficeVeteransReviewerPackageDocumentConverter.ApplyControlledEnvironment(start);
        Assert.Equal("96", start.Environment["SAL_FORCEDPI"]);
        Assert.Equal("svp", start.Environment["SAL_USE_VCLPLUGIN"]);
        Assert.Equal("C.UTF-8", start.Environment["LC_ALL"]);
        Assert.Equal("en_US", start.Environment["LANGUAGE"]);
        Assert.Equal("UTC", start.Environment["TZ"]);
        foreach (var key in new[] { "SAL_OVERRIDE_LOCALE", "FONTCONFIG_FILE", "QT_SCALE_FACTOR",
            "GDK_DPI_SCALE", "DISPLAY", "LD_LIBRARY_PATH" }) Assert.False(start.Environment.ContainsKey(key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Conversion_UsesPrivateWorkspaceAndDoesNotExposeDiagnostics(int exitCode)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var directory = Directory.CreateTempSubdirectory("emf-converter-test-").FullName;
        var script = Path.Combine(directory, "fake-converter");
        var receipt = Path.Combine(directory, "receipt");
        try
        {
            // Synthetic input only. Fake converter makes no network calls.
            await File.WriteAllTextAsync(script,
                "#!/bin/sh\n" +
                "stat -c %a . > '" + receipt + "'\n" +
                "pwd >> '" + receipt + "'\n" +
                "echo 'synthetic patient diagnosis and synthetic secret' >&2\n" +
                "exit " + exitCode + "\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var converter = new LibreOfficeVeteransReviewerPackageDocumentConverter(script);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => converter.ConvertDocxToPdfAsync(new byte[] { 1, 2, 3 }));
            var lines = await File.ReadAllLinesAsync(receipt);
            Assert.Equal("700", lines[0]);
            Assert.False(Directory.Exists(lines[1]));
            Assert.DoesNotContain("synthetic patient", failure.ToString());
            Assert.DoesNotContain("synthetic secret", failure.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
