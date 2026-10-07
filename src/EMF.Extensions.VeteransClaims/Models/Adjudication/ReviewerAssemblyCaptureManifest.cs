namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

// Synthetic, non-consumable foundation. None of these identities is registered
// with capture persistence, Security, the coordinator, or reviewer output V1.
public sealed record ReviewerAssemblyDraftIdentity(string Schema, int RepresentationVersion, int ValidationVersion)
{
    public static ReviewerAssemblyDraftIdentity Foundation => new("Reviewer.Capture.FoundationDraft", 1, 1);
}

public enum ReviewerAssemblyReceiptKind { Present, Empty, Absent, NotInvoked }
public enum ReviewerAssemblyArgumentKind { None, Package, Artifact, Issue, Claim, Veteran }
public sealed record ReviewerAssemblyQueryArgument(ReviewerAssemblyArgumentKind Kind, string? Value);
public sealed record ReviewerAssemblyReceipt<T>(string QueryId, ReviewerAssemblyQueryArgument Argument,
    ReviewerAssemblyReceiptKind Kind, T[] Rows);

// Value is a typed identity in the query's fixed field map, never a SQL string or
// input-selected method/type name. Only the explicit query below may interpret it.
public sealed record ReviewerAssemblyQueryField(string Id, string Interface, string Overload,
    ReviewerAssemblyArgumentKind ArgumentKind, string Result, bool Optional, bool Deferred);
public static class ReviewerAssemblyDraftFields
{
    public static IReadOnlyList<ReviewerAssemblyQueryField> Queries { get; } = Array.AsReadOnly(new ReviewerAssemblyQueryField[]
    {
        new ReviewerAssemblyQueryField("package", "IEvidencePackageRepository", "GetEvidencePackageAsync(EvidencePackageId)", ReviewerAssemblyArgumentKind.Package, "Package", false, false),
        new("pending", "IEvidencePackageRepository", "ReadReviewerSnapshotAsync(EvidencePackageId)", ReviewerAssemblyArgumentKind.Package, "Pending", false, false),
        new("snapshot", "IEvidencePackageRepository", "GetReviewerSnapshotAsync(EvidencePackageId)", ReviewerAssemblyArgumentKind.Package, "Snapshot", true, false),
        new("members", "IEvidencePackageRepository", "GetEvidencePackageArtifactsAsync(EvidencePackageId)", ReviewerAssemblyArgumentKind.Package, "Member[]", false, false),
        new("issue", "IClaimIssueRepository", "GetClaimIssueAsync(ClaimIssueId)", ReviewerAssemblyArgumentKind.Issue, "Issue", false, false),
        new("claim", "IClaimRepository", "GetClaimAsync(ClaimId)", ReviewerAssemblyArgumentKind.Claim, "Claim", false, false),
        new("artifact", "IEvidenceRepository", "GetArtifactAsync(ArtifactId)", ReviewerAssemblyArgumentKind.Artifact, "Artifact", false, false),
        new("protection", "ArtifactMutationAuthority", "adopted classification/revision facts for ArtifactId", ReviewerAssemblyArgumentKind.Artifact, "Protection", false, false),
        new("provenance", "IEvidenceRepository", "GetProvenanceAsync(ArtifactId)", ReviewerAssemblyArgumentKind.Artifact, "Provenance[]", false, false),
        new("relationships", "IEvidenceRepository", "GetRelationshipsAsync(ArtifactId)", ReviewerAssemblyArgumentKind.Artifact, "Relationship[]", false, false),
        new("classifications", "IEvidenceClassificationRepository", "GetEvidenceClassificationsAsync(ArtifactId)", ReviewerAssemblyArgumentKind.Artifact, "Classification[]", false, false),
        new("literature", "IMedicalLiteratureRepository", "GetMedicalLiteratureSourceIdsAsync(ArtifactId)", ReviewerAssemblyArgumentKind.Artifact, "LiteratureSourceId[]", false, false),
        new("conditions", "IConditionRepository", "GetClaimedConditionsAsync(ClaimIssueId)", ReviewerAssemblyArgumentKind.Issue, "Condition[]", false, false),
        new("ledgers", "IMedicationRepository", "GetMedicationLedgersAsync(VeteranId)", ReviewerAssemblyArgumentKind.Veteran, "Ledger[]", false, false),
        new("clarifications", "ISourceClarificationRepository", "GetAsync(ClaimIssueId)", ReviewerAssemblyArgumentKind.Issue, "Clarification[]", false, false),
        new("progression", "IClinicalProgressionRepository", "GetAsync(ClaimIssueId)", ReviewerAssemblyArgumentKind.Issue, "Progression[]", false, false),
        new("ledgerEntries", "IMedicationRepository", "GetMedicationLedgerEntriesAsync(MedicationLedgerId)", ReviewerAssemblyArgumentKind.None, "NotInvoked:no ledger", false, true),
        new("currentUse", "IMedicationRepository", "GetMedicationCurrentUseReconciliationsAsync(VeteranId)", ReviewerAssemblyArgumentKind.None, "NotInvoked:no ledger", false, true),
        new("basis", "IServiceConnectionRepository", "GetServiceConnectionBasisAsync(ServiceConnectionBasisId)", ReviewerAssemblyArgumentKind.None, "NotInvoked:null selected basis", false, true),
        new("clinicalContext", "IMedicationRepository", "GetMedicationClinicalContextsAsync(VeteranId)", ReviewerAssemblyArgumentKind.None, "NotInvoked:no progressions", false, true),
        new("adjudication", "IClaimIssueAdjudicationDetailsService", "GetAsync(ClaimIssueId)", ReviewerAssemblyArgumentKind.None, "NotInvoked:no literature/basis", false, true),
        new("regulatory", "IVeteransReviewerRegulatoryTextProvider", "GetCurrentAsync(citations)", ReviewerAssemblyArgumentKind.None, "NotInvoked:no opinion/citations", false, true)
    });
}

