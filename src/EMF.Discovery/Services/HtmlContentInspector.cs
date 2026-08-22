using System.Text;
using System.Text.RegularExpressions;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class HtmlContentInspector :
    IArtifactContentInspector
{
    private static readonly Regex TitlePattern =
        new(
            @"<title\b[^>]*>(?<title>.*?)</title\s*>",
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline |
            RegexOptions.Compiled);

    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "text/html",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        var text = Encoding.UTF8.GetString(content);

        var hasHtml =
            text.Contains(
                "<html",
                StringComparison.OrdinalIgnoreCase);

        var hasBody =
            text.Contains(
                "<body",
                StringComparison.OrdinalIgnoreCase);

        metadata["htmlHasHtmlElement"] = hasHtml;
        metadata["htmlHasBodyElement"] = hasBody;

        var match = TitlePattern.Match(text);

        if (match.Success)
        {
            metadata["htmlTitle"] =
                System.Net.WebUtility.HtmlDecode(
                    match.Groups["title"].Value.Trim());
        }

        if (hasHtml || hasBody)
        {
            findings.Add(
                "HTML document structure detected.");
        }
        else
        {
            findings.Add(
                "Content does not contain recognizable HTML document structure.");
        }
    }
}
