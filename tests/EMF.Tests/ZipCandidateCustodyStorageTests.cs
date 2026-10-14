using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;
using Microsoft.Win32.SafeHandles;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ZipCandidateCustodyStorageTests
{
    private sealed class Calls:IZipCandidateNamespaceSyscalls
    {
        private readonly LinuxCandidateNamespaceSyscalls real=new();
        internal int? RenameError;internal bool Unsupported,WrongOwner;
        internal CandidateDirectoryBarrier? Failure;internal int Renames;
        internal readonly List<CandidateDirectoryBarrier> Barriers=[];
        public int RenameNoReplace(int source,string sourceLeaf,int target,string targetLeaf,out int error)
        {Renames++;if(RenameError is {} e){error=e;return -1;}return real.RenameNoReplace(source,sourceLeaf,target,targetLeaf,out error);}
        public void Flush(int fd,CandidateDirectoryBarrier barrier)
        {Barriers.Add(barrier);if(Failure==barrier)throw new IOException("Injected fsync failure");real.Flush(fd,barrier);}
        public void RequireSupportedFileSystem(int fd){if(Unsupported)throw new PlatformNotSupportedException();real.RequireSupportedFileSystem(fd);}
        public ZipCandidateFileIdentity Inspect(int fd,string leaf,int flags)
        {var s=real.Inspect(fd,leaf,flags);return WrongOwner&&(s.Mode&0xF000)==0x8000&&s.Length>0?s with{UserId=s.UserId+1}:s;}
    }
    [Fact]
    public async Task Native_noreplace_conserves_exact_inode_bytes_length_and_keeps_custody()
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        var result=await f.Native.MoveAsync(f.Intent,f.Authority);
        Assert.False(File.Exists(f.Active));Assert.True(File.Exists(f.Custody));Assert.Equal(f.Bytes,await File.ReadAllBytesAsync(f.Custody));
        Assert.Equal(f.Intent.SourceIdentity.Inode,result.File.Inode);Assert.Equal(f.Bytes.Length,result.File.Length);
        Assert.Equal(f.Intent.CandidateHash,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(f.Custody))));
        Assert.Equal(new[]{CandidateDirectoryBarrier.Custody,CandidateDirectoryBarrier.Source},calls.Barriers.Where(b=>b!=CandidateDirectoryBarrier.Provisioning));Assert.Equal(1,calls.Renames);
    }
    [Fact]
    public async Task Existing_destination_is_never_overwritten()
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        Directory.CreateDirectory(Path.GetDirectoryName(f.Custody)!,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        for(var dir=Path.GetDirectoryName(f.Custody);dir is not null&&dir!=f.Root;dir=Path.GetDirectoryName(dir))
            File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        await File.WriteAllBytesAsync(f.Custody,[9,8,7]);
        var error=await Assert.ThrowsAsync<CandidateNamespaceIOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.Equal(17,error.Error);Assert.Equal(0,calls.Renames);Assert.Equal(f.Bytes,await File.ReadAllBytesAsync(f.Active));Assert.Equal(new byte[]{9,8,7},await File.ReadAllBytesAsync(f.Custody));
    }
    [Theory]
    [InlineData(18)][InlineData(38)][InlineData(22)][InlineData(95)]
    public async Task Refused_syscall_has_no_copy_delete_or_other_fallback(int error)
    {
        var calls=new Calls{RenameError=error};using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.Equal(1,calls.Renames);Assert.True(File.Exists(f.Active));Assert.False(File.Exists(f.Custody));
        Assert.DoesNotContain(CandidateDirectoryBarrier.Source,calls.Barriers);Assert.DoesNotContain(CandidateDirectoryBarrier.Custody,calls.Barriers);
    }
    [Fact]
    public async Task Unsupported_filesystem_denies_before_custody_creation()
    {
        var calls=new Calls{Unsupported=true};using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.Equal(0,calls.Renames);Assert.False(Directory.Exists(Path.Combine(f.Root,"zip-candidate-custody")));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task First_barrier_failure_still_attempts_second_and_retains_witness(bool sourceFailure)
    {
        var calls=new Calls{Failure=sourceFailure?CandidateDirectoryBarrier.Source:CandidateDirectoryBarrier.Custody};using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        await Assert.ThrowsAsync<IOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.Equal(new[]{CandidateDirectoryBarrier.Custody,CandidateDirectoryBarrier.Source},calls.Barriers.Where(b=>b!=CandidateDirectoryBarrier.Provisioning));
        Assert.False(File.Exists(f.Active));Assert.Equal(f.Bytes,await File.ReadAllBytesAsync(f.Custody));
    }
    [Theory]
    [InlineData("cancel")][InlineData("revoke")][InlineData("observer")][InlineData("release")]
    public async Task After_successful_accepted_rename_tail_is_not_revocable(string failure)
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);using var cancel=new CancellationTokenSource();
        f.Native.AfterRename=()=>{if(failure=="cancel")cancel.Cancel();if(failure=="revoke")f.Authority.Revoke();if(failure=="observer")throw new IOException("Observer");if(failure=="release")f.Authority.ThrowOnRelease=true;};
        if(failure is "observer" or "release")await Assert.ThrowsAsync<IOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority,cancel.Token));
        else await f.Native.MoveAsync(f.Intent,f.Authority,cancel.Token);
        Assert.Equal(new[]{CandidateDirectoryBarrier.Custody,CandidateDirectoryBarrier.Source},calls.Barriers.Where(b=>b!=CandidateDirectoryBarrier.Provisioning));Assert.False(File.Exists(f.Active));Assert.True(File.Exists(f.Custody));
    }
    [Fact]
    public async Task Revocation_before_final_acceptance_prevents_rename()
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);f.Native.BeforeRename=f.Authority.Revoke;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));Assert.Equal(0,calls.Renames);Assert.True(File.Exists(f.Active));
    }
    [Fact]
    public async Task Missing_acceptance_cannot_create_namespace_open_candidate_or_rename()
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);f.Authority.MissingAcceptance=true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Native.InspectActiveAsync(f.Intent.Anchors.Retention,f.Authority));
        Assert.Equal(0,calls.Renames);Assert.Empty(calls.Barriers);Assert.False(Directory.Exists(Path.Combine(f.Root,"zip-candidate-custody")));Assert.True(File.Exists(f.Active));
    }
    [Fact]
    public async Task Interrupted_directory_creation_requires_parent_barrier_on_retry()
    {
        var calls=new Calls{Failure=CandidateDirectoryBarrier.Provisioning};using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        await Assert.ThrowsAsync<IOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.True(Directory.Exists(Path.Combine(f.Root,"zip-candidate-custody")));Assert.Equal(0,calls.Renames);Assert.True(File.Exists(f.Active));
        calls.Failure=null;calls.Barriers.Clear();await f.Native.MoveAsync(f.Intent,f.Authority);
        Assert.Equal(CandidateDirectoryBarrier.Provisioning,calls.Barriers[0]);
        Assert.Equal(new[]{CandidateDirectoryBarrier.Custody,CandidateDirectoryBarrier.Source},calls.Barriers.TakeLast(2));Assert.True(File.Exists(f.Custody));
    }
    [Fact]
    public async Task Existing_namespace_trailing_separator_grammar_is_preserved()
    {
        using var f=await ZipCandidateCustodyFoundationFixture.Create();var root=f.Root+Path.DirectorySeparatorChar;
        var expected=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root))));
        var native=new LinuxZipCandidateNamespace(root,4096);var identity=f.Intent.Anchors.Retention with{NamespaceId=expected};
        var observed=await native.InspectActiveAsync(identity,f.Authority);Assert.Equal(f.Intent.CandidateHash,observed.Hash);
    }
    [Theory]
    [InlineData("hash")][InlineData("symlink")][InlineData("hardlink")][InlineData("mode")][InlineData("directory")][InlineData("fifo")][InlineData("owner")]
    public async Task Unsafe_or_substituted_source_never_moves(string fault)
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        switch(fault)
        {
            case "hash":await File.WriteAllBytesAsync(f.Active,[1,2]);break;
            case "symlink":File.Move(f.Active,f.Active+"-actual");File.CreateSymbolicLink(f.Active,f.Active+"-actual");break;
            case "hardlink":Assert.Equal(0,link(f.Active,f.Active+"-link"));break;
            case "mode":File.SetUnixFileMode(f.Active,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);break;
            case "directory":File.Delete(f.Active);Directory.CreateDirectory(f.Active);break;
            case "fifo":File.Delete(f.Active);Assert.Equal(0,mkfifo(f.Active,0x180));break;
            case "owner":calls.WrongOwner=true;break;
        }
        if(fault=="hash")await Assert.ThrowsAsync<InvalidDataException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        else await Assert.ThrowsAnyAsync<IOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));
        Assert.Equal(0,calls.Renames);Assert.False(File.Exists(f.Custody));
    }
    [Fact]
    public async Task Source_name_identity_is_rechecked_after_hash()
    {
        var calls=new Calls();using var f=await ZipCandidateCustodyFoundationFixture.Create(calls);
        f.Native.BeforeRename=()=>{File.Move(f.Active,f.Active+"-original");File.WriteAllBytes(f.Active,f.Bytes);};
        await Assert.ThrowsAsync<IOException>(()=>f.Native.MoveAsync(f.Intent,f.Authority));Assert.Equal(0,calls.Renames);
    }
    [Fact]
    public async Task Real_kernel_noreplace_and_exdev_refusal()
    {
        using var f=await ZipCandidateCustodyFoundationFixture.Create();var source=Path.GetDirectoryName(f.Active)!;
        using var sourceFd=new SafeFileHandle((IntPtr)open(source,0x10000|0x20000|0x80000,0),true);
        using var targetFd=new SafeFileHandle((IntPtr)open(f.Root,0x10000|0x20000|0x80000,0),true);
        var calls=new LinuxCandidateNamespaceSyscalls();await File.WriteAllTextAsync(Path.Combine(f.Root,"existing"),"keep");
        Assert.Equal(-1,calls.RenameNoReplace(sourceFd.DangerousGetHandle().ToInt32(),Path.GetFileName(f.Active),targetFd.DangerousGetHandle().ToInt32(),"existing",out var exists));Assert.Equal(17,exists);
        var other=Path.Combine("/dev/shm","emf-d1-exdev-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(other);
        try
        {
            using var fd=new SafeFileHandle((IntPtr)open(other,0x10000|0x20000|0x80000,0),true);
            Assert.Equal(-1,calls.RenameNoReplace(sourceFd.DangerousGetHandle().ToInt32(),Path.GetFileName(f.Active),fd.DangerousGetHandle().ToInt32(),"never.custody",out var exdev));Assert.Equal(18,exdev);Assert.False(File.Exists(Path.Combine(other,"never.custody")));Assert.True(File.Exists(f.Active));
            Assert.Throws<PlatformNotSupportedException>(()=>calls.RequireSupportedFileSystem(fd.DangerousGetHandle().ToInt32()));
        }
        finally{Directory.Delete(other,true);}
    }
    [DllImport("libc",SetLastError=true)]private static extern int open(string path,int flags,int mode);
    [DllImport("libc",SetLastError=true)]private static extern int link(string a,string b);
    [DllImport("libc",SetLastError=true)]private static extern int mkfifo(string path,int mode);
}
