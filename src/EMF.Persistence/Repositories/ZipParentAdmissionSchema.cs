namespace EMF.Persistence.Repositories;

internal static class ZipParentAdmissionSchema
{
    internal const string Sql = """
        CREATE TABLE ZipParentAdmissions(
            OperationId TEXT PRIMARY KEY, IssuerId TEXT NOT NULL, RequestId TEXT NOT NULL,
            BindingJson TEXT NOT NULL, BindingHash TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            UNIQUE(IssuerId,RequestId));
        CREATE TABLE ZipParentAdmissionEvents(
            OperationId TEXT NOT NULL, Sequence INTEGER NOT NULL CHECK(Sequence>0),
            EventId TEXT NOT NULL UNIQUE, EvidenceJson TEXT NOT NULL,
            PRIMARY KEY(OperationId,Sequence));
        CREATE TRIGGER ZipAdmissionNoUpdate BEFORE UPDATE ON ZipParentAdmissions
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission identity'); END;
        CREATE TRIGGER ZipAdmissionNoDelete BEFORE DELETE ON ZipParentAdmissions
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission identity'); END;
        CREATE TRIGGER ZipAdmissionNoReplace BEFORE INSERT ON ZipParentAdmissions
            WHEN EXISTS(SELECT 1 FROM ZipParentAdmissions WHERE OperationId=NEW.OperationId OR (IssuerId=NEW.IssuerId AND RequestId=NEW.RequestId))
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission identity'); END;
        CREATE TRIGGER ZipAdmissionEventNoUpdate BEFORE UPDATE ON ZipParentAdmissionEvents
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission event'); END;
        CREATE TRIGGER ZipAdmissionEventNoDelete BEFORE DELETE ON ZipParentAdmissionEvents
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission event'); END;
        CREATE TRIGGER ZipAdmissionEventNoReplace BEFORE INSERT ON ZipParentAdmissionEvents
            WHEN EXISTS(SELECT 1 FROM ZipParentAdmissionEvents WHERE EventId=NEW.EventId OR (OperationId=NEW.OperationId AND Sequence=NEW.Sequence))
            BEGIN SELECT RAISE(ABORT,'Immutable ZIP admission event'); END;
        INSERT INTO ZipExtractionSchema VALUES(5);
        """;
}
