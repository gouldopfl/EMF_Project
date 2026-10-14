namespace EMF.Persistence.Repositories;

internal static class ZipCandidateCustodySchema
{
    internal const string Sql="""
        CREATE TABLE ZipCandidateCleanupIntents(
          CleanupOperationId TEXT PRIMARY KEY NOT NULL,ParentOperationId TEXT NOT NULL,
          FileOrdinal INTEGER NOT NULL CHECK(FileOrdinal>=0),NamespaceId TEXT NOT NULL,
          CreateOperationId TEXT NOT NULL,ProtocolVersion INTEGER NOT NULL CHECK(ProtocolVersion=1),
          CustodyRelativePath TEXT NOT NULL,IntentDigest TEXT NOT NULL CHECK(length(IntentDigest)=64 AND IntentDigest NOT GLOB '*[^0-9A-F]*'),
          IntentJson TEXT NOT NULL CHECK(json_valid(IntentJson)),
          UNIQUE(ParentOperationId,FileOrdinal),UNIQUE(NamespaceId,CreateOperationId),
          UNIQUE(NamespaceId,CustodyRelativePath),UNIQUE(CleanupOperationId,IntentDigest));
        CREATE TABLE ZipCandidateCustodyCompletions(
          CleanupOperationId TEXT PRIMARY KEY NOT NULL,IntentDigest TEXT NOT NULL,
          ProtocolVersion INTEGER NOT NULL CHECK(ProtocolVersion=1),
          Outcome TEXT NOT NULL CHECK(Outcome='MovedToRetainedCustody'),
          CompletionDigest TEXT NOT NULL CHECK(length(CompletionDigest)=64 AND CompletionDigest NOT GLOB '*[^0-9A-F]*'),
          CompletionJson TEXT NOT NULL CHECK(json_valid(CompletionJson)));
        CREATE TRIGGER ZipCandidateCleanupIntentsNoUpdate BEFORE UPDATE ON ZipCandidateCleanupIntents BEGIN SELECT RAISE(ABORT,'Immutable D1 intent'); END;
        CREATE TRIGGER ZipCandidateCleanupIntentsNoDelete BEFORE DELETE ON ZipCandidateCleanupIntents BEGIN SELECT RAISE(ABORT,'Immutable D1 intent'); END;
        CREATE TRIGGER ZipCandidateCleanupIntentsNoReplace BEFORE INSERT ON ZipCandidateCleanupIntents
          WHEN EXISTS(SELECT 1 FROM ZipCandidateCleanupIntents WHERE CleanupOperationId=NEW.CleanupOperationId OR (ParentOperationId=NEW.ParentOperationId AND FileOrdinal=NEW.FileOrdinal) OR (NamespaceId=NEW.NamespaceId AND CreateOperationId=NEW.CreateOperationId) OR (NamespaceId=NEW.NamespaceId AND CustodyRelativePath=NEW.CustodyRelativePath))
          BEGIN SELECT RAISE(ABORT,'D1 intent rebinding'); END;
        CREATE TRIGGER ZipCandidateCustodyCompletionsNoUpdate BEFORE UPDATE ON ZipCandidateCustodyCompletions BEGIN SELECT RAISE(ABORT,'Immutable D1 completion'); END;
        CREATE TRIGGER ZipCandidateCustodyCompletionsNoDelete BEFORE DELETE ON ZipCandidateCustodyCompletions BEGIN SELECT RAISE(ABORT,'Immutable D1 completion'); END;
        CREATE TRIGGER ZipCandidateCustodyCompletionsNoReplace BEFORE INSERT ON ZipCandidateCustodyCompletions
          WHEN EXISTS(SELECT 1 FROM ZipCandidateCustodyCompletions WHERE CleanupOperationId=NEW.CleanupOperationId)
          BEGIN SELECT RAISE(ABORT,'D1 completion rebinding'); END;
        CREATE TRIGGER ZipCandidateCleanupIntentBinding BEFORE INSERT ON ZipCandidateCleanupIntents
          WHEN json_extract(NEW.IntentJson,'$.ProtocolVersion') IS NOT NEW.ProtocolVersion
            OR json_extract(NEW.IntentJson,'$.CleanupOperationId') IS NOT NEW.CleanupOperationId
            OR json_extract(NEW.IntentJson,'$.IntentDigest') IS NOT NEW.IntentDigest
            OR json_extract(NEW.IntentJson,'$.CustodyRelativePath') IS NOT NEW.CustodyRelativePath
            OR json_extract(NEW.IntentJson,'$.Anchors.Retention.NamespaceId') IS NOT NEW.NamespaceId
            OR json_extract(NEW.IntentJson,'$.Anchors.Retention.CreateOperationId') IS NOT NEW.CreateOperationId
            OR json_extract(NEW.IntentJson,'$.Anchors.Retention.ParentOperationId') IS NOT NEW.ParentOperationId
            OR json_extract(NEW.IntentJson,'$.Anchors.Retention.FileOrdinal') IS NOT NEW.FileOrdinal
            OR NOT EXISTS(SELECT 1 FROM ZipExtractionRetentions r
              WHERE r.ParentOperationId=NEW.ParentOperationId AND r.FileOrdinal=NEW.FileOrdinal
                AND json_extract(r.StateJson,'$.State')=4
                AND json_extract(r.StateJson,'$.ReleaseReceipt') IS NOT NULL
                AND json_extract(r.StateJson,'$.CandidateCleanup') IS NULL
                AND json_extract(r.StateJson,'$.CandidateHash')=json_extract(NEW.IntentJson,'$.CandidateHash')
                AND json_extract(r.StateJson,'$.Revision')=json_extract(NEW.IntentJson,'$.BaseRetentionRevision')
                AND json(r.BindingJson)=json_extract(NEW.IntentJson,'$.Anchors.Retention'))
          BEGIN SELECT RAISE(ABORT,'D1 intent binding changed'); END;
        CREATE TRIGGER ZipCandidateCustodyCompletionBinding BEFORE INSERT ON ZipCandidateCustodyCompletions
          WHEN json_extract(NEW.CompletionJson,'$.ProtocolVersion') IS NOT NEW.ProtocolVersion
            OR json_extract(NEW.CompletionJson,'$.CleanupOperationId') IS NOT NEW.CleanupOperationId
            OR json_extract(NEW.CompletionJson,'$.IntentDigest') IS NOT NEW.IntentDigest
            OR json_extract(NEW.CompletionJson,'$.CompletionDigest') IS NOT NEW.CompletionDigest
            OR json_extract(NEW.CompletionJson,'$.ActiveSourceAbsent') IS NOT 1
            OR json_extract(NEW.CompletionJson,'$.CustodyObjectVerified') IS NOT 1
            OR json_extract(NEW.CompletionJson,'$.SourceDirectoryFlushed') IS NOT 1
            OR json_extract(NEW.CompletionJson,'$.CustodyDirectoryFlushed') IS NOT 1
            OR NOT EXISTS(SELECT 1 FROM ZipCandidateCleanupIntents i
              WHERE i.CleanupOperationId=NEW.CleanupOperationId AND i.IntentDigest=NEW.IntentDigest
                AND i.NamespaceId=json_extract(NEW.CompletionJson,'$.NamespaceId')
                AND i.ParentOperationId=json_extract(NEW.CompletionJson,'$.ParentOperationId')
                AND i.FileOrdinal=json_extract(NEW.CompletionJson,'$.FileOrdinal')
                AND i.CustodyRelativePath=json_extract(NEW.CompletionJson,'$.CustodyRelativePath')
                AND json_extract(i.IntentJson,'$.CandidateHash')=json_extract(NEW.CompletionJson,'$.CandidateHash')
                AND json_extract(i.IntentJson,'$.StoredEncryptedLength')=json_extract(NEW.CompletionJson,'$.StoredEncryptedLength')
                AND (i.IntentJson->'$.SourceIdentity.LinkCount')=(NEW.CompletionJson->'$.CustodyIdentity.LinkCount')
                AND (i.IntentJson->'$.SourceIdentity.UserId')=(NEW.CompletionJson->'$.CustodyIdentity.UserId')
                AND (i.IntentJson->'$.SourceIdentity.GroupId')=(NEW.CompletionJson->'$.CustodyIdentity.GroupId')
                AND (i.IntentJson->'$.SourceIdentity.Mode')=(NEW.CompletionJson->'$.CustodyIdentity.Mode')
                AND (i.IntentJson->'$.SourceIdentity.Length')=(NEW.CompletionJson->'$.CustodyIdentity.Length')
                AND (i.IntentJson->'$.SourceIdentity.ModifiedSeconds')=(NEW.CompletionJson->'$.CustodyIdentity.ModifiedSeconds')
                AND (i.IntentJson->'$.SourceIdentity.ModifiedNanoseconds')=(NEW.CompletionJson->'$.CustodyIdentity.ModifiedNanoseconds')
                AND (i.IntentJson->'$.SourceIdentity.Inode')=(NEW.CompletionJson->'$.CustodyIdentity.Inode')
                AND (i.IntentJson->'$.SourceIdentity.DeviceMajor')=(NEW.CompletionJson->'$.CustodyIdentity.DeviceMajor')
                AND (i.IntentJson->'$.SourceIdentity.DeviceMinor')=(NEW.CompletionJson->'$.CustodyIdentity.DeviceMinor'))
          BEGIN SELECT RAISE(ABORT,'D1 completion intent binding changed'); END;
        CREATE TRIGGER ZipCandidateCustodyNoReopen BEFORE UPDATE OF StateJson ON ZipExtractionRetentions
          WHEN EXISTS(SELECT 1 FROM ZipCandidateCleanupIntents WHERE ParentOperationId=OLD.ParentOperationId AND FileOrdinal=OLD.FileOrdinal)
            AND (json_extract(NEW.StateJson,'$.State') IS NULL OR json_extract(NEW.StateJson,'$.State') NOT IN (4,5,6) OR (json_extract(OLD.StateJson,'$.State')=5 AND json_extract(NEW.StateJson,'$.State') NOT IN (5,6)) OR (json_extract(OLD.StateJson,'$.State')=6 AND json_extract(NEW.StateJson,'$.State')<>6))
          BEGIN SELECT RAISE(ABORT,'D1 candidate creation cannot reopen'); END;
        CREATE TRIGGER ZipCandidateCustodyReleasedProof BEFORE UPDATE OF StateJson ON ZipExtractionRetentions
          WHEN json_extract(NEW.StateJson,'$.State')=5
            AND EXISTS(SELECT 1 FROM ZipCandidateCleanupIntents WHERE ParentOperationId=OLD.ParentOperationId AND FileOrdinal=OLD.FileOrdinal)
            AND NOT EXISTS(SELECT 1 FROM ZipCandidateCleanupIntents i JOIN ZipCandidateCustodyCompletions c ON c.CleanupOperationId=i.CleanupOperationId AND c.IntentDigest=i.IntentDigest
              WHERE i.ParentOperationId=OLD.ParentOperationId AND i.FileOrdinal=OLD.FileOrdinal
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.AlreadyAbsent') IS 0
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.CreateOperationId')=i.CreateOperationId
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.CandidateHash')=json_extract(i.IntentJson,'$.CandidateHash')
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.NamespaceId')=i.NamespaceId
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.CustodyRelativePath')=i.CustodyRelativePath
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.CandidateHash')=json_extract(i.IntentJson,'$.CandidateHash')
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.StoredEncryptedLength')=json_extract(i.IntentJson,'$.StoredEncryptedLength')
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.ProtocolVersion')=1
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.CleanupOperationId')=i.CleanupOperationId
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.IntentDigest')=i.IntentDigest
                AND json_extract(NEW.StateJson,'$.CandidateCleanup.Custody.CompletionDigest')=c.CompletionDigest)
          BEGIN SELECT RAISE(ABORT,'D1 Released requires exact custody proof'); END;
        INSERT INTO ZipExtractionSchema VALUES(6);
        """;
}
