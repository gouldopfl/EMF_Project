using System.Xml;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class XmlContentInspector :
    IArtifactContentInspector
{
    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "application/xml",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            contentType,
            "text/xml",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        try
        {
            using var stream =
                new MemoryStream(content.ToArray());

            using var reader =
                XmlReader.Create(
                    stream,
                    new XmlReaderSettings
                    {
                        DtdProcessing =
                            DtdProcessing.Prohibit,
                        XmlResolver = null
                    });

            var elementCount = 0;
            var rootName = string.Empty;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                elementCount++;

                if (string.IsNullOrEmpty(rootName))
                    rootName = reader.Name;
            }

            metadata["xmlRootElement"] = rootName;
            metadata["xmlElementCount"] = elementCount;

            findings.Add(
                "Valid XML content was parsed successfully.");
        }
        catch (XmlException)
        {
            findings.Add(
                "Content is not valid XML.");
        }
    }
}
