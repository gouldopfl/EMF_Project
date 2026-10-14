using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using Microsoft.Win32.SafeHandles;

namespace EMF.Persistence.Storage;

internal enum CandidateDirectoryBarrier { Provisioning,Custody,Source }
internal interface IZipCandidateNamespaceSyscalls
{
    int RenameNoReplace(int source,string sourceLeaf,int target,string targetLeaf,out int error);
    void Flush(int descriptor,CandidateDirectoryBarrier barrier);
    void RequireSupportedFileSystem(int descriptor);
    ZipCandidateFileIdentity Inspect(int descriptor,string leaf,int flags);
}
internal sealed class LinuxCandidateNamespaceSyscalls:IZipCandidateNamespaceSyscalls
{
    public ZipCandidateFileIdentity Inspect(int descriptor,string leaf,int flags)=>LinuxZipCandidateNamespace.InspectNative(descriptor,leaf,flags);
    public int RenameNoReplace(int source,string sourceLeaf,int target,string targetLeaf,out int error)
    {
        try{var result=LinuxZipCandidateNamespace.renameat2(source,sourceLeaf,target,targetLeaf,1);error=result==0?0:Marshal.GetLastPInvokeError();return result;}
        catch(EntryPointNotFoundException e){throw new PlatformNotSupportedException("renameat2 NOREPLACE is unavailable; no fallback.",e);}
    }
    public void Flush(int descriptor,CandidateDirectoryBarrier barrier)
    {
        while(LinuxZipCandidateNamespace.fsync(descriptor)!=0)
        {var error=Marshal.GetLastPInvokeError();if(error==4)continue;throw new CandidateNamespaceIOException("Directory fsync failed.",error);}
    }
    public void RequireSupportedFileSystem(int descriptor)
    {
        var buffer=Marshal.AllocHGlobal(256);
        try
        {
            if(LinuxZipCandidateNamespace.fstatfs(descriptor,buffer)!=0)throw new CandidateNamespaceIOException("Cannot inspect candidate filesystem.",Marshal.GetLastPInvokeError());
            var type=Marshal.ReadInt64(buffer);
            if(type!=0xEF53&&type!=0x58465342&&type!=0x9123683E)throw new PlatformNotSupportedException("D1 requires supported local ext/XFS/Btrfs storage.");
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
}
internal sealed class CandidateNamespaceIOException(string message,int error):IOException(message+" errno="+error)
{internal int Error{get;}=error;}
internal sealed record CandidateFileInspection(ZipCandidateDirectoryIdentity Root,
    ZipCandidateDirectoryIdentity SourceDirectory,ZipCandidateFileIdentity File,string Hash);
internal sealed record CandidateCustodyMoveObservation(ZipCandidateFileIdentity File,
    ZipCandidateDirectoryIdentity CustodyDirectory,string RelativePath);

// Internal foundation plumbing ONLY: not composed, not live cleanup authority,
// not a journal interlock, receipt writer or Released transition. Later live
// integration must hold the exact journal writer reservation across MoveAsync.
internal sealed class LinuxZipCandidateNamespace
{
    private const int DirectoryFlags=0x10000|0x20000|0x80000; // DIRECTORY|NOFOLLOW|CLOEXEC
    private const int FileFlags=0x20000|0x80000|0x800; // NOFOLLOW|CLOEXEC|NONBLOCK
    private readonly string root;
    private readonly string namespaceId;
    private readonly long maximum;
    private readonly IZipCandidateNamespaceSyscalls calls;
    internal Action? AfterRename {get;set;}
    internal Action? BeforeRename {get;set;}
    internal LinuxZipCandidateNamespace(string privateRoot,long maximumBytes,IZipCandidateNamespaceSyscalls? syscalls=null)
    {
        if(!OperatingSystem.IsLinux()||IntPtr.Size!=8)throw new PlatformNotSupportedException("D1 requires 64-bit Linux.");
        if(maximumBytes<=0||maximumBytes>Array.MaxLength)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        CandidateCustodyRootPolicy.RejectGenericRoot(privateRoot);
        namespaceId=ZipCandidateCustodyEncoding.Namespace(privateRoot);
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(privateRoot));maximum=maximumBytes;calls=syscalls??new LinuxCandidateNamespaceSyscalls();
    }
    [DllImport("libc",SetLastError=true)] private static extern int open(string path,int flags,int mode);
    [DllImport("libc",SetLastError=true)] private static extern int openat(int dir,string path,int flags,int mode);
    [DllImport("libc",SetLastError=true)] private static extern int mkdirat(int dir,string path,int mode);
    [DllImport("libc",SetLastError=true)] private static extern int statx(int dir,string path,int flags,uint mask,IntPtr buffer);
    [DllImport("libc",SetLastError=true)] private static extern int flock(int fd,int operation);
    [DllImport("libc")] private static extern uint geteuid();
    [DllImport("libc",SetLastError=true)] internal static extern int renameat2(int oldDir,string oldName,int newDir,string newName,uint flags);
    [DllImport("libc",SetLastError=true)] internal static extern int fsync(int fd);
    [DllImport("libc",SetLastError=true)] internal static extern int fstatfs(int fd,IntPtr buffer);
    private static int Fd(SafeFileHandle handle)=>handle.DangerousGetHandle().ToInt32();
    private static SafeFileHandle Handle(int fd,string operation)
    {if(fd<0)throw new CandidateNamespaceIOException(operation,Marshal.GetLastPInvokeError());return new((IntPtr)fd,true);}
    internal static void PinExistingGenericAncestors(string absoluteRoot)
    {
        if(!OperatingSystem.IsLinux()||IntPtr.Size!=8)throw new PlatformNotSupportedException("Storage roots require supported Linux.");
        using var start=Handle(open("/",DirectoryFlags,0),"Open root");SafeFileHandle? current=null;
        try
        {
            foreach(var part in absoluteRoot.Split('/',StringSplitOptions.RemoveEmptyEntries))
            {
                var fd=openat(Fd(current??start),part,DirectoryFlags,0);
                if(fd<0&&Marshal.GetLastPInvokeError()==2)return; // missing tail, validated lexical components already
                var next=Handle(fd,"Pin generic storage ancestry");current?.Dispose();current=next;
            }
        }
        finally{current?.Dispose();}
    }
    private SafeFileHandle Root()
    {
        var current=Handle(open("/",DirectoryFlags,0),"Open root");
        try
        {
            foreach(var part in root.Split('/',StringSplitOptions.RemoveEmptyEntries))
            {var next=Handle(openat(Fd(current),part,DirectoryFlags,0),"Pin private root");current.Dispose();current=next;}
            DirectoryIdentity(current,true);calls.RequireSupportedFileSystem(Fd(current));return current;
        }
        catch{current.Dispose();throw;}
    }
    internal static ZipCandidateFileIdentity InspectNative(int dir,string leaf,int flags)
    {
        var buffer=Marshal.AllocHGlobal(256);
        try
        {
            const uint required=0x17FF; // BASIC_STATS|MNT_ID; statx fixed Linux ABI
            if(statx(dir,leaf,flags,required,buffer)!=0)throw new CandidateNamespaceIOException("Inspect pinned namespace identity",Marshal.GetLastPInvokeError());
            if((unchecked((uint)Marshal.ReadInt32(buffer))&required)!=required)throw new PlatformNotSupportedException("D1 identity fields unavailable.");
            return new(unchecked((uint)Marshal.ReadInt32(buffer,136)),unchecked((uint)Marshal.ReadInt32(buffer,140)),unchecked((ulong)Marshal.ReadInt64(buffer,32)),unchecked((ulong)Marshal.ReadInt64(buffer,144)),
                unchecked((uint)Marshal.ReadInt32(buffer,16)),unchecked((uint)Marshal.ReadInt32(buffer,20)),unchecked((uint)Marshal.ReadInt32(buffer,24)),unchecked((ushort)Marshal.ReadInt16(buffer,28)),Marshal.ReadInt64(buffer,40),
                Marshal.ReadInt64(buffer,112),unchecked((uint)Marshal.ReadInt32(buffer,120)),Marshal.ReadInt64(buffer,96),unchecked((uint)Marshal.ReadInt32(buffer,104)));
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    private ZipCandidateFileIdentity Inspect(SafeFileHandle handle)=>calls.Inspect(Fd(handle),"",0x1000); // AT_EMPTY_PATH
    private ZipCandidateDirectoryIdentity DirectoryIdentity(SafeFileHandle handle,bool requirePrivate)
    {
        var s=Inspect(handle);
        if((s.Mode&0xF000)!=0x4000||requirePrivate&&(s.UserId!=geteuid()||(s.Mode&0xFFF)!=0x1C0))throw new IOException("D1 directories must be owned, mode 0700, and not links.");
        return new(s.DeviceMajor,s.DeviceMinor,s.Inode,s.MountId);
    }
    private static void SameMount(ZipCandidateDirectoryIdentity a,ZipCandidateDirectoryIdentity b)
    {if(a.DeviceMajor!=b.DeviceMajor||a.DeviceMinor!=b.DeviceMinor||a.MountId!=b.MountId)throw new CandidateNamespaceIOException("D1 requires one mount; no fallback.",18);}
    private static bool SameDirectory(ZipCandidateDirectoryIdentity a,ZipCandidateDirectoryIdentity b)=>a.DeviceMajor==b.DeviceMajor&&a.DeviceMinor==b.DeviceMinor&&a.Inode==b.Inode;
    private static SafeFileHandle Child(SafeFileHandle parent,string name)=>Handle(openat(Fd(parent),name,DirectoryFlags,0),"Pin candidate directory");
    private async Task<SafeFileHandle> Gate(SafeFileHandle source,CancellationToken ct)
    {
        var gate=Handle(openat(Fd(source),".staging-gate",2|FileFlags,0),"Open existing staging gate");
        try
        {
            var s=Inspect(gate);Regular(s,long.MaxValue);
            if(s!=calls.Inspect(Fd(source),".staging-gate",0x100))throw new IOException("Staging gate identity changed.");
            var elapsed=Stopwatch.StartNew();
            while(flock(Fd(gate),2|4)!=0)
            {var error=Marshal.GetLastPInvokeError();if(error is not (4 or 11))throw new CandidateNamespaceIOException("Acquire staging gate",error);ct.ThrowIfCancellationRequested();if(elapsed.Elapsed>TimeSpan.FromSeconds(10))throw new TimeoutException("D1 staging gate timeout.");await Task.Delay(10,ct);}
            if(Inspect(gate)!=calls.Inspect(Fd(source),".staging-gate",0x100))throw new IOException("Locked staging gate was substituted.");
            return gate;
        }
        catch{gate.Dispose();throw;}
    }
    private static void Regular(ZipCandidateFileIdentity s,long maximum)
    {
        if((s.Mode&0xF000)!=0x8000||s.LinkCount!=1||s.UserId!=geteuid()||(s.Mode&0xFFF)!=0x180||s.Length<0||s.Length>maximum)
            throw new IOException("D1 requires an owned mode-0600 regular file with one link and bounded length.");
    }
    private bool Exists(SafeFileHandle dir,string leaf)
    {try{calls.Inspect(Fd(dir),leaf,0x100);return true;}catch(CandidateNamespaceIOException e)when(e.Error==2){return false;}}
    private static IDisposable Accept(IZipEffectBoundaryGuard guard,CancellationToken ct)
        =>guard.Accept(ct)??throw new UnauthorizedAccessException("D1 acceptance capability is missing.");
    private static SafeFileHandle OpenAccepted(int dir,string leaf,IZipEffectBoundaryGuard guard,CancellationToken ct)
    {
        SafeFileHandle? handle=null;
        try{using(Accept(guard,ct)){handle=Handle(openat(dir,leaf,FileFlags,0),"Open exact candidate");}return handle;}
        catch{handle?.Dispose();throw;}
    }
    private async Task<(FileStream Stream,ZipCandidateFileIdentity Identity,string Hash)> Read(SafeFileHandle dir,string leaf,IZipEffectBoundaryGuard guard,CancellationToken ct)
    {
        await guard.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();SafeFileHandle handle;
        handle=OpenAccepted(Fd(dir),leaf,guard,ct);
        FileStream? stream=null;
        try
        {
            var before=Inspect(handle);Regular(before,maximum);if(before.Length==0)throw new InvalidDataException("Empty encrypted candidate.");
            stream=new(handle,FileAccess.Read,81920,false);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[81920];long read=0;
            try
            {
                while(read<before.Length)
                {
                    await guard.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();int n;
                    using(Accept(guard,ct)){n=stream.Read(buffer,0,(int)Math.Min(buffer.Length,before.Length-read));if(n>0)hash.AppendData(buffer,0,n);}
                    if(n==0)throw new EndOfStreamException();read+=n;
                }
                if(Inspect(handle)!=before||calls.Inspect(Fd(dir),leaf,0x100)!=before)throw new IOException("Candidate changed during hashing.");
                return(stream,before,Convert.ToHexString(hash.GetHashAndReset()));
            }
            finally{CryptographicOperations.ZeroMemory(buffer);}
        }
        catch{if(stream is not null)stream.Dispose();else handle.Dispose();throw;}
    }
    internal async Task<CandidateFileInspection> InspectActiveAsync(ZipRetentionIdentity retention,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        if(retention.NamespaceId!=namespaceId)throw new InvalidDataException("D1 namespace changed.");
        using var rootHandle=Root();using var source=Child(rootHandle,"zip-candidates");using var gate=await Gate(source,ct);
        SameMount(DirectoryIdentity(rootHandle,true),DirectoryIdentity(source,true));
        var read=await Read(source,ZipCandidateCustodyEncoding.ActiveLeaf(retention.CreateOperationId),guard,ct);using var stream=read.Stream;
        return new(DirectoryIdentity(rootHandle,true),DirectoryIdentity(source,true),read.Identity,read.Hash);
    }
    private async Task<SafeFileHandle> Provision(SafeFileHandle parent,string leaf,IZipEffectBoundaryGuard guard,CancellationToken ct)
    {
        if(!Exists(parent,leaf))
        {
            await guard.RevalidateAsync(ct);using var accepted=Accept(guard,ct);
            if(mkdirat(Fd(parent),leaf,0x1C0)!=0)throw new CandidateNamespaceIOException("Create private custody directory",Marshal.GetLastPInvokeError());
            // This directory's accepted creation also finishes its barrier.
            calls.Flush(Fd(parent),CandidateDirectoryBarrier.Provisioning);
        }
        var child=Child(parent,leaf);
        try
        {
            SameMount(DirectoryIdentity(parent,true),DirectoryIdentity(child,true));calls.RequireSupportedFileSystem(Fd(child));
            await guard.RevalidateAsync(ct);using(Accept(guard,ct))calls.Flush(Fd(parent),CandidateDirectoryBarrier.Provisioning);
            return child;
        }
        catch{child.Dispose();throw;}
    }
    internal async Task<CandidateCustodyMoveObservation> MoveAsync(CandidateCleanupIntent intent,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        ZipCandidateCustodyEncoding.Validate(intent);
        if(intent.Anchors.Retention.NamespaceId!=namespaceId||intent.StoredEncryptedLength>maximum)throw new InvalidDataException("D1 namespace/length mismatch.");
        using var rootHandle=Root();using var source=Child(rootHandle,"zip-candidates");using var gate=await Gate(source,ct);
        var rootId=DirectoryIdentity(rootHandle,true);var sourceId=DirectoryIdentity(source,true);SameMount(rootId,sourceId);
        if(!SameDirectory(rootId,intent.RootIdentity)||!SameDirectory(sourceId,intent.SourceDirectoryIdentity))throw new IOException("D1 private root identity changed.");
        if(intent.InitiatingHistory.BootId==System.IO.File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()&&
            (rootId.MountId!=intent.RootIdentity.MountId||sourceId.MountId!=intent.SourceDirectoryIdentity.MountId))
            throw new IOException("D1 mount identity changed within this boot.");
        using var custody=await Provision(rootHandle,CandidateCustodyRootPolicy.Component,guard,ct);
        using var version=await Provision(custody,"v1",guard,ct);
        var slot=ZipCandidateCustodyEncoding.ActiveLeaf(intent.Anchors.Retention.CreateOperationId)[..^10];
        using var target=await Provision(version,slot,guard,ct);SameMount(sourceId,DirectoryIdentity(target,true));
        var active=ZipCandidateCustodyEncoding.ActiveLeaf(intent.Anchors.Retention.CreateOperationId);
        var leaf=ZipCandidateCustodyEncoding.Key(intent.Anchors,intent.CandidateHash)+".custody";
        var read=await Read(source,active,guard,ct);using var stream=read.Stream;
        if(read.Hash!=intent.CandidateHash||read.Identity.MountId!=sourceId.MountId||!ZipCandidateCustodyEncoding.Conserved(read.Identity,intent.SourceIdentity)||read.Identity.ChangedSeconds!=intent.SourceIdentity.ChangedSeconds||read.Identity.ChangedNanoseconds!=intent.SourceIdentity.ChangedNanoseconds)
            throw new InvalidDataException("D1 frozen candidate changed.");
        if(Exists(target,leaf))throw new CandidateNamespaceIOException("Custody destination already exists.",17);
        BeforeRename?.Invoke();await guard.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();
        if(calls.Inspect(Fd(source),active,0x100)!=read.Identity)throw new IOException("Candidate substituted before rename.");
        var errors=new List<Exception>();var moved=false;var sourceFd=Fd(source);var targetFd=Fd(target);
        var acceptance=Accept(guard,ct);
        try
        {
            var result=calls.RenameNoReplace(sourceFd,active,targetFd,leaf,out var error);
            if(result!=0)
            {
                if(error is 22 or 38 or 95)throw new PlatformNotSupportedException("D1 NOREPLACE unsupported; no fallback. errno="+error);
                throw new CandidateNamespaceIOException("D1 rename failed; no fallback.",error);
            }
            moved=true;
            try{AfterRename?.Invoke();}catch(Exception e){errors.Add(e);}
        }
        finally
        {
            if(moved)
            {
                // Non-revocable completion of the accepted mutation. Never check
                // cancellation between these two calls or on acceptance release.
                try{calls.Flush(Fd(target),CandidateDirectoryBarrier.Custody);}catch(Exception e){errors.Add(e);}
                try{calls.Flush(Fd(source),CandidateDirectoryBarrier.Source);}catch(Exception e){errors.Add(e);}
            }
            try{acceptance.Dispose();}catch(Exception e){errors.Add(e);}
        }
        if(errors.Count!=0)throw new IOException("D1 accepted mutation completion failed; retain custody for fresh verification.",new AggregateException(errors));
        var observed=calls.Inspect(Fd(target),leaf,0x100);Regular(observed,maximum);
        if(Exists(source,active)||!ZipCandidateCustodyEncoding.Conserved(intent.SourceIdentity,observed))throw new IOException("D1 custody identity contradiction.");
        return new(observed,DirectoryIdentity(target,true),intent.CustodyRelativePath);
    }
}
