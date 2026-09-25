namespace EMF.Extensions.VeteransClaims.Models;

public static class VeteransArtifactMetadataKeys
{
    public const string SourceStartPage = "sourceStartPage";
    public const string SourceEndPage = "sourceEndPage";
    // Verbatim native text at the beginning/end of a reviewed excerpt. These
    // refine page lineage when neighboring records share a source page.
    public const string SourceStartText = "sourceStartText";
    public const string SourceEndText = "sourceEndText";
    public const string SourceStartLine = "sourceStartLine";
    public const string SourceEndLine = "sourceEndLine";
    public const string ClaimIssueId = "claimIssueId";
    public const string ServiceConnectionBasisId = "serviceConnectionBasisId";
    public const string RequirementIds = "requirementIds";
    public const string NoteDate = "noteDate";
    public const string NoteTitle = "noteTitle";
    public const string EvidenceDate = "evidenceDate";
    public const string EvidenceTitle = "evidenceTitle";
    public const string LiteratureAuthors = "literatureAuthors";
    public const string LiteraturePublication = "literaturePublication";
    public const string LiteratureDoi = "literatureDoi";
    public const string LiteraturePmid = "literaturePmid";
}
