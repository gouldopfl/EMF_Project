using System.IO.Compression;
using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class OfficeOpenXmlContainerClassifierTests
{
    [Theory]
    [InlineData("xl/workbook.xml", "XLSX")]
    [InlineData("word/document.xml", "DOCX")]
    [InlineData("ppt/presentation.xml", "PPTX")]
    public void Classify_RecognizesOfficeContainer(
        string entryName,
        string expected)
    {
        var content = CreateZip(entryName);

        var classifier =
            new OfficeOpenXmlContainerClassifier();

        Assert.Equal(
            expected,
            classifier.Classify(content));
    }

    [Fact]
    public void Classify_ReturnsNullForOrdinaryZip()
    {
        var content = CreateZip("data.bin");

        var classifier =
            new OfficeOpenXmlContainerClassifier();

        Assert.Null(
            classifier.Classify(content));
    }

    private static byte[] CreateZip(
        string entryName)
    {
        using var stream = new MemoryStream();

        using (var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            archive.CreateEntry(entryName);
        }

        return stream.ToArray();
    }
}