public sealed record ReviewerAssemblyPackage(string Id, string IssueId, string Purpose, string ReviewerRole, string? SelectedBasisId);
public sealed record ReviewerAssemblyPending(bool IsLegacy, int SourceSnapshotVersion, bool IsSealed, string? Snapshot);
public sealed record ReviewerAssemblySnapshot(string PackageId, string Sha256);
public sealed record ReviewerAssemblyMember(string PackageId, string ArtifactId, string ContentRole, string? PageSelection);
public sealed record ReviewerAssemblyIssue(string Id, string ClaimId, string ClaimIssueType);
public sealed record ReviewerAssemblyClaim(string Id, string VeteranId);
public sealed record ReviewerAssemblyArtifact(string Id, string Name, string ArtifactType, string FingerprintAlgorithm,
    string Fingerprint, DateTimeOffset CreatedUtc, Dictionary<string, ReviewerAssemblyValue> Metadata);
public sealed record ReviewerAssemblyProtection(string ArtifactId, string ClassificationId, string ClassificationRevision, bool IsAdopted);
public sealed record ReviewerAssemblyProvenance(string ArtifactId, string Source, DateTimeOffset RecordedUtc,
    string RecordedBy, Dictionary<string, ReviewerAssemblyValue> Properties);
public sealed record ReviewerAssemblyRelationship(string SourceArtifactId, string TargetArtifactId, string RelationshipType,
    DateTimeOffset CreatedUtc, Dictionary<string, ReviewerAssemblyValue> Properties);
public sealed record ReviewerAssemblyClassification(string Id, string ArtifactId, string? IssueId, string Classification);
public sealed record ReviewerAssemblyCondition(string Id, string IssueId, string Name);
public sealed record ReviewerAssemblyLedger(string Id, string VeteranId, string SourceArtifactId, DateOnly ReportDate,
    int SourceStartPage, int SourceEndPage, int? ReportedEntryCount, int ParsedEntryCount, bool IsComplete);
public sealed record ReviewerAssemblyClarification(string Id, string IssueId, string SourceArtifactId, DateOnly EvidenceDate,
    int SourceStartPage, int SourceEndPage, string RecordTitle, string Category, string OriginalText, string Clarification,
    string? ReviewerMatchText, string? ReviewerReplacementText);
public sealed record ReviewerAssemblyProgression(string Id, string IssueId, string SourceArtifactId, DateOnly EventDate,
    int? SourceStartPage, int? SourceEndPage, string RecordTitle, string EventType, string Summary);
public sealed record ReviewerAssemblyDeferredReceipt(string QueryId, ReviewerAssemblyQueryArgument Argument,
    ReviewerAssemblyReceiptKind Kind, string Predicate);
