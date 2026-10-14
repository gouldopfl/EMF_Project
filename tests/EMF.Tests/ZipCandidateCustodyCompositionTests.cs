using System.Runtime.Versioning;
using System.Reflection;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ZipCandidateCustodyCompositionTests
{
    [Theory]
    [InlineData("")][InlineData("/v1/operation")][InlineData("/../zip-candidate-custody/v1/operation")]
    public async Task Generic_constructors_and_migration_refuse_reserved_tree_before_mutation(string suffix)
    {
        using var f=await ZipCandidateCustodyFoundationFixture.Create();var path=Path.Combine(f.Root,"zip-candidate-custody")+suffix;
        Assert.Throws<InvalidOperationException>(()=>new FileSystemArtifactContentStore(path));
        Assert.Throws<InvalidOperationException>(()=>new FileSystemArtifactContentStagingStore(path));
        Assert.Throws<InvalidOperationException>(()=>new FileSystemArtifactContentGarbageCollector(path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new FileSystemArtifactContentMigration().MigrateAsync(path,new(true)));
        Assert.False(Directory.Exists(Path.Combine(f.Root,"zip-candidate-custody")));
    }
    [Fact]
    public async Task Alias_and_resolved_ancestor_relative_targets_fail_before_creation()
    {
        using var f=await ZipCandidateCustodyFoundationFixture.Create();var custody=Path.Combine(f.Root,"zip-candidate-custody");Directory.CreateDirectory(custody);
        var alias=Path.Combine(f.Root,"alias");Directory.CreateSymbolicLink(alias,custody);
        Assert.ThrowsAny<IOException>(()=>new FileSystemArtifactContentStore(Path.Combine(alias,"child")));
        Assert.ThrowsAny<IOException>(()=>new FileSystemArtifactContentStagingStore(Path.Combine(alias,"child")));
        Assert.Throws<InvalidOperationException>(()=>CandidateCustodyRootPolicy.ResolveGenericTarget(f.Root,"normal/../zip-candidate-custody/v1/file.custody"));
        Assert.ThrowsAny<IOException>(()=>CandidateCustodyRootPolicy.ResolveGenericTarget(f.Root,"alias/new.custody"));Assert.Empty(Directory.GetFileSystemEntries(custody));
    }
    [Fact]
    public async Task Logical_artifact_id_is_not_a_relative_filesystem_path()
    {
        using var f=await ZipCandidateCustodyFoundationFixture.Create();var normal=Path.Combine(f.Root,"normal-content");var store=new FileSystemArtifactContentStore(normal);
        await store.WriteAsync(new ArtifactId("../zip-candidate-custody/v1/../file.custody"),new byte[]{7});
        Assert.False(Directory.Exists(Path.Combine(f.Root,"zip-candidate-custody")));
    }
    [Fact]
    public async Task Inventory_root_and_arbitrary_targets_refuse_custody_without_side_effects()
    {
        using var f = await ZipCandidateCustodyFoundationFixture.Create();
        var custody = Path.Combine(f.Root, "zip-candidate-custody");
        foreach (var path in new[] { custody, Path.Combine(custody, "v1", "operation"), Path.Combine(f.Root, "normal", "..", "zip-candidate-custody") })
        {
            Assert.ThrowsAny<IOException>(() => new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Inventory.Persistence.SqliteInventoryParentJournal(Path.Combine(path, "journal.sqlite")));
        }
        var workspace = new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(Path.Combine(f.Root, "ordinary"));
        Assert.ThrowsAny<IOException>(() => workspace.CreateFile(Path.Combine(custody, "file")));
        Assert.ThrowsAny<IOException>(() => workspace.CreateFile(Path.Combine(f.Root, "ordinary", "..", "zip-candidate-custody", "file")));
        Assert.False(Directory.Exists(custody));
        var adapter = (EMF.Orchestration.Services.InventoryProtectedSnapshotStorageAdapter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(EMF.Orchestration.Services.InventoryProtectedSnapshotStorageAdapter));
        await Assert.ThrowsAnyAsync<IOException>(() => adapter.MaterializeAsync(null!, Path.Combine(custody, "file")));
        Directory.CreateDirectory(custody);
        var alias = Path.Combine(f.Root, "alias"); Directory.CreateSymbolicLink(alias, custody);
        Assert.ThrowsAny<IOException>(() => new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(alias));
        Assert.ThrowsAny<IOException>(() => workspace.CreateFile(Path.Combine(alias, "file")));
        Assert.ThrowsAny<IOException>(() => new EMF.Inventory.Persistence.SqliteInventoryParentJournal(Path.Combine(alias, "journal.sqlite")));
        Assert.Empty(Directory.GetFileSystemEntries(custody));
        using (var file = workspace.CreateFile(Path.Combine(workspace.Root, "normal"))) file.WriteByte(9);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(workspace.Root, "normal")));
    }
    [Fact]
    public async Task Migration_refuses_reserved_regular_name_before_workspace_or_source_mutation()
    {
        using var f = await ZipCandidateCustodyFoundationFixture.Create();
        var root = Path.Combine(f.Root, "legacy"); Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var source = Path.Combine(root, "zip-candidate-custody"); File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentMigration().MigrateAsync(root, new(true)));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        Assert.Single(Directory.GetFileSystemEntries(root));
    }
    [Fact]
    public async Task Shared_generic_writer_exclusion_rejects_reserved_aliases_and_preserves_ordinary_aliases()
    {
        using var f = await ZipCandidateCustodyFoundationFixture.Create();
        var policy = typeof(EMF.Common.EmfBuildManifestFile).Assembly.GetType("EMF.Infrastructure.CandidateCustodyPathExclusion")!;
        var reject = policy.GetMethod("Reject", BindingFlags.Static | BindingFlags.NonPublic)!;
        var sqlite = policy.GetMethod("RejectSqlite", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Denies(MethodInfo method, string path) => Assert.IsAssignableFrom<IOException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { path })).InnerException);
        var custody = Path.Combine(f.Root, "zip-candidate-custody"); Directory.CreateDirectory(custody);
        var alias = Path.Combine(f.Root, "custody-alias"); Directory.CreateSymbolicLink(alias, custody);
        Denies(reject, Path.Combine(alias, "..", "normal"));
        foreach (var path in new[] { custody, Path.Combine(custody, "v1", "file"), Path.Combine(f.Root, "ordinary", "..", "zip-candidate-custody", "file"), Path.Combine(alias, "file") })
        {
            Denies(reject, path);
            Assert.ThrowsAny<IOException>(() => new EMF.Persistence.Repositories.SqliteEvidenceRepository(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Persistence.Repositories.SqliteWorkflowRepository(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Persistence.Repositories.SqliteWorkflowDefinitionRepository(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Persistence.Repositories.SqliteZipExtractionJournal(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Persistence.Repositories.SqliteArtifactIngestionPersistence(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Security.Persistence.Sqlite.SqliteArtifactMutationAuthority(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Security.Persistence.Sqlite.SqliteArtifactRewrapJournal(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Security.Persistence.Sqlite.Auditing.SqliteSecurityAuditSink(path));
            Assert.ThrowsAny<IOException>(() => new EMF.Intelligence.Persistence.Sqlite.SqliteIntelligenceAgentStateStore(path));
        }
        Denies(sqlite, "file:" + Path.Combine(f.Root, "zip%2Dcandidate%2Dcustody", "file") + "?mode=rwc");
        Denies(sqlite, "file://localhost" + Path.Combine(alias, "file"));
        var encodedAlias = Path.Combine(f.Root, "ordinary%2Falias"); Directory.CreateSymbolicLink(encodedAlias, custody);
        Denies(sqlite, "file://localhost" + Path.Combine(f.Root, "ordinary%252Falias", "file"));
        sqlite.Invoke(null, new object[] { ":memory:" });
        var pivot = Path.Combine(f.Root, "pivot"); Directory.CreateSymbolicLink(pivot, Path.Combine(custody, "inner"));
        var tricky = Path.Combine(f.Root, "tricky"); File.CreateSymbolicLink(tricky, Path.Combine(pivot, "..", "v1", "file"));
        Denies(reject, tricky);
        Denies(sqlite, "file://localhost" + Path.Combine(pivot, "..", "v1", "file"));
        var normal = Path.Combine(f.Root, "ordinary"); Directory.CreateDirectory(normal);
        var normalAlias = Path.Combine(f.Root, "ordinary-alias"); Directory.CreateSymbolicLink(normalAlias, normal);
        reject.Invoke(null, new object[] { Path.Combine(normalAlias, "file") });
        var secondAlias = Path.Combine(f.Root, "ordinary-second-alias"); Directory.CreateSymbolicLink(secondAlias, normalAlias);
        reject.Invoke(null, new object[] { Path.Combine(secondAlias, "file") });
        var up = Path.Combine(normal, "up"); Directory.CreateSymbolicLink(up, normal);
        reject.Invoke(null, new object[] { Path.Combine(up, "up", "file") });
        Assert.Empty(Directory.GetFileSystemEntries(custody));
    }
    [Fact]
    public void Dedicated_capability_is_internal_uncomposed_and_never_exposed_by_public_contracts()
    {
        var type=typeof(LinuxZipCandidateNamespace);Assert.False(type.IsVisible);
        bool Refers(Type t)=>t==type||t==typeof(IZipCandidateNamespaceSyscalls)||(t.HasElementType&&Refers(t.GetElementType()!))||t.IsGenericType&&t.GetGenericArguments().Any(Refers);
        foreach(var t in type.Assembly.GetTypes().Where(t=>t!=type&&t.DeclaringType!=type&&t!=typeof(LinuxCandidateNamespaceSyscalls)&&t!=typeof(IZipCandidateNamespaceSyscalls)))
        {
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
            Assert.DoesNotContain(t.GetConstructors(flags).SelectMany(c=>c.GetParameters()),p=>Refers(p.ParameterType));
            Assert.DoesNotContain(t.GetFields(flags),f=>Refers(f.FieldType));
            Assert.DoesNotContain(t.GetMethods(flags),m=>Refers(m.ReturnType)||m.GetParameters().Any(p=>Refers(p.ParameterType)));
        }
        foreach(var t in type.Assembly.GetExportedTypes())
        {
            Assert.DoesNotContain(t.GetConstructors().SelectMany(c=>c.GetParameters()),p=>p.ParameterType==type);
            Assert.DoesNotContain(t.GetProperties(),p=>p.PropertyType==type);
        }
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"EMF.sln")))root=root.Parent;
        Assert.NotNull(root);
        foreach(var path in Directory.GetFiles(Path.Combine(root!.FullName,"src"),"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains("/obj/")&&!p.Contains("/bin/")))
        {
            if(Path.GetFileName(path)=="LinuxZipCandidateNamespace.cs")continue;
            var text=File.ReadAllText(path);
            if(Path.GetFileName(path)!="CandidateCustodyRootPolicy.cs")Assert.DoesNotContain("LinuxZipCandidateNamespace",text);
            Assert.DoesNotContain("new LinuxZipCandidateNamespace",text);
        }
    }
}
