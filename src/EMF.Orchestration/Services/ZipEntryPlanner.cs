using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Services;

public sealed class ZipEntryPlanner
{
    private readonly ZipCentralDirectoryPreflight _preflight=new();
    internal Action? ArchiveConstructionStarting { get; set; }
    public async Task<ZipParentSnapshot> AdmitAsync(IZipExtractionJournal journal,ZipParentSnapshot parent,
        IArtifactContentReadLease input,CancellationToken ct=default)
    {
        // The caller retains exclusive ownership of this admitted lease throughout.
        if(input.ArtifactId.Value!=parent.Binding.Input.ContentId || input.Revision.Value!=parent.Binding.Input.Revision ||
            input.ReturnedLength!=parent.Binding.Input.Length || input.Content.Length!=input.ReturnedLength ||
            !Convert.ToHexString(SHA256.HashData(input.Content.Span)).Equals(parent.Binding.Input.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ZIP retained-input binding changed.");
        var current=await journal.ReadAsync(parent.Binding.OperationId,ct)??throw new ZipFenceException();
        if(current.Fence!=parent.Fence)throw new ZipFenceException();
        if(current.Plan is not null)return current;
        parent=await journal.ReserveAsync(parent.Fence,new(Guid.NewGuid().ToString("N"),ZipWorkKind.Preflight,null,
            checked(parent.Binding.Input.Length+65539)),ct);
        try
        {
            var receipt=_preflight.Validate(input.Content.Span,parent.Binding.Input,ct);
            using var stream=input.OpenReadStream();
            if(stream.GetType()!=typeof(MemoryStream))throw new InvalidDataException("ZIP preflight requires the admitted same-array MemoryStream.");
            ArchiveConstructionStarting?.Invoke();
            using var archive=new ZipArchive(stream,ZipArchiveMode.Read,leaveOpen:true);
            if(archive.Entries.Count!=receipt.CentralEntries)throw new InvalidDataException("ZIP library/preflight entry count disagreement.");
            var entries=new List<ZipEntryPlan>(receipt.CentralEntries);var fileOrdinal=0;
            foreach(var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();ValidatePath(entry.FullName);
                var directory=string.IsNullOrEmpty(entry.Name);
                entries.Add(new(entries.Count,directory?null:fileOrdinal++,entry.FullName,entry.FullName.Normalize(NormalizationForm.FormC),
                    entry.CompressedLength,entry.Length,entry.Crc32,entry.IsEncrypted,directory,
                    directory?null:Guid.NewGuid().ToString("N"),directory?null:Guid.NewGuid().ToString("N")));
            }
            var json=JsonSerializer.Serialize(receipt);
            return await journal.AdmitPlanAsync(parent.Fence,new(ZipPlanBinding.Compute(json,entries),json,entries),ct);
        }
        catch(InvalidDataException)
        {
            await journal.ReviewAsync(parent.Fence,"ZipAdmissionRejected",ct);throw;
        }
    }
    internal static void ValidatePath(string name)
    {
        if(string.IsNullOrEmpty(name) || name[0] is '/' or '\\' || name.Any(char.IsControl) || name.Contains(':') ||
            name.Split(['/', '\\']).Any(p=>p is "." or ".."))
            throw new InvalidDataException("Unsafe ZIP display name requires review.");
        // Names are display metadata only, never filesystem destination paths.
    }
}
