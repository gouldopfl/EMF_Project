namespace EMF.Persistence.Repositories;

internal static class ZipRetentionSchema
{
    internal const string Sql="""
        CREATE TABLE ZipExtractionRetentions(ParentOperationId TEXT NOT NULL,FileOrdinal INTEGER NOT NULL,
            BindingJson TEXT NOT NULL,StateJson TEXT NOT NULL,
            PRIMARY KEY(ParentOperationId,FileOrdinal));
        CREATE TRIGGER ZipRetentionBindingImmutable BEFORE UPDATE OF ParentOperationId,FileOrdinal,BindingJson ON ZipExtractionRetentions
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP retention binding'); END;
        CREATE TRIGGER ZipRetentionNoDelete BEFORE DELETE ON ZipExtractionRetentions
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP retention evidence'); END;
        CREATE TRIGGER ZipRetentionNoReplace BEFORE INSERT ON ZipExtractionRetentions
            WHEN EXISTS(SELECT 1 FROM ZipExtractionRetentions WHERE ParentOperationId=NEW.ParentOperationId AND FileOrdinal=NEW.FileOrdinal)
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP retention evidence'); END;
        CREATE TRIGGER ZipRetentionFrozenEvidence BEFORE UPDATE OF StateJson ON ZipExtractionRetentions
            WHEN json_extract(NEW.StateJson,'$.Revision')!=json_extract(OLD.StateJson,'$.Revision')+1
            OR (json_extract(OLD.StateJson,'$.CandidateHash') IS NOT NULL AND json_extract(NEW.StateJson,'$.CandidateHash') IS NOT json_extract(OLD.StateJson,'$.CandidateHash'))
            OR (json_extract(OLD.StateJson,'$.CreateReceipt') IS NOT NULL AND json_extract(NEW.StateJson,'$.CreateReceipt') IS NOT json_extract(OLD.StateJson,'$.CreateReceipt'))
            OR (json_extract(OLD.StateJson,'$.ReleaseReceipt') IS NOT NULL AND json_extract(NEW.StateJson,'$.ReleaseReceipt') IS NOT json_extract(OLD.StateJson,'$.ReleaseReceipt'))
            OR (json_extract(OLD.StateJson,'$.CandidateCleanup') IS NOT NULL AND json_extract(NEW.StateJson,'$.CandidateCleanup') IS NOT json_extract(OLD.StateJson,'$.CandidateCleanup'))
            BEGIN SELECT RAISE(ABORT,'Frozen ZIP retention evidence'); END;
        INSERT INTO ZipExtractionSchema VALUES(2);
        """;
}
