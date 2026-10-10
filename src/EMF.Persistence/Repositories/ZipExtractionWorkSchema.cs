namespace EMF.Persistence.Repositories;
internal static class ZipExtractionWorkSchema
{
    internal const string Sql="""
        CREATE TABLE ZipExtractionCharges(ParentOperationId TEXT NOT NULL,ChargeId TEXT NOT NULL,
            ReservationId TEXT NOT NULL,FileOrdinal INTEGER NOT NULL,ChargeJson TEXT NOT NULL,
            PRIMARY KEY(ParentOperationId,ChargeId));
        CREATE TRIGGER ZipChargeNoUpdate BEFORE UPDATE ON ZipExtractionCharges
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP work charge'); END;
        CREATE TRIGGER ZipChargeNoDelete BEFORE DELETE ON ZipExtractionCharges
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP work charge'); END;
        CREATE TRIGGER ZipChargeNoReplace BEFORE INSERT ON ZipExtractionCharges
            WHEN EXISTS(SELECT 1 FROM ZipExtractionCharges WHERE ParentOperationId=NEW.ParentOperationId AND ChargeId=NEW.ChargeId)
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP work charge'); END;
        CREATE TABLE ZipExtractionVerifications(ParentOperationId TEXT NOT NULL,FileOrdinal INTEGER NOT NULL,VerificationJson TEXT NOT NULL,
            PRIMARY KEY(ParentOperationId,FileOrdinal));
        CREATE TRIGGER ZipVerificationNoUpdate BEFORE UPDATE ON ZipExtractionVerifications
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP integrity proof'); END;
        CREATE TRIGGER ZipVerificationNoDelete BEFORE DELETE ON ZipExtractionVerifications
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP integrity proof'); END;
        CREATE TRIGGER ZipVerificationNoReplace BEFORE INSERT ON ZipExtractionVerifications
            WHEN EXISTS(SELECT 1 FROM ZipExtractionVerifications WHERE ParentOperationId=NEW.ParentOperationId AND FileOrdinal=NEW.FileOrdinal)
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP integrity proof'); END;
        INSERT INTO ZipExtractionSchema VALUES(3);
        """;
}
