using System.Text;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class EmlContentInspector :
    IArtifactContentInspector
{
    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "message/rfc822",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        var text = Encoding.UTF8.GetString(content);

        var headers =
            text.Split(
                ["\r\n\r\n", "\n\n"],
                StringSplitOptions.None)[0];

        var headerNames =
            headers
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith(
                    " ",
                    StringComparison.Ordinal))
                .Select(line =>
                {
                    var separator = line.IndexOf(':');
                    return separator > 0
                        ? line[..separator].Trim()
                        : string.Empty;
                })
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        metadata["emailHeaderCount"] = headerNames.Count;
        metadata["emailHasFrom"] =
            headerNames.Contains("From");
        metadata["emailHasTo"] =
            headerNames.Contains("To");
        metadata["emailHasSubject"] =
            headerNames.Contains("Subject");
        metadata["emailHasDate"] =
            headerNames.Contains("Date");
        metadata["emailHasMimeVersion"] =
            headerNames.Contains("MIME-Version");

        if (headerNames.Contains("From") &&
            headerNames.Contains("To"))
        {
            findings.Add(
                "EML message headers detected.");
        }
        else
        {
            findings.Add(
                "Content does not contain sufficient EML headers.");
        }
    }
}
