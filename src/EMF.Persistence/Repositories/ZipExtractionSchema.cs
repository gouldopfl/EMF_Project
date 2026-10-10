namespace EMF.Persistence.Repositories;

internal static class ZipExtractionSchema
{
    internal const string Sql = """
        CREATE TABLE ZipExtractionSchema(Version INTEGER PRIMARY KEY CHECK(Version>0));
        INSERT INTO ZipExtractionSchema VALUES(1);
        CREATE TABLE ZipExtractionParents(OperationId TEXT PRIMARY KEY, BindingJson TEXT NOT NULL,
            Owner TEXT NOT NULL, Epoch INTEGER NOT NULL CHECK(Epoch>=0), Revision INTEGER NOT NULL CHECK(Revision>0),
            OwnerUntil TEXT NOT NULL, State INTEGER NOT NULL, PlanJson TEXT NULL, PlanHash TEXT NULL,
            ConfirmedOrdinal INTEGER NOT NULL CHECK(ConfirmedOrdinal>=-1), BudgetJson TEXT NOT NULL, SafeReason TEXT NULL);
        CREATE TABLE ZipExtractionEntries(ParentOperationId TEXT NOT NULL, CentralOrdinal INTEGER NOT NULL,
            FileOrdinal INTEGER NULL, ChildOperationId TEXT NULL UNIQUE, ArtifactId TEXT NULL UNIQUE,
            PlanJson TEXT NOT NULL, ProgressJson TEXT NOT NULL,
            PRIMARY KEY(ParentOperationId,CentralOrdinal), UNIQUE(ParentOperationId,FileOrdinal),
            CHECK((FileOrdinal IS NULL AND ChildOperationId IS NULL AND ArtifactId IS NULL) OR
                  (FileOrdinal IS NOT NULL AND ChildOperationId IS NOT NULL AND ArtifactId IS NOT NULL)));
        CREATE TABLE ZipExtractionReservations(ParentOperationId TEXT NOT NULL, ReservationId TEXT NOT NULL,
            FileOrdinal INTEGER NULL, Kind INTEGER NOT NULL, ReservationJson TEXT NOT NULL,
            PRIMARY KEY(ParentOperationId,ReservationId));
        CREATE TABLE ZipExtractionAcknowledgements(ParentOperationId TEXT NOT NULL, FileOrdinal INTEGER NOT NULL,
            ReceiptJson TEXT NOT NULL, ContainsId INTEGER NOT NULL, DerivedFromId INTEGER NOT NULL,
            PRIMARY KEY(ParentOperationId,FileOrdinal));
        CREATE TABLE ZipExtractionRetentionReceipts(ParentOperationId TEXT NOT NULL, ContentId TEXT NOT NULL,
            Revision TEXT NOT NULL, ReceiptJson TEXT NOT NULL, PRIMARY KEY(ParentOperationId,ContentId,Revision));
        CREATE TRIGGER ZipParentBindingImmutable BEFORE UPDATE OF BindingJson,OperationId ON ZipExtractionParents
            WHEN NEW.BindingJson IS NOT OLD.BindingJson OR NEW.OperationId IS NOT OLD.OperationId
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP parent'); END;
        CREATE TRIGGER ZipParentPlanImmutable BEFORE UPDATE OF PlanJson,PlanHash ON ZipExtractionParents
            WHEN OLD.PlanJson IS NOT NULL AND (NEW.PlanJson IS NOT OLD.PlanJson OR NEW.PlanHash IS NOT OLD.PlanHash)
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP plan'); END;
        CREATE TRIGGER ZipEntryBindingImmutable BEFORE UPDATE OF ParentOperationId,PlanJson,CentralOrdinal,FileOrdinal,ChildOperationId,ArtifactId ON ZipExtractionEntries
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP entry'); END;
        """;
    internal static readonly string[] ImmutableTables = ["ZipExtractionEntries", "ZipExtractionReservations",
        "ZipExtractionAcknowledgements", "ZipExtractionRetentionReceipts"];
}
