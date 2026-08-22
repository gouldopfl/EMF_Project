using EMF.Discovery.Contracts;
using EMF.Discovery.Services;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ArtifactInspectionServiceTests
{
    [Fact]
    public async Task InspectAsync_UsesBoundedSample()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-large-{Guid.NewGuid():N}.bin");

        try
        {
            await File.WriteAllBytesAsync(
                path,
                new byte[128 * 1024]);

            var service =
                new ArtifactInspectionService([]);

            var result =
                await service.InspectAsync(path);

            Assert.Equal(
                64 * 1024,
                result.Metadata["inspectionSampleBytes"]);

            Assert.Equal(
                true,
                result.Metadata["inspectionSampleTruncated"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsync_ReportsUnknownFormat()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-unknown-{Guid.NewGuid():N}.bin");

        try
        {
            await File.WriteAllBytesAsync(
                path,
                "unknown artifact content"u8.ToArray());

            var service =
                new ArtifactInspectionService([]);

            var result =
                await service.InspectAsync(path);

            Assert.Null(result.DetectedFormat);
            Assert.Contains(
                result.Findings,
                finding => finding.Contains(
                    "No registered content signature matched",
                    StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsync_DetectsSqliteFromContent()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-inspection-{Guid.NewGuid():N}.bin");

        try
        {
            await using (var connection =
                new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    "CREATE TABLE evidence (id INTEGER PRIMARY KEY);";

                await command.ExecuteNonQueryAsync();
            }

            var service =
                new ArtifactInspectionService(
                    [new SqliteSignatureProvider()]);

            var result =
                await service.InspectAsync(path);

            Assert.Equal(
                "application/x-sqlite3",
                result.DetectedContentType);

            Assert.Equal(
                "SQLite",
                result.DetectedFormat);

            Assert.Equal(
                ".bin",
                result.Metadata["extension"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsync_RoutesJsonToJsonInspector()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-json-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """{"id":1,"name":"evidence"}""");

            var service =
                new ArtifactInspectionService(
                    [],
                    [new JsonContentInspector()]);

            var result =
                await service.InspectAsync(path);

            Assert.Equal(
                "application/json",
                result.DetectedContentType);

            Assert.Equal(
                "Object",
                result.Metadata["jsonRootKind"]);

            Assert.Equal(
                2,
                result.Metadata["jsonPropertyCount"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".csv", "text/csv", "csvColumnCount", 2)]
    [InlineData(".xml", "application/xml", "xmlElementCount", 2)]
    public async Task InspectAsync_RoutesStructuredText(
        string extension,
        string contentType,
        string metadataKey,
        int expectedValue)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-routing-{Guid.NewGuid():N}{extension}");

        try
        {
            var content =
                extension == ".csv"
                    ? "id,name\n1,Veteran"
                    : "<root><id>1</id></root>";

            await File.WriteAllTextAsync(path, content);

            var inspectors =
                new IArtifactContentInspector[]
                {
                    new CsvContentInspector(),
                    new XmlContentInspector()
                };

            var service =
                new ArtifactInspectionService([], inspectors);

            var result =
                await service.InspectAsync(path);

            Assert.Equal(
                contentType,
                result.DetectedContentType);

            Assert.Equal(
                expectedValue,
                result.Metadata[metadataKey]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".html", "text/html", "htmlHasBodyElement")]
    [InlineData(".txt", "text/plain", "textCharacterCount")]
    public async Task InspectAsync_RoutesHtmlAndPlainText(
        string extension,
        string contentType,
        string metadataKey)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-routing-{Guid.NewGuid():N}{extension}");

        try
        {
            var content =
                extension == ".html"
                    ? "<html><body>Evidence</body></html>"
                    : "Plain evidence text.";

            await File.WriteAllTextAsync(path, content);

            var inspectors =
                new IArtifactContentInspector[]
                {
                    new HtmlContentInspector(),
                    new PlainTextContentInspector()
                };

            var service =
                new ArtifactInspectionService([], inspectors);

            var result =
                await service.InspectAsync(path);

            Assert.Equal(
                contentType,
                result.DetectedContentType);

            Assert.True(
                result.Metadata.ContainsKey(metadataKey));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
