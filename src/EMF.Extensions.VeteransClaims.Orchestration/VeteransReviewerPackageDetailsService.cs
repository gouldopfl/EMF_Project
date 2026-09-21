using System.Text;
using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetailsService
{
    private readonly IEvidencePackageService _packages;
    private readonly IEvidenceRepository _evidence;
    private readonly IEvidenceClassificationRepository? _classifications;
    private readonly IMedicalLiteratureRepository? _medicalLiterature;
    private readonly IClaimIssueAdjudicationDetailsService? _adjudicationDetails;
    private readonly IArtifactTextExtractor? _textExtractor;
    private readonly IArtifactPrintRenderer? _printRenderer;

    private const string PdfMedicalLiteratureReviewerTextExtractionMethod =
        "artifact-text-extractor-pdf-normalized-v8";

    private const string DefaultMedicalLiteratureReviewerTextExtractionMethod =
        "artifact-text-extractor-v1";

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);

        _packages = packages;
        _evidence = evidence;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(packages, evidence)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        _classifications = classifications;
        _medicalLiterature = medicalLiterature;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IMedicalLiteratureRepository medicalLiterature,
        IClaimIssueAdjudicationDetailsService adjudicationDetails)
        : this(
            packages,
            evidence,
            classifications,
            medicalLiterature)
    {
        ArgumentNullException.ThrowIfNull(adjudicationDetails);
        _adjudicationDetails = adjudicationDetails;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IArtifactTextExtractor textExtractor)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(textExtractor);

        _packages = packages;
        _evidence = evidence;
        _textExtractor = textExtractor;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(packages, evidence, textExtractor)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        _classifications = classifications;
        _medicalLiterature = medicalLiterature;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IMedicalLiteratureRepository medicalLiterature,
        IClaimIssueAdjudicationDetailsService adjudicationDetails)
        : this(
            packages,
            evidence,
            classifications,
            textExtractor,
            medicalLiterature)
    {
        ArgumentNullException.ThrowIfNull(adjudicationDetails);
        _adjudicationDetails = adjudicationDetails;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IArtifactPrintRenderer printRenderer,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(
            packages,
            evidence,
            classifications,
            textExtractor)
    {
        ArgumentNullException.ThrowIfNull(printRenderer);
        _printRenderer = printRenderer;
        _medicalLiterature = medicalLiterature;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IArtifactPrintRenderer printRenderer,
        IMedicalLiteratureRepository medicalLiterature,
        IClaimIssueAdjudicationDetailsService adjudicationDetails)
        : this(
            packages,
            evidence,
            classifications,
            textExtractor,
            printRenderer,
            medicalLiterature)
    {
        ArgumentNullException.ThrowIfNull(adjudicationDetails);
        _adjudicationDetails = adjudicationDetails;
    }

    public async Task<VeteransReviewerPackageDetails?> GetAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default)
    {
        var details =
            await _packages.GetAsync(
                packageId,
                cancellationToken);

        if (details is null)
            return null;

        var artifacts = new List<Artifact>();
        var artifactContents =
            new List<VeteransReviewerArtifactContent>();

        var packageArtifacts = details.Artifacts.ToList();

        var literatureScope =
            await GetReviewerLiteratureScopeAsync(
                details.Package,
                cancellationToken);

        if (literatureScope is not null)
        {
            await AddReviewedMedicalLiteratureArtifactsAsync(
                details.Package,
                packageArtifacts,
                literatureScope.Value.Details,
                literatureScope.Value.BasisIds,
                cancellationToken);
        }

        foreach (var packageArtifact in packageArtifacts)
        {
            var artifact =
                await _evidence.GetArtifactAsync(
                    packageArtifact.ArtifactId,
                    cancellationToken);

            if (artifact is null)
            {
                throw new InvalidOperationException(
                    $"Evidence package '{packageId.Value}' references " +
                    $"missing artifact '{packageArtifact.ArtifactId.Value}'.");
            }

            if (artifact.Id != packageArtifact.ArtifactId)
                throw new InvalidOperationException(
                    "Evidence package artifact identity mismatch.");

            artifacts.Add(artifact);

            var isOscar =
                artifact.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                artifact.Name.Contains("OSCAR", StringComparison.OrdinalIgnoreCase);

            var isSnore =
                artifact.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                artifact.Name.Contains("SNORE", StringComparison.OrdinalIgnoreCase);

            var isPapExport =
                isOscar || isSnore;

            var appendix =
                await GetAppendixAsync(
                    artifact.Id,
                    cancellationToken);

            var text =
                GetTextSummary(artifact);

            var isMedicalLiterature =
                string.Equals(
                    appendix,
                    VeteransReviewerPackageAppendix.MedicalLiterature,
                    StringComparison.Ordinal);

            if ((isPapExport || isMedicalLiterature || string.IsNullOrWhiteSpace(text)) &&
                _textExtractor is not null)
            {
                text =
                    await _textExtractor.ExtractTextAsync(
                        artifact.Id,
                        cancellationToken);
            }

            if (isPapExport)
            {
                var papSourceName =
                    isSnore ? "SNORE" : "OSCAR";

                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(
                        $"{papSourceName} evidence has no extractable session data.");

                var bytes =
                    Encoding.UTF8.GetBytes(text);

                text =
                    isSnore
                        ? PapTherapyReviewerFormatter.FormatSnore(bytes)
                        : PapTherapyReviewerFormatter.Format(bytes);
            }

            IReadOnlyList<PrintableArtifactPage> printablePages = [];

            if (string.Equals(
                    packageArtifact.ContentRole,
                    Models.Adjudication.EvidencePackageContentRoles
                        .UnderlyingEvidence,
                    StringComparison.Ordinal) &&
                _printRenderer is not null)
            {
                printablePages =
                    isPapExport
                        ? [new PrintableArtifactPage
                        {
                            PageNumber = 1,
                            ContentType = "text/plain",
                            Content = Encoding.UTF8.GetBytes(text!)
                        }]
                        : await _printRenderer.RenderAsync(
                            artifact.Id,
                            cancellationToken);

                if (printablePages.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Underlying evidence artifact '{artifact.Id.Value}' " +
                        "has no printable source representation.");
                }
            }

            if (packageArtifact.ReviewerPageSelection is not null)
            {
                if (!string.Equals(
                        packageArtifact.ContentRole,
                        EvidencePackageContentRoles.UnderlyingEvidence,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Reviewer page selection is only valid for " +
                        "underlying evidence.");
                }

                printablePages =
                    VeteransReviewerPageSelector.Select(
                        printablePages,
                        packageArtifact.ReviewerPageSelection);
            }

            var provenance =
                await _evidence.GetProvenanceAsync(
                    artifact.Id,
                    cancellationToken);

            if (provenance.Any(
                    item => item.ArtifactId != artifact.Id))
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifact.Id.Value}' provenance lookup " +
                    "returned a different artifact.");
            }

            var relationships =
                await _evidence.GetRelationshipsAsync(
                    artifact.Id,
                    cancellationToken);

            if (relationships.Any(
                    relationship =>
                        relationship.SourceArtifactId != artifact.Id &&
                        relationship.TargetArtifactId != artifact.Id))
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifact.Id.Value}' relationship lookup " +
                    "returned an unrelated artifact relationship.");
            }

            var sourceName =
                await GetReviewerSourceNameAsync(
                    artifact.Id,
                    relationships,
                    cancellationToken);

            var reviewerArtifact =
                await GetReviewerArtifactAsync(
                    artifact,
                    appendix,
                    cancellationToken);

            var reviewedMedicalLiteratureClassifications =
                await GetReviewedMedicalLiteratureClassificationsAsync(
                    artifact.Id,
                    appendix,
                    details.Package.ServiceConnectionBasisId,
                    literatureScope?.BasisIds,
                    cancellationToken);

            if (appendix == VeteransReviewerPackageAppendix.MedicalLiterature &&
                reviewedMedicalLiteratureClassifications.Count == 0)
            {
                artifacts.Remove(artifact);
                continue;
            }

            var medicalLiteratureReviewerText =
                await GetOrCreateMedicalLiteratureReviewerTextAsync(
                    artifact,
                    appendix,
                    text,
                    cancellationToken);

            artifactContents.Add(
                new VeteransReviewerArtifactContent
                {
                    Artifact = reviewerArtifact,
                    Text = text ?? string.Empty,
                    MedicalLiteratureReviewerText =
                        medicalLiteratureReviewerText,
                    PrintablePages = printablePages,
                    ReviewerPageSelection =
                        packageArtifact.ReviewerPageSelection,
                    Provenance = provenance,
                    Relationships = relationships,
                    Appendix = appendix,
                    SourceName = sourceName,
                    ReviewedMedicalLiteratureClassifications =
                        reviewedMedicalLiteratureClassifications
                });
        }

        var reviewerPackageDetails =
            packageArtifacts.Count == details.Artifacts.Count
                ? details
                : new EvidencePackageDetails
                {
                    Package = details.Package,
                    Artifacts = packageArtifacts
                };

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = reviewerPackageDetails,
            Artifacts = artifacts,
            ArtifactContents = artifactContents
        };
    }

    private async Task<(
        ClaimIssueAdjudicationDetails Details,
        IReadOnlySet<ServiceConnectionBasisId> BasisIds)?>
        GetReviewerLiteratureScopeAsync(
            EvidencePackage package,
            CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            _adjudicationDetails is null ||
            package.ServiceConnectionBasisId is null)
        {
            return null;
        }

        var adjudicationDetails =
            await _adjudicationDetails.GetAsync(
                package.ClaimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Reviewer claim issue not found: {package.ClaimIssueId.Value}");

        if (adjudicationDetails.ClaimIssue.Id != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer adjudication details claim issue mismatch.");
        }

        var selectedBasis =
            adjudicationDetails.ServiceConnectionBases.SingleOrDefault(
                basis => basis.Id == package.ServiceConnectionBasisId.Value)
            ?? throw new InvalidOperationException(
                $"Reviewer literature basis not found: " +
                $"{package.ServiceConnectionBasisId.Value.Value}");

        if (selectedBasis.ClaimIssueId != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer literature basis belongs to another claim issue.");
        }

        var prescribedMedicationBasisIds =
            adjudicationDetails.PrescribedMedications
                .Select(item => item.Basis.Id)
                .ToHashSet();

        var basisIds =
            adjudicationDetails.ServiceConnectionBases
                .Where(
                    basis =>
                        basis.ClaimIssueId == package.ClaimIssueId &&
                        basis.ServiceConnectionTheoryId ==
                            selectedBasis.ServiceConnectionTheoryId &&
                        (basis.Id == selectedBasis.Id ||
                         prescribedMedicationBasisIds.Contains(basis.Id)))
                .Select(basis => basis.Id)
                .ToHashSet();

        return (adjudicationDetails, basisIds);
    }

    private async Task AddReviewedMedicalLiteratureArtifactsAsync(
        EvidencePackage package,
        List<EvidencePackageArtifact> packageArtifacts,
        ClaimIssueAdjudicationDetails adjudicationDetails,
        IReadOnlySet<ServiceConnectionBasisId> literatureBasisIds,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null)
            return;

        var existingArtifactIds =
            packageArtifacts
                .Select(item => item.ArtifactId)
                .ToHashSet();

        foreach (var requirement in
                 adjudicationDetails.Requirements.Where(
                     item =>
                         literatureBasisIds.Contains(item.Basis.Id)))
        {
            IReadOnlyList<ReviewedMedicalLiteratureClassification> reviewed;

            try
            {
                reviewed =
                    await _medicalLiterature.GetReviewedClassificationsAsync(
                        requirement.Basis.Id,
                        requirement.Requirement.Id,
                        cancellationToken);
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (reviewed.Any(
                    item =>
                        item.Association.ServiceConnectionBasisId !=
                            requirement.Basis.Id ||
                        item.Association.RequirementId !=
                            requirement.Requirement.Id))
            {
                throw new InvalidOperationException(
                    "Reviewer medical literature reviewed classification " +
                    "requirement mismatch.");
            }

            foreach (var artifactId in
                     reviewed
                         .Select(item => item.ArtifactId)
                         .Distinct())
            {
                if (!existingArtifactIds.Add(artifactId))
                    continue;

                packageArtifacts.Add(
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = package.Id,
                        ArtifactId = artifactId,
                        ContentRole =
                            EvidencePackageContentRoles.UnderlyingEvidence
                    });
            }
        }
    }

    private async Task<string?> GetOrCreateMedicalLiteratureReviewerTextAsync(
        Artifact artifact,
        string? appendix,
        string? extractedText,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            appendix != VeteransReviewerPackageAppendix.MedicalLiterature)
            return null;

        var sourceIds =
            await _medicalLiterature.GetMedicalLiteratureSourceIdsAsync(
                artifact.Id,
                cancellationToken);

        if (sourceIds.Count != 1)
            return null;

        var sourceId = sourceIds[0];

        MedicalLiteratureReviewerText? stored = null;

        try
        {
            stored =
                await _medicalLiterature.GetReviewerTextAsync(
                    sourceId,
                    artifact.Id,
                    cancellationToken);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        var isPdf = IsPdfArtifact(artifact);

        if (stored is not null &&
            (!isPdf ||
             string.Equals(
                 stored.ExtractionMethod,
                 PdfMedicalLiteratureReviewerTextExtractionMethod,
                 StringComparison.Ordinal)))
        {
            return stored.Text;
        }

        if (string.IsNullOrWhiteSpace(extractedText))
            return stored?.Text;

        var source =
            await _medicalLiterature.GetMedicalLiteratureSourceAsync(
                sourceId,
                cancellationToken);

        var reviewerText = new MedicalLiteratureReviewerText
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifact.Id,
            Text = isPdf
                ? NormalizePdfMedicalLiteratureReviewerText(extractedText)
                : extractedText.Trim(),
            SourceHash = source?.SourceHash,
            ExtractionMethod = isPdf
                ? PdfMedicalLiteratureReviewerTextExtractionMethod
                : DefaultMedicalLiteratureReviewerTextExtractionMethod,
            ExtractedUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await _medicalLiterature.UpsertReviewerTextAsync(
                reviewerText,
                cancellationToken);
        }
        catch (NotSupportedException)
        {
            return reviewerText.Text;
        }

        return reviewerText.Text;
    }


    private static bool IsPdfArtifact(Artifact artifact)
    {
        if (artifact.Name.EndsWith(
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return artifact.Metadata.TryGetValue(
                   ArtifactMetadataKeys.ContentType,
                   out var contentType) &&
               string.Equals(
                   contentType?.ToString(),
                   "application/pdf",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizePdfMedicalLiteratureReviewerText(
        string text)
    {
        var normalized =
            text.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace('\r', '\n');

        normalized =
            RemovePdfMedicalLiteratureLayoutNoise(normalized);

        normalized =
            System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"(?<left>\p{L}{2,})[\u00AD\uFFFD\uFFFE\uFFFF](?<right>\p{Ll}{2,})",
                NormalizePdfSplitWord,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        normalized =
            System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"(?<left>\b\p{L}{2,})-[ \t]*(?:(?:\n[ \t]*)+|[ \t]+)(?<right>\p{Ll}{2,}\b)",
                NormalizePdfSplitWord,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        normalized =
            NormalizePdfMedicalLiteratureParagraphStructure(normalized);

        return normalized.Trim();
    }

    private static string RemovePdfMedicalLiteratureLayoutNoise(
        string text)
    {
        var cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(?m)(?:^[ \t]*[•●▪◦][ \t]*$\n?){2,}",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"(?m)^[ \t]*(?:\d{1,3}\.[ \t]*){4,}$\n?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // The Mungan source includes a repeating journal running header that PdfPig
        // can place either on its own line or inline between the two halves of a
        // word in the body column. Remove it before dehyphenation so `pla-` +
        // `cebo` and `to-` + `tal` can be reconstructed correctly. PdfPig may
        // separate the S-page markers and running header with hard line breaks.
        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"S\d{1,4}\s+S\d{1,4}\s+Mungan and Pınarbaşı Şimşek\. Drugs and\s+gastroesophageal reflux disease",
                " ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"Mungan and Pınarbaşı Şimşek\. Drugs and\s+gastroesophageal reflux disease",
                " ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // Standalone supplement-page markers are layout artifacts, not article text.
        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"(?m)^[ \t]*S\d{1,4}[ \t]*$\n?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // Remove the journal correspondence/copyright footer when PdfPig injects
        // it into the middle of the article body.
        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"Address for Correspondence:.*?DOI:\s*10\.5152/tjg\.2017\.11",
                " ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.Singleline);

        // Likewise discard the duplicated journal footer/citation line wherever
        // the PDF layout engine injects it into the body text. The source citation
        // remains elsewhere in the article and package metadata.
        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"Turk J Gastroenterol 2017;[ \t]*28\(Suppl 1\):[ \t]*S38-S43",
                " ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return cleaned;
    }

    private static string NormalizePdfMedicalLiteratureParagraphStructure(
        string text)
    {
        var withHeadingBoundaries =
            SplitInlinePdfMedicalLiteratureHeadings(text);

        var output = new List<string>();
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;

            output.Add(paragraph.ToString());
            paragraph.Clear();
        }

        foreach (var rawLine in withHeadingBoundaries.Split('\n'))
        {
            var line =
                System.Text.RegularExpressions.Regex.Replace(
                        rawLine.Trim(),
                        @"[ \t]+",
                        " ",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant)
                    .Trim();

            // PdfPig paragraph/block boundaries are layout hints, not reliable
            // semantic paragraph boundaries. Ignore empty lines and reflow prose.
            if (line.Length == 0)
                continue;

            if (IsPdfMedicalLiteratureListItemStart(line))
            {
                FlushParagraph();
                paragraph.Append(line);
                continue;
            }

            if (IsPdfMedicalLiteratureStructuralLine(line))
            {
                FlushParagraph();
                output.Add(line);
                continue;
            }

            if (paragraph.Length > 0)
                paragraph.Append(' ');

            paragraph.Append(line);
        }

        FlushParagraph();

        var reflowed = string.Join("\n\n", output);

        // Some PDF extractors concatenate the first word of a new sentence to
        // terminal punctuation. Restore the missing sentence boundary space.
        reflowed =
            System.Text.RegularExpressions.Regex.Replace(
                reflowed,
                @"(?<=[.!?])(?=\p{Lu})",
                " ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return reflowed;
    }

    private static string SplitInlinePdfMedicalLiteratureHeadings(
        string text)
    {
        const string headingPattern =
            "Abstract|Introduction|Background|Methods|Materials and Methods|" +
            "Results|Discussion|Conclusion|Conclusions|References|" +
            "Acknowledgments|Funding|Disclosures|Keywords|" +
            "Non-Steroidal Anti-Inflammatory Drugs|Acetylsalicylic Acid|" +
            "Hormone Replacement Therapy and Oral Contraceptive Drugs|" +
            "Bisphosphonates|Nitrates and Calcium Channel Blockers|" +
            "Antidepressant Drugs|Benzodiazepines and Hypnotic Drugs|" +
            "Anticholinergic Drugs|Antiasthmatic Drugs";

        return System.Text.RegularExpressions.Regex.Replace(
            text,
            $@"(?m)(^|(?<=[.!?])\s+)(?<heading>{headingPattern})(?=\s+\p{{Lu}})",
            match =>
                $"{match.Groups[1].Value.TrimEnd()}\n{match.Groups["heading"].Value}\n",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsPdfMedicalLiteratureListItemStart(
        string line) =>
        line.StartsWith("•", StringComparison.Ordinal) ||
        line.StartsWith("- ", StringComparison.Ordinal) ||
        line.StartsWith("* ", StringComparison.Ordinal);

    private static bool IsPdfMedicalLiteratureStructuralLine(
        string line)
    {
        if (line.StartsWith("Table ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Figure ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var heading = line.Trim().TrimEnd(':');

        if (heading.Length is >= 3 and <= 100 &&
            heading.Any(char.IsLetter) &&
            heading.Where(char.IsLetter).All(char.IsUpper))
        {
            return true;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            heading,
            @"^(?:Abstract|Introduction|Background|Methods|Materials and Methods|Results|Discussion|Conclusion|Conclusions|References|Acknowledgments|Funding|Disclosures|Keywords|Non-Steroidal Anti-Inflammatory Drugs|Acetylsalicylic Acid|Hormone Replacement Therapy and Oral Contraceptive Drugs|Bisphosphonates|Nitrates and Calcium Channel Blockers|Antidepressant Drugs|Benzodiazepines and Hypnotic Drugs|Anticholinergic Drugs|Antiasthmatic Drugs)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string NormalizePdfSplitWord(
        System.Text.RegularExpressions.Match match)
    {
        var left = match.Groups["left"].Value;
        var right = match.Groups["right"].Value;

        return PreserveMedicalCompoundHyphen(left, right)
            ? $"{left}-{right}"
            : left + right;
    }

    private static bool PreserveMedicalCompoundHyphen(
        string left,
        string right)
    {
        if (left.Equals("pre", StringComparison.OrdinalIgnoreCase) &&
            right.Equals("scribed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return
            left.Equals("anti", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("case", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("cross", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("double", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("follow", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("long", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("meta", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("non", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("post", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("pre", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("service", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("short", StringComparison.OrdinalIgnoreCase) ||
            left.Equals("well", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<
        IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedMedicalLiteratureClassificationsAsync(
            EMF.Core.Models.Identities.ArtifactId artifactId,
            string? appendix,
            ServiceConnectionBasisId? serviceConnectionBasisId,
            IReadOnlySet<ServiceConnectionBasisId>? literatureBasisIds,
            CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            appendix != VeteransReviewerPackageAppendix.MedicalLiterature)
        {
            return [];
        }

        if (serviceConnectionBasisId is null)
            throw new InvalidOperationException(
                "A reviewer package containing medical literature must have a persisted service-connection basis.");

        IReadOnlyList<ReviewedMedicalLiteratureClassification> reviewed;

        try
        {
            if (literatureBasisIds is null)
            {
                reviewed =
                    await _medicalLiterature.GetReviewedClassificationsAsync(
                        serviceConnectionBasisId.Value,
                        artifactId,
                        cancellationToken);
            }
            else
            {
                reviewed =
                    (await _medicalLiterature.GetReviewedClassificationsAsync(
                        artifactId,
                        cancellationToken))
                    .Where(
                        item =>
                            item.Association.ServiceConnectionBasisId is not null &&
                            literatureBasisIds.Contains(
                                item.Association.ServiceConnectionBasisId.Value))
                    .ToArray();
            }
        }
        catch (NotSupportedException)
        {
            return [];
        }

        if (reviewed.Any(
                item =>
                    item.ArtifactId != artifactId ||
                    (literatureBasisIds is null
                        ? item.Association.ServiceConnectionBasisId !=
                            serviceConnectionBasisId
                        : item.Association.ServiceConnectionBasisId is null ||
                          !literatureBasisIds.Contains(
                              item.Association.ServiceConnectionBasisId.Value))))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification lookup returned a different artifact.");
        }

        if (reviewed.Any(
                item =>
                    item.SourceExcerpts.Any(
                        excerpt => excerpt.ArtifactId != artifactId)))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification excerpt identity mismatch.");
        }

        var sourceIds =
            await _medicalLiterature.GetMedicalLiteratureSourceIdsAsync(
                artifactId,
                cancellationToken);

        if (reviewed.Any(
                item =>
                    !sourceIds.Contains(
                        item.Association.MedicalLiteratureSourceId)))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification source identity mismatch.");
        }

        return reviewed
            .OrderBy(
                item => item.Association.RequirementId.Value,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.Association.GuidanceRole,
                StringComparer.Ordinal)
            .ThenBy(item => item.ReviewedUtc)
            .ToArray();
    }

    private async Task<string?> GetReviewerSourceNameAsync(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        IReadOnlyList<Relationship> relationships,
        CancellationToken cancellationToken)
    {
        var derivedFrom =
            relationships
                .Where(
                    relationship =>
                        relationship.SourceArtifactId == artifactId &&
                        string.Equals(
                            relationship.RelationshipType,
                            RelationshipTypes.DerivedFrom,
                            StringComparison.Ordinal))
                .ToArray();

        if (derivedFrom.Length > 1)
            throw new InvalidOperationException(
                $"Reviewer evidence artifact '{artifactId.Value}' has " +
                "ambiguous source provenance.");

        if (derivedFrom.Length == 0)
            return null;

        var parent =
            await _evidence.GetArtifactAsync(
                derivedFrom[0].TargetArtifactId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Reviewer evidence source artifact " +
                $"'{derivedFrom[0].TargetArtifactId.Value}' was not found.");

        return GetHumanSourceName(parent);
    }

    private static string GetHumanSourceName(Artifact artifact)
    {
        var name = artifact.Name;

        if (name.Contains(
                "Blue-Button",
                StringComparison.OrdinalIgnoreCase) ||
            name.Contains(
                "Blue Button",
                StringComparison.OrdinalIgnoreCase))
        {
            return "VA Blue Button Report";
        }

        return name;
    }

    private async Task<Artifact> GetReviewerArtifactAsync(
        Artifact artifact,
        string? appendix,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            appendix != VeteransReviewerPackageAppendix.MedicalLiterature)
            return artifact;

        var sourceIds =
            await _medicalLiterature.GetMedicalLiteratureSourceIdsAsync(
                artifact.Id,
                cancellationToken);

        if (sourceIds.Count != 1)
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifact.Id.Value}' " +
                "must map to exactly one literature source.");

        var source =
            await _medicalLiterature.GetMedicalLiteratureSourceAsync(
                sourceIds[0],
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Medical literature source '{sourceIds[0].Value}' " +
                "could not be read.");

        if (source.Id != sourceIds[0])
            throw new InvalidOperationException(
                "Medical literature source identity mismatch.");

        var metadata =
            new Dictionary<string, object>(artifact.Metadata)
            {
                [EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.EvidenceTitle] =
                    source.Title
            };

        if (source.PublicationYear is not null)
        {
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.EvidenceDate] =
                    source.PublicationYear.Value.ToString();
        }

        metadata[
            EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteratureAuthors] =
                source.Authors;

        metadata[
            EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteraturePublication] =
                source.Publication;

        if (!string.IsNullOrWhiteSpace(source.Doi))
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteratureDoi] =
                    source.Doi;

        if (!string.IsNullOrWhiteSpace(source.Pmid))
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteraturePmid] =
                    source.Pmid;

        return new Artifact
        {
            Id = artifact.Id,
            Name = artifact.Name,
            ArtifactType = artifact.ArtifactType,
            Fingerprint = artifact.Fingerprint,
            CreatedUtc = artifact.CreatedUtc,
            Metadata = metadata
        };
    }

    private async Task<string?> GetAppendixAsync(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is not null)
        {
            var literatureSourceIds =
                await _medicalLiterature
                    .GetMedicalLiteratureSourceIdsAsync(
                        artifactId,
                        cancellationToken);

            if (literatureSourceIds.Count > 0)
                return VeteransReviewerPackageAppendix.MedicalLiterature;
        }

        if (_classifications is null)
            return null;

        var visited =
            new HashSet<EMF.Core.Models.Identities.ArtifactId>();
        var appendixes =
            new HashSet<string>(StringComparer.Ordinal);
        var current = artifactId;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(current))
                throw new InvalidOperationException(
                    "Reviewer appendix supersession cycle detected.");

            var classifications =
                await _classifications.GetEvidenceClassificationsAsync(
                    current,
                    cancellationToken);

            if (classifications.Any(x => x.ArtifactId != current))
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' classification lookup " +
                    "returned a different artifact.");

            foreach (var classification in classifications)
            {
                appendixes.Add(
                    VeteransReviewerPackageAppendix.GetAppendix(
                        classification.Classification));
            }

            var relationships =
                await _evidence.GetRelationshipsAsync(
                    current,
                    cancellationToken);

            if (relationships.Any(
                    relationship =>
                        relationship.SourceArtifactId != current &&
                        relationship.TargetArtifactId != current))
            {
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' relationship lookup " +
                    "returned an unrelated artifact relationship.");
            }

            var predecessors =
                relationships
                    .Where(
                        relationship =>
                            relationship.SourceArtifactId == current &&
                            string.Equals(
                                relationship.RelationshipType,
                                RelationshipTypes.Supersedes,
                                StringComparison.Ordinal))
                    .ToArray();

            if (predecessors.Length == 0)
                break;

            if (predecessors.Length > 1)
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' has invalid multiple " +
                    "supersession predecessors.");

            current = predecessors[0].TargetArtifactId;
        }

        return appendixes.Count switch
        {
            0 => null,
            1 => appendixes.Single(),
            _ => throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' maps to multiple reviewer appendixes.")
        };
    }

    private static string? GetTextSummary(
        Artifact artifact)
    {
        if (!string.Equals(
                artifact.ArtifactType,
                "text-summary",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!artifact.Metadata.TryGetValue(
                "summary",
                out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement json when
                json.ValueKind == JsonValueKind.String =>
                json.GetString(),
            _ => null
        };
    }
}
