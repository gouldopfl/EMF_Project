namespace EMF.Persistence.Repositories;
internal static class ZipScanSchema
{
    internal const string Sql="""
        CREATE TABLE ZipExtractionScans(ParentOperationId TEXT NOT NULL,ReservationId TEXT NOT NULL,FileOrdinal INTEGER NOT NULL,
            BindingJson TEXT NOT NULL,EvidenceJson TEXT NULL,PRIMARY KEY(ParentOperationId,ReservationId));
        CREATE TRIGGER ZipScanBindingImmutable BEFORE UPDATE OF ParentOperationId,ReservationId,FileOrdinal,BindingJson ON ZipExtractionScans
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP scanner attempt'); END;
        CREATE TRIGGER ZipScanEvidenceImmutable BEFORE UPDATE OF EvidenceJson ON ZipExtractionScans
            WHEN OLD.EvidenceJson IS NOT NULL AND NEW.EvidenceJson IS NOT OLD.EvidenceJson
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP scanner evidence'); END;
        CREATE TRIGGER ZipScanNoDelete BEFORE DELETE ON ZipExtractionScans
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP scanner evidence'); END;
        CREATE TRIGGER ZipScanNoReplace BEFORE INSERT ON ZipExtractionScans
            WHEN EXISTS(SELECT 1 FROM ZipExtractionScans WHERE ParentOperationId=NEW.ParentOperationId AND ReservationId=NEW.ReservationId)
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP scanner evidence'); END;
        INSERT INTO ZipExtractionSchema VALUES(4);
        """;
}