public sealed record ReviewerAssemblySource(string ArtifactId, string PhysicalRevision, string ContentArtifactId,
    string ContentPhysicalRevision, string FingerprintAlgorithm, string Fingerprint, string Sha256, byte[] Content);
public sealed record ReviewerAssemblyPreparation(string? VeteranDisplayName, string? Preparer, DateOnly PreparedDate,
    DateOnly ReviewDate, string OutputFormat, string ExtractionProfile, string PrintProfile, string RendererProfile,
    string? ConverterProfile, string RegulatoryProfile, string[] RequiredCitations,
    Dictionary<string, string> RegulatoryMap, int[] SelectedPageSequence);

// Closed tagged value algebra preserves scalar/container types, including binary
// values. No arbitrary object payload or runtime type-name deserialization.
public enum ReviewerAssemblyValueKind { Null, String, Boolean, Int64, Decimal, Binary, Array, Map }
public sealed record ReviewerAssemblyValue(ReviewerAssemblyValueKind Kind, string? Text, bool? Boolean,
    long? Integer, decimal? Decimal, byte[]? Binary, ReviewerAssemblyValue[]? Items,
    Dictionary<string, ReviewerAssemblyValue>? Properties)
{
    public static ReviewerAssemblyValue String(string value) => new(ReviewerAssemblyValueKind.String, value, null, null, null, null, null, null);
    public static ReviewerAssemblyValue Map(Dictionary<string, ReviewerAssemblyValue> value) => new(ReviewerAssemblyValueKind.Map, null, null, null, null, null, null, value);
}

public sealed record ReviewerAssemblyCaptureManifest(ReviewerAssemblyDraftIdentity Identity, string SnapshotId,
    string OperationId, ReviewerAssemblyReceipt<ReviewerAssemblyPackage> Package,
    ReviewerAssemblyReceipt<ReviewerAssemblyPending> Pending, ReviewerAssemblyReceipt<ReviewerAssemblySnapshot> Snapshot,
    ReviewerAssemblyReceipt<ReviewerAssemblyMember> Members, ReviewerAssemblyReceipt<ReviewerAssemblyIssue> Issue,
    ReviewerAssemblyReceipt<ReviewerAssemblyClaim> Claim, ReviewerAssemblyReceipt<ReviewerAssemblyArtifact>[] Artifacts,
    ReviewerAssemblyReceipt<ReviewerAssemblyProtection>[] Protection, ReviewerAssemblyReceipt<ReviewerAssemblyProvenance>[] Provenance,
    ReviewerAssemblyReceipt<ReviewerAssemblyRelationship>[] Relationships,
    ReviewerAssemblyReceipt<ReviewerAssemblyClassification>[] Classifications,
    ReviewerAssemblyReceipt<string>[] Literature, ReviewerAssemblyReceipt<ReviewerAssemblyCondition> Conditions,
    ReviewerAssemblyReceipt<ReviewerAssemblyLedger> Ledgers, ReviewerAssemblyReceipt<ReviewerAssemblyClarification> Clarifications,
    ReviewerAssemblyReceipt<ReviewerAssemblyProgression> Progression, ReviewerAssemblyDeferredReceipt[] Deferred,
    ReviewerAssemblySource[] Sources, ReviewerAssemblyPreparation Preparation);

// All limits are explicit; callers must not infer production defaults from tests.
public sealed record ReviewerAssemblyFoundationLimits(int MaximumEncodedBytes, int MaximumDepth, int MaximumValueBytes,
    int MaximumReceipts, int MaximumRows, int MaximumMembers, long MaximumSourceBytes, long MaximumAggregateSourceBytes,
    long MaximumPrivateBytes, long MaximumDetachedBytes, long MaximumCopyWork, int MaximumLeases)
{
    public void Validate()
    {
        if (MaximumEncodedBytes <= 0 || MaximumEncodedBytes > Array.MaxLength || MaximumDepth <= 0 || MaximumDepth > 128 ||
            MaximumValueBytes <= 0 || MaximumReceipts <= 0 || MaximumRows <= 0 || MaximumMembers <= 0 ||
            MaximumSourceBytes <= 0 || MaximumAggregateSourceBytes <= 0 || MaximumPrivateBytes <= 0 ||
            MaximumDetachedBytes <= 0 || MaximumCopyWork <= 0 || MaximumLeases <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReviewerAssemblyFoundationLimits));
        checked { _ = (long)MaximumEncodedBytes * 16 + 4096; }
    }
}
