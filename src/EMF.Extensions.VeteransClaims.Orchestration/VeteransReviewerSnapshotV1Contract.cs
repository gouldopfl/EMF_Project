using EMF.Core.Models;
using EMF.Core.Models.Integrity;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// V1 wire fields, explicitly reviewed independently of API serialization attributes.
// Array order is meaningful renderer input and is retained. Object/map keys are ordinal.
// Do not extend this map in place when changing the persisted contract: add a new version.
internal static class VeteransReviewerSnapshotV1Contract
{
    internal static readonly IReadOnlyDictionary<Type, string[]> Fields = new Dictionary<Type, string[]>
    {
        [typeof(VeteransReviewerPackageSnapshot)] = ["Version", "Details", "Regulations"],
        [typeof(VeteransReviewerPackageDetails)] = ["PackageDetails", "Artifacts", "ArtifactContents", "CurrentPrescribedMedications", "MedicationProgressions", "MedicationClinicalContexts", "SourceClarifications", "ClinicalProgressionEvents", "CurrentMedications", "MedicalOpinionRequested", "VeteranDisplayName", "PackagePreparedBy"],
        [typeof(EvidencePackageDetails)] = ["Package", "Artifacts"],
        [typeof(EvidencePackage)] = ["Id", "ClaimIssueId", "Purpose", "ReviewerRole", "ServiceConnectionBasisId"],
        [typeof(EvidencePackageArtifact)] = ["EvidencePackageId", "ArtifactId", "ContentRole", "ReviewerPageSelection"],
        [typeof(Artifact)] = ["Id", "Name", "ArtifactType", "Fingerprint", "CreatedUtc", "Metadata"],
        [typeof(ContentFingerprint)] = ["Algorithm", "Value"],
        [typeof(VeteransReviewerArtifactContent)] = ["Artifact", "Text", "MedicalLiteratureReviewerText", "PrintablePages", "ReviewerPageSelection", "PrintableSourceArtifactId", "IsExtractedTextFallback", "Appendix", "SourceName", "Provenance", "Relationships", "ReviewedMedicalLiteratureClassifications"],
        [typeof(PrintableArtifactPage)] = ["PageNumber", "ContentType", "Content", "SuggestedClockwiseRotation", "TextGeometry"],
        [typeof(PrintableArtifactTextGeometry)] = ["Width", "Height", "ContainsGraphics", "Glyphs"],
        [typeof(PrintableArtifactGlyph)] = ["Text", "X", "EndX", "Top", "Bottom", "Left", "Right", "Baseline", "Font", "FontSize"],
        [typeof(Provenance)] = ["ArtifactId", "Source", "RecordedUtc", "RecordedBy", "Properties"],
        [typeof(Relationship)] = ["SourceArtifactId", "TargetArtifactId", "RelationshipType", "CreatedUtc", "Properties"],
        [typeof(ReviewedMedicalLiteratureClassification)] = ["Association", "ArtifactId", "PromotedBy", "PromotedUtc", "ReviewedBy", "ReviewedUtc", "IntelligenceOutput", "CapabilityId", "ProviderId", "CorrelationId", "EngineName", "EngineVersion", "ProviderOperationId", "StartedUtc", "CompletedUtc", "RequiresReview", "Warnings", "SourceExcerpts"],
        [typeof(RequirementMedicalLiterature)] = ["ServiceConnectionBasisId", "RequirementId", "MedicalLiteratureSourceId", "GuidanceRole", "Description"],
        [typeof(MedicalLiteratureSourceExcerpt)] = ["ArtifactId", "Text", "StartOffset", "Length"],
        [typeof(VeteransReviewerMedication)] = ["CurrentMedication", "EarliestDocumentedRelease"],
        [typeof(MedicationRecord)] = ["Id", "VeteranId", "SourceArtifactId", "RecordDate", "SourcePage", "MedicationName", "Strength", "Directions", "Indication", "Status", "SourceDesignation"],
        [typeof(MedicationHistoryEvent)] = ["Id", "VeteranId", "SourceArtifactId", "EventDate", "SourcePage", "MedicationName", "EventType", "Strength", "Directions", "PharmacyIndication", "PrescriptionNumber"],
        [typeof(VeteransReviewerMedicationProgression)] = ["ServiceConnectionBasisId", "ServiceConnectionBasisReviewerLabel", "MedicationName", "Entries", "EntrySources", "IndicationReconciliation"],
        [typeof(MedicationLedgerEntry)] = ["Id", "MedicationLedgerId", "EntryOrdinal", "SourceStartPage", "SourceEndPage", "MedicationName", "Strength", "Status", "PrescriptionNumber", "PrescribedDate", "LastFilledDate", "LastFilledOnText", "ExpirationDate", "RefillsLeft", "Directions", "Indication", "Prescriber", "Facility", "Quantity"],
        [typeof(MedicationIndicationReconciliation)] = ["Id", "VeteranId", "MedicationName", "ReconciliationDate", "Indication", "Source"],
        [typeof(VeteransReviewerMedicationClinicalContext)] = ["MedicationName", "ContextType", "PrescriptionNumber", "SourceLocator", "Summary"],
        [typeof(VeteransReviewerSourceClarification)] = ["ReviewerArtifactId", "SourceLocator", "OriginalText", "Clarification", "ReviewerMatchText", "ReviewerReplacementText"],
        [typeof(VeteransReviewerClinicalProgressionEvent)] = ["ReviewerArtifactId", "EventDate", "EventType", "SourceLocator", "Summary"],
        [typeof(VeteransReviewerMedicalOpinionRequest)] = ["OpinionText", "ApplicableRegulatoryCitations"],
        [typeof(VeteransReviewerApplicableRegulation)] = ["Citation", "Text", "SourceUri", "UpToDateAsOf", "RetrievedUtc", "SourceSha256"],
    };

