using EMF.Discovery.Contracts;
using EMF.Discovery.Services;

namespace EMF.ConsoleApplication;

internal static class ArtifactInspectionFactory
{
    public static IArtifactInspectionService Create()
    {
        return new ArtifactInspectionService(
            new IArtifactSignatureProvider[]
            {
                new OfficePackageSignatureProvider(),
                new PdfSignatureProvider(),
                new SqliteSignatureProvider(),
                new ZipSignatureProvider()
            },
            new IArtifactContentInspector[]
            {
                new CsvContentInspector(),
                new EmlContentInspector(),
                new HtmlContentInspector(),
                new JsonContentInspector(),
                new PlainTextContentInspector(),
                new XmlContentInspector()
            });
    }
}
