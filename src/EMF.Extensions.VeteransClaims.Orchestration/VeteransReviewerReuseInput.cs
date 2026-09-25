using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// This is the semantic input contract for reuse, not a document or prompt renderer.
// Collections are unordered multisets: duplicate evidence is retained, but storage
// identity and insertion order cannot change the meaning of the key.
internal static class VeteransReviewerReuseInput
{
    public static string Serialize(
        ClaimIssueAdjudicationDetails details,
        IReadOnlyList<VeteransReviewerEvidenceSource> sources,
        IReadOnlyList<VeteransReviewerEvidenceDevelopmentDetails> developments,
        IReadOnlyList<EvidenceRecognitionTerm> terms,
        IReadOnlyList<ClinicalProgressionEvent> progression)
    {
        var requirements = details.Requirements.GroupBy(x => x.Requirement.Id)
            .ToDictionary(x => x.Key, x => Consistent(x.Select(y => Json(new
            {
                y.Requirement.Description, y.RegulatoryProvision.Citation
            }))));
        string Requirement(RequirementId id) => requirements.TryGetValue(id, out var value)
            ? value : throw new InvalidOperationException("Reuse input contains an unresolved requirement.");

        var theories = details.ServiceConnectionTheories.GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => Consistent(x.Select(y => Json(y.TheoryType))));
        string Theory(ServiceConnectionTheoryId id) => theories.TryGetValue(id, out var value)
            ? value : throw new InvalidOperationException("Reuse input contains an unresolved service-connection theory.");

        object Source(VeteransReviewerEvidenceSource source) => new
        {
            DisplayName = VeteransReviewerDisplayNameResolver.Resolve("Evidence of Record",
                source.EvidenceTitle, source.SourceName, source.ArtifactName),
            source.ArtifactType, source.ContentRole,
            SourceName = VeteransReviewerDisplayNameResolver.IsReviewerFacingLabel(source.SourceName)
                ? source.SourceName : null,
            source.SourceStartPage, source.SourceEndPage,
            source.EvidenceTitle, source.EvidenceDate, source.Text,
            Classifications = Values(source.Classifications),
            ReviewedLiterature = Values(source.ReviewedMedicalLiteratureClassifications.Select(x => new
            {
                Requirement = Requirement(x.Association.RequirementId),
                x.Association.GuidanceRole, x.Association.Description, x.ReviewedBy, x.ReviewedUtc,
                Excerpts = Values(x.SourceExcerpts.Select(y => new { y.StartOffset, y.Length, y.Text }))
            }))
        };
        var artifacts = sources.GroupBy(x => x.ArtifactId)
            .ToDictionary(x => x.Key, x => Consistent(x.Select(y => Digest(Json(Source(y))))));
        string Artifact(ArtifactId id) => artifacts.TryGetValue(id, out var value)
            ? value : throw new InvalidOperationException("Reuse input references evidence absent from its source set.");