    // Explicit V1 nullability, independent of evolving renderer DTO annotations.
    internal static readonly IReadOnlySet<(Type Type, string Field)> NullableFields = new HashSet<(Type, string)>
    {
        (typeof(VeteransReviewerPackageDetails), "MedicalOpinionRequested"),
        (typeof(VeteransReviewerPackageDetails), "VeteranDisplayName"),
        (typeof(VeteransReviewerPackageDetails), "PackagePreparedBy"),
        (typeof(EvidencePackage), "ServiceConnectionBasisId"),
        (typeof(EvidencePackageArtifact), "ReviewerPageSelection"),
        (typeof(Artifact), "Fingerprint"),
        (typeof(VeteransReviewerArtifactContent), "MedicalLiteratureReviewerText"),
        (typeof(VeteransReviewerArtifactContent), "ReviewerPageSelection"),
        (typeof(VeteransReviewerArtifactContent), "PrintableSourceArtifactId"),
        (typeof(VeteransReviewerArtifactContent), "Appendix"),
        (typeof(VeteransReviewerArtifactContent), "SourceName"),
        (typeof(PrintableArtifactPage), "TextGeometry"),
        (typeof(ReviewedMedicalLiteratureClassification), "EngineVersion"),
        (typeof(ReviewedMedicalLiteratureClassification), "ProviderOperationId"),
        (typeof(RequirementMedicalLiterature), "ServiceConnectionBasisId"),
        (typeof(MedicalLiteratureSourceExcerpt), "StartOffset"),
        (typeof(MedicalLiteratureSourceExcerpt), "Length"),
        (typeof(VeteransReviewerMedication), "EarliestDocumentedRelease"),
        (typeof(MedicationRecord), "Strength"),
        (typeof(MedicationRecord), "Directions"),
        (typeof(MedicationRecord), "Indication"),
        (typeof(MedicationRecord), "SourceDesignation"),
        (typeof(MedicationHistoryEvent), "Strength"),
        (typeof(MedicationHistoryEvent), "Directions"),
        (typeof(MedicationHistoryEvent), "PharmacyIndication"),
        (typeof(MedicationHistoryEvent), "PrescriptionNumber"),
        (typeof(VeteransReviewerMedicationProgression), "ServiceConnectionBasisId"),
        (typeof(VeteransReviewerMedicationProgression), "ServiceConnectionBasisReviewerLabel"),
        (typeof(VeteransReviewerMedicationProgression), "IndicationReconciliation"),
        (typeof(MedicationLedgerEntry), "Strength"),
        (typeof(MedicationLedgerEntry), "PrescriptionNumber"),
        (typeof(MedicationLedgerEntry), "PrescribedDate"),
        (typeof(MedicationLedgerEntry), "LastFilledDate"),
        (typeof(MedicationLedgerEntry), "LastFilledOnText"),
        (typeof(MedicationLedgerEntry), "ExpirationDate"),
        (typeof(MedicationLedgerEntry), "RefillsLeft"),
        (typeof(MedicationLedgerEntry), "Directions"),
        (typeof(MedicationLedgerEntry), "Indication"),
        (typeof(MedicationLedgerEntry), "Prescriber"),
        (typeof(MedicationLedgerEntry), "Facility"),
        (typeof(MedicationLedgerEntry), "Quantity"),
        (typeof(VeteransReviewerSourceClarification), "ReviewerMatchText"),
        (typeof(VeteransReviewerSourceClarification), "ReviewerReplacementText"),
    };
}
