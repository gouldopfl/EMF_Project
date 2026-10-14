using System.Runtime.Versioning;
using System.Reflection;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ZipCandidateCustodySchemaTests
{
    private static string V5=>(string)typeof(SqliteZipExtractionJournal).GetProperty("SchemaSql",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!+"\n"+ZipRetentionSchema.Sql+"\n"+ZipExtractionWorkSchema.Sql+"\n"+ZipScanSchema.Sql+"\n"+ZipParentAdmissionSchema.Sql;
    private static async Task<long> Scalar(ArtifactIngestionFixture f,string sql)
    {await using var c=new SqliteConnection("Data Source="+f.DatabasePath+";Pooling=False");await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(await q.ExecuteScalarAsync());}
    private static async Task<string> OldSnapshot(ArtifactIngestionFixture f)
    {
        await using var c=new SqliteConnection("Data Source="+f.DatabasePath+";Pooling=False");await c.OpenAsync();using var q=c.CreateCommand();
        q.CommandText="SELECT name,type,sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'ZipCandidate%' ORDER BY name";
        var lines=new List<string>();var tables=new List<string>();using(var rows=await q.ExecuteReaderAsync())while(await rows.ReadAsync())
        {var values=new object[3];rows.GetValues(values);lines.Add(JsonSerializer.Serialize(values));if(rows.GetString(1)=="table")tables.Add(rows.GetString(0));}
        foreach(var table in tables)
        {
            q.CommandText="SELECT * FROM \""+table+"\""+(table=="ZipExtractionSchema"?" WHERE Version<>6":"")+" ORDER BY rowid";
            using var rows=await q.ExecuteReaderAsync();while(await rows.ReadAsync()){var values=new object[rows.FieldCount];rows.GetValues(values);lines.Add(table+JsonSerializer.Serialize(values));}
        }
        return string.Join("\n",lines);
    }
    private static Task SeedRetention(ArtifactIngestionFixture f,CandidateCleanupIntent i,bool legacyReleased=false)
    {
        var r=new ZipRetentionRecord(i.Anchors.Retention,legacyReleased?ZipRetentionState.Released:ZipRetentionState.ReleasePending,1,i.CandidateHash,i.Anchors.CreateReceipt,i.Anchors.ReleaseReceipt,
            legacyReleased?new(i.Anchors.Retention.CreateOperationId,i.CandidateHash,true):null);
        return f.SqlAsync("INSERT INTO ZipExtractionRetentions VALUES($op,0,$binding,$state)",("$op",r.Identity.ParentOperationId),("$binding",JsonSerializer.Serialize(r.Identity)),("$state",JsonSerializer.Serialize(r)));
    }
    private static Task InsertIntent(ArtifactIngestionFixture f,CandidateCleanupIntent i,string verb="INSERT")=>f.SqlAsync(verb+" INTO ZipCandidateCleanupIntents VALUES($cleanup,$parent,$ordinal,$namespace,$create,1,$path,$digest,$json)",
        ("$cleanup",i.CleanupOperationId),("$parent",i.Anchors.Retention.ParentOperationId),("$ordinal",i.Anchors.Retention.FileOrdinal),("$namespace",i.Anchors.Retention.NamespaceId),("$create",i.Anchors.Retention.CreateOperationId),("$path",i.CustodyRelativePath),("$digest",i.IntentDigest),("$json",ZipCandidateCustodyEncoding.Json(i)));
    private static Task InsertCompletion(ArtifactIngestionFixture f,CandidateCustodyCompletion c,string verb="INSERT")=>f.SqlAsync(verb+" INTO ZipCandidateCustodyCompletions VALUES($op,$intent,1,'MovedToRetainedCustody',$digest,$json)",
        ("$op",c.CleanupOperationId),("$intent",c.IntentDigest),("$digest",c.CompletionDigest),("$json",ZipCandidateCustodyEncoding.Json(c)));
    [Fact]
    public async Task Genuine_v5_upgrade_preserves_every_old_schema_row_and_legacy_released_bytes()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();await f.SqlAsync(V5);var i=ZipCandidateCustodyEncodingTests.Golden();await SeedRetention(f,i,true);
        var before=await OldSnapshot(f);var journal=new SqliteZipExtractionJournal(f.DatabasePath);await journal.InitializeAsync();
        Assert.Equal(6,await Scalar(f,"SELECT MAX(Version) FROM ZipExtractionSchema"));Assert.Equal(before,await OldSnapshot(f));
        Assert.Equal(0,await Scalar(f,"SELECT COUNT(*) FROM ZipCandidateCleanupIntents"));Assert.Equal(0,await Scalar(f,"SELECT COUNT(*) FROM ZipCandidateCustodyCompletions"));
        var legacy=await journal.ReadRetentionAsync(i.Anchors.Retention.ParentOperationId,0);Assert.True(legacy!.CandidateCleanup!.AlreadyAbsent);Assert.Equal(ZipRetentionState.Released,legacy.State);
        await journal.InitializeAsync();Assert.Equal(before,await OldSnapshot(f));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Migration_failure_rolls_back_v6_and_preserves_exact_v5(bool late)
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();await f.SqlAsync(V5);
        await f.SqlAsync(late?"CREATE TRIGGER ZipCandidateCustodyReleasedProof BEFORE UPDATE ON ZipExtractionRetentions BEGIN SELECT 1; END":"CREATE TABLE ZipCandidateCustodyCompletions(Collision TEXT)");
        var before=await OldSnapshot(f);await Assert.ThrowsAsync<SqliteException>(()=>new SqliteZipExtractionJournal(f.DatabasePath).InitializeAsync());
        Assert.Equal(5,await Scalar(f,"SELECT MAX(Version) FROM ZipExtractionSchema"));Assert.Equal(before,await OldSnapshot(f));Assert.Equal(0,await Scalar(f,"SELECT COUNT(*) FROM sqlite_master WHERE name='ZipCandidateCleanupIntents'"));
        await f.SqlAsync(late?"DROP TRIGGER ZipCandidateCustodyReleasedProof":"DROP TABLE ZipCandidateCustodyCompletions");await new SqliteZipExtractionJournal(f.DatabasePath).InitializeAsync();
        Assert.Equal(6,await Scalar(f,"SELECT MAX(Version) FROM ZipExtractionSchema"));
    }
    [Fact]
    public async Task Immutable_rows_exact_intent_link_and_no_reopen_guards()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();await new SqliteZipExtractionJournal(f.DatabasePath).InitializeAsync();var i=ZipCandidateCustodyEncodingTests.Golden();await SeedRetention(f,i);
        var c=ZipCandidateCustodyFoundationFixture.Completion(i);
        await Assert.ThrowsAsync<SqliteException>(()=>InsertCompletion(f,c));await InsertIntent(f,i);await InsertCompletion(f,c);
        foreach(var table in new[]{"ZipCandidateCleanupIntents","ZipCandidateCustodyCompletions"})
        {
            await Assert.ThrowsAsync<SqliteException>(()=>f.SqlAsync("DELETE FROM "+table));
            await Assert.ThrowsAsync<SqliteException>(()=>f.SqlAsync("UPDATE "+table+" SET CleanupOperationId='rebound'"));
        }
        await Assert.ThrowsAsync<SqliteException>(()=>InsertIntent(f,i,"INSERT OR REPLACE"));await Assert.ThrowsAsync<SqliteException>(()=>InsertCompletion(f,c,"INSERT OR REPLACE"));
        await Assert.ThrowsAsync<SqliteException>(()=>InsertCompletion(f,c with{IntentDigest=new string('0',64)}));
        await Assert.ThrowsAsync<SqliteException>(()=>f.SqlAsync("UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Revision',2,'$.State',1)"));
        await Assert.ThrowsAsync<SqliteException>(()=>f.SqlAsync("UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Revision',2,'$.State',5,'$.CandidateCleanup',json_object('CreateOperationId','create-op','CandidateHash',$hash,'AlreadyAbsent',1))",("$hash",i.CandidateHash)));
        Assert.Equal(4,await Scalar(f,"SELECT json_extract(StateJson,'$.State') FROM ZipExtractionRetentions"));
    }
    [Fact]
    public async Task Missing_v6_guard_and_corrupted_v5_guard_fail_exact_validation()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();await f.SqlAsync(V5);
        await f.SqlAsync("DROP TRIGGER ZipRetentionFrozenEvidence");await Assert.ThrowsAsync<InvalidDataException>(()=>new SqliteZipExtractionJournal(f.DatabasePath).InitializeAsync());
        Assert.Equal(5,await Scalar(f,"SELECT MAX(Version) FROM ZipExtractionSchema"));
        await using var second=await ArtifactIngestionFixture.CreateAsync();var journal=new SqliteZipExtractionJournal(second.DatabasePath);await journal.InitializeAsync();
        await second.SqlAsync("DROP TRIGGER ZipCandidateCustodyNoReopen");await Assert.ThrowsAsync<InvalidDataException>(()=>journal.InitializeAsync());await Assert.ThrowsAsync<InvalidDataException>(()=>journal.ReadRetentionAsync("missing",0));
    }
}
