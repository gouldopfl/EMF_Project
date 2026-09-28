using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerDocumentPrivacy
{
    private static readonly Regex IdentifierLine = new(
        @"^\s*(?:MRN|SSN)\s*:\s*\*{3}-\*{2}-(?<last>\d{4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    // Final presentation sweep also catches identifiers split across runs or a
    // label/value line break. Retain surrounding formatting and source objects.
    public static void Mask(OpenXmlElement root)
    {
        var text = new StringBuilder();
        var runs = new List<(Text Node, int Start, int Length)>();
        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            foreach (var node in paragraph.Descendants<Text>())
            {
                runs.Add((node, text.Length, node.Text.Length));
                text.Append(node.Text);
            }
            text.Append('\n');
        }
        foreach (var span in VeteransReviewerPackagePrivacySanitizer.Redactions(text.ToString()).Reverse())
        {
            var first = true;
            foreach (var run in runs.Where(r => r.Start < span.Start + span.Length && r.Start + r.Length > span.Start))
            {
                var begin = Math.Max(0, span.Start - run.Start);
                var length = Math.Min(run.Start + run.Length, span.Start + span.Length) - run.Start - begin;
                run.Node.Text = run.Node.Text.Remove(begin, length).Insert(begin, first ? span.Replacement : "");
                first = false;
            }
        }
        // Only standalone identifier lines are relabelled. Adjacent MRN/SSN
        // lines carrying the same last four are one intentional presentation
        // redaction, not missing source content. Never modify source objects.
        Paragraph? previousIdentifier = null;
        string? previousLastFour = null;
        foreach (var paragraph in root.Descendants<Paragraph>().ToArray())
        {
            var match = IdentifierLine.Match(paragraph.InnerText);
            if (!match.Success)
            {
                previousIdentifier = null;
                previousLastFour = null;
                continue;
            }
            var lastFour = match.Groups["last"].Value;
            var nodes = paragraph.Descendants<Text>().ToArray();
            var duplicate = previousIdentifier?.NextSibling() == paragraph && previousLastFour == lastFour;
            for (var i = 0; i < nodes.Length; i++)
                nodes[i].Text = !duplicate && i == 0 ? "Patient identifier: " + lastFour : "";
            if (duplicate && paragraph.ParagraphProperties?.GetFirstChild<SectionProperties>() is null)
                paragraph.Remove();
            else
                previousIdentifier = paragraph;
            previousLastFour = lastFour;
        }
        foreach (var element in root.Descendants())
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.Value is null || attribute.LocalName is not ("descr" or "title" or "name")) continue;
            var masked = VeteransReviewerPackagePrivacySanitizer.Redact(attribute.Value);
            if (masked != attribute.Value)
                element.SetAttribute(new OpenXmlAttribute(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, masked));
        }
    }
}
