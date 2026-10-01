using EMF.Common;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Preparation resolves legacy semantic/source records into an immutable document
/// plan. Only this operation may interpret source prose. Print/export subsequently
/// materializes the plan, without sourcing decisions. All components remain shared.
/// See docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md.
/// </summary>
public static class VeteransReviewerPackagePresentationPreparation
{
    public const string Profile = "reviewer-presentation-v1/invariant-English/embedded-fonts/shared-letter-layout";

    public static ReviewerPackagePresentationSnapshot Prepare(ReviewerPackageSnapshot source,
        VeteransReviewerPackageRenderSettings settings, ReviewerPackageCover? resolvedCover = null,
        string? previousPackageId = null)
    {
        using var performanceTiming = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.PresentationPreparation);
        settings.Validate();
        var restored = VeteransReviewerPackageSnapshot.Restore(source);
        var cover = resolvedCover ?? ResolveLegacyCover(restored.Details);
        if (cover.Veteran != restored.Details.VeteranDisplayName ||
            cover.PreparedBy != restored.Details.PackagePreparedBy ||
            cover.ReviewerRole != restored.Details.PackageDetails.Package.ReviewerRole)
            throw new InvalidDataException("Resolved cover identity does not match the frozen package.");
        var plan = VeteransReviewerPackageDocxRenderer.Render(restored.Details,
            restored.Regulations, settings.PackagePreparedDate, cover);
        return ReviewerPackagePresentationSnapshot.Create(source, settings.PackagePreparedDate,
            cover, Profile, VeteransReviewerPackageRendererIdentity.Build, Canonicalize(plan), previousPackageId,
            VeteransReviewerSourceEnlargements.ReadFrozenAudit(plan));
    }

    // Historical V1 has no typed claim scope. Interpret its frozen opinion once at
    // explicit preparation, never during preserved printing or from live data.
    // This adapter preserves the legacy absence of unknown scope; it invents none.
    internal static ReviewerPackageCover ResolveLegacyCover(VeteransReviewerPackageDetails details)
    {
        var scope = Regex.Match(details.MedicalOpinionRequested?.OpinionText ?? string.Empty,
            @"^Determine whether the Veteran's (?<condition>.+?) is at least as likely as not .*?proximately due to or the result of the Veteran's service-connected (?<basis>.+?)\. If causation",
            RegexOptions.CultureInvariant);
        return new(details.VeteranDisplayName,
            scope.Success ? "Secondary service connection" : null,
            scope.Success ? scope.Groups["condition"].Value : null,
            scope.Success ? scope.Groups["basis"].Value : null,
            details.PackagePreparedBy, details.PackageDetails.Package.ReviewerRole);
    }

    // Canonicalize container mechanics only. Do not normalize source text, layout,
    // images or provenance output hashes. The resulting bytes are the frozen plan.
    internal static byte[] Canonicalize(byte[] bytes)
    {
        using var input = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace reference = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var maps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var entry in input.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal) ||
            e.FullName.EndsWith(".rels", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            documents[entry.FullName] = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        foreach (var entry in documents.Where(pair => pair.Key.EndsWith(".rels", StringComparison.Ordinal)))
        {
            var separator = entry.Key.LastIndexOf("_rels/", StringComparison.Ordinal);
            var owner = entry.Key[..separator] + entry.Key[(separator + 6)..^5];
            var relationships = entry.Value.Root!.Elements(rel + "Relationship")
                .OrderBy(e => (string?)e.Attribute("Type"), StringComparer.Ordinal)
                .ThenBy(e => (string?)e.Attribute("Target"), StringComparer.Ordinal)
                .ThenBy(e => (string?)e.Attribute("TargetMode"), StringComparer.Ordinal).ToArray();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < relationships.Length; i++)
            {
                var id = "rId" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                map.Add(relationships[i].Attribute("Id")!.Value, id);
                relationships[i].SetAttributeValue("Id", id);
            }
            entry.Value.Root.ReplaceNodes(relationships);
            maps.Add(owner, map);
        }
        foreach (var entry in documents)
            foreach (var attribute in entry.Value.Descendants().Attributes().Where(a => a.Name.Namespace == reference))
                attribute.Value = maps[entry.Key][attribute.Value];
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in input.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var target = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                target.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = target.Open();
                if (documents.TryGetValue(entry.FullName, out var document)) document.Save(stream, SaveOptions.DisableFormatting);
                else { using var sourceStream = entry.Open(); sourceStream.CopyTo(stream); }
            }
        return output.ToArray();
    }
}