        string BasisValue(ServiceConnectionBasis basis) => Json(new
        {
            Theory = Theory(basis.ServiceConnectionTheoryId), basis.ReviewerLabel,
            ClaimedConditions = Values(details.ClaimedConditionBases.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.ClaimedCondition.Name)),
            Conditions = Values(details.ServiceConnectedConditions.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.ServiceConnectedCondition.Name)),
            Medications = Values(details.PrescribedMedications.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.MedicationName)),
            Exposures = Values(details.Exposures.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.Exposure.ExposureType)),
            Preexisting = Values(details.PreexistingConditions.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.PreexistingCondition.Name)),
            Presumptions = Values(details.Presumptions.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.PresumptionProvision.Citation)),
            Requirements = Values(details.Requirements.Where(x => x.Basis.Id == basis.Id)
                .Select(x => Requirement(x.Requirement.Id))),
            ServiceEvents = Values(details.ServiceEvents.Where(x => x.Basis.Id == basis.Id)
                .Select(x => x.ServiceEvent.Description)),
            Artifacts = Values(details.BasisArtifacts.Where(x => x.Basis.Id == basis.Id)
                .Select(x => new { Source = Artifact(x.ArtifactId), x.Role })),
            Opinions = Values(details.MedicalOpinions.Where(x => x.Basis.Id == basis.Id)
                .Select(x => new { x.Role, x.MedicalOpinion.Question, x.MedicalOpinion.Opinion,
                    Artifacts = Values(x.ArtifactIds.Select(Artifact)) })),
            ExposureArtifacts = Values(details.Exposures.Where(x => x.Basis.Id == basis.Id)
                .Select(x => new { x.Exposure.ExposureType,
                    Artifacts = Values(x.Artifacts.Select(y => new { Source = Artifact(y.ArtifactId), y.Role })) }))
        });
        var bases = details.ServiceConnectionBases.GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => Consistent(x.Select(BasisValue)));
        string Basis(ServiceConnectionBasis basis)
        {
            if (basis.ClaimIssueId != details.ClaimIssue.Id ||
                !bases.TryGetValue(basis.Id, out var value) || value != BasisValue(basis))
                throw new InvalidOperationException("Reuse input contains a conflicting or unresolved service-connection basis.");
            return value;
        }

        object Checklist(EvidenceDevelopmentChecklist checklist) => new
        {
            Requirement = Requirement(checklist.RequirementId),
            Items = Values(checklist.Items.Select(x => new
            {
                Requirement = Requirement(x.RequirementId), x.EvidenceClassification, x.GuidanceRole, x.Description
            }))
        };

        if (details.ServiceConnectionTheories.Any(x => x.ClaimIssueId != details.ClaimIssue.Id) ||
            details.Requirements.Any(x => x.Responsiveness.RequirementId != x.Requirement.Id ||
                x.DevelopmentChecklist.RequirementId != x.Requirement.Id ||
                x.Requirement.RegulatoryProvisionId != x.RegulatoryProvision.Id) ||
            details.Evidence.ClaimIssue.Id != details.ClaimIssue.Id ||
            details.Evidence.Checklist.ClaimIssueId != details.ClaimIssue.Id ||
            details.Timeline.Any(x => x.ClaimIssueId != details.ClaimIssue.Id))
            throw new InvalidOperationException("Reuse input contains evidence from another claim issue.");

        return Json(new
        {
            details.ClaimIssue.ClaimIssueType,
            ClaimedConditions = Values(details.ClaimedConditions.Select(x => x.Name)),
            Theories = Values(details.ServiceConnectionTheories.Select(x => x.TheoryType)),
            Bases = Values(details.ServiceConnectionBases.Select(Basis)),
            ClaimedBases = Values(details.ClaimedConditionBases.Select(x => new
                { Basis = Basis(x.Basis), x.ClaimedCondition.Name })),
            Conditions = Values(details.ServiceConnectedConditions.Select(x => new
                { Basis = Basis(x.Basis), x.ServiceConnectedCondition.Name })),
            Medications = Values(details.PrescribedMedications.Select(x => new
                { Basis = Basis(x.Basis), x.MedicationName })),
            Exposures = Values(details.Exposures.Select(x => new
            {
                Basis = Basis(x.Basis), x.Exposure.ExposureType,
                Artifacts = Values(x.Artifacts.Select(y => new { Source = Artifact(y.ArtifactId), y.Role }))
            })),
            Preexisting = Values(details.PreexistingConditions.Select(x => new
                { Basis = Basis(x.Basis), x.PreexistingCondition.Name })),
            Presumptions = Values(details.Presumptions.Select(x => new
                { Basis = Basis(x.Basis), x.PresumptionProvision.Citation })),
            BasisArtifacts = Values(details.BasisArtifacts.Select(x => new
                { Basis = Basis(x.Basis), Source = Artifact(x.ArtifactId), x.Role })),
            Opinions = Values(details.MedicalOpinions.Select(x => new
            {
                Basis = Basis(x.Basis), x.Role, x.MedicalOpinion.Question, x.MedicalOpinion.Opinion,
                Artifacts = Values(x.ArtifactIds.Select(Artifact))
            })),
            ServiceEvents = Values(details.ServiceEvents.Select(x => new
                { Basis = Basis(x.Basis), x.ServiceEvent.Description })),
            Requirements = Values(details.Requirements.Select(x => new
            {
                Basis = Basis(x.Basis), Requirement = Requirement(x.Requirement.Id),
                x.Responsiveness.MatchingItemCount, x.Responsiveness.MissingItemCount,
                Checklist = Checklist(x.DevelopmentChecklist),
                Literature = Values(x.MedicalLiterature.Select(y => new
                {
                    y.Source.Title, y.Source.Authors, y.Source.Publication, y.Source.PublicationYear,
                    y.Source.VaAffiliated, y.Source.VaFunded, y.Source.PeerReviewed,
                    y.Source.ResearchOrganization, y.Source.FundingSource, y.Source.Doi, y.Source.Pmid,
                    y.Association.GuidanceRole, y.Association.Description
                }))
            })),
            Checklist = Values(details.Evidence.Checklist.RequirementChecklists.Select(Checklist)),
            DevelopmentPlans = Values(details.Evidence.DevelopmentPlans.Select(x => x.Description)),
            Developments = Values(developments.Select(x =>
            {
                if (x.Gap.ClaimIssueId != details.ClaimIssue.Id ||
                    x.Gap.Id != x.Result.EvidenceGapId || x.Gap.RequirementId != x.Result.RequirementId)
                    throw new InvalidOperationException("Reuse input contains conflicting evidence development lineage.");
                var matches = x.Result.RecognitionMatches.GroupBy(y => y.TermId)
                    .ToDictionary(y => y.Key, y => Consistent(y.Select(z => Json(new
                        { z.Term, z.RecognitionRole, z.EvidenceClassification, z.AuthoritySource }))));
                if (x.Result.RecognitionMatchArtifacts.Any(y => !matches.ContainsKey(y.RecognitionTermId)))
                    throw new InvalidOperationException("Reuse input contains an unresolved recognition match.");
                return new
                {
                    Requirement = Requirement(x.Gap.RequirementId),
                    Matches = Values(x.Result.RecognitionMatches.Select(y => new
                    {
                        y.Term, y.RecognitionRole, y.EvidenceClassification, y.AuthoritySource,
                        Artifacts = Values(x.Result.RecognitionMatchArtifacts
                            .Where(z => z.RecognitionTermId == y.TermId)
                            .Select(z => new { Source = Artifact(z.ArtifactId), z.Role }))
                    }))
                };
            })),
            Sources = Values(sources.Select(Source)),
            Timeline = Values(details.Timeline.Select(x => new
                { x.OccurredAt, x.EventType, x.ReferenceId, x.Outcome, x.Description })),
            RecognitionTerms = Values(terms.Select(x => new
            {
                Requirement = Requirement(x.RequirementId), x.Term, x.TermType,
                x.RecognitionRole, x.EvidenceClassification, x.AuthoritySource
            })),
            Progression = Values(progression.Where(x => x.ClaimIssueId == details.ClaimIssue.Id &&
                    artifacts.ContainsKey(x.SourceArtifactId)).Select(x => new
            {
                Source = Artifact(x.SourceArtifactId), x.EventDate, x.SourceStartPage, x.SourceEndPage,
                x.RecordTitle, x.EventType, x.Summary
            }))
        });
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static string[] Values<T>(IEnumerable<T> values) => values.Select(Json)
        .OrderBy(x => x, StringComparer.Ordinal).ToArray();
    private static string Consistent(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Take(2).ToArray();
        if (distinct.Length != 1)
            throw new InvalidOperationException("Reuse input contains conflicting values for one identity.");
        return distinct[0];
    }
}
