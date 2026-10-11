using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipEntryPlanner
{
    private readonly ZipCentralDirectoryPreflight _preflight=new();
    internal Action? ArchiveConstructionStarting { get; set; }
    internal async Task<ZipParentSnapshot> AdmitAsync(IZipExtractionJournal journal,ZipParentSnapshot parent,
        IArtifactContentReadLease input,CancellationToken ct=default)
        => await AdmitCoreAsync(journal,parent,input,null,ct);
    public Task<ZipParentSnapshot> AdmitAsync(IZipExtractionJournal journal,ZipParentSnapshot parent,
        IArtifactContentReadLease input,ZipExecutionAttempt attempt,CancellationToken ct=default)
        => AdmitCoreAsync(journal,parent,input,attempt ?? throw new ArgumentNullException(nameof(attempt)),ct);
    private async Task<ZipParentSnapshot> AdmitCoreAsync(IZipExtractionJournal journal,ZipParentSnapshot parent,
        IArtifactContentReadLease input,ZipExecutionAttempt? attempt,CancellationToken ct)
    {
        await using var guard = attempt?.PlanBoundary(() => parent);
        using var linked = guard is null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct = linked.Token;
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        var existing = await journal.ReadAsync(parent.Binding.OperationId,ct) ?? throw new ZipFenceException();
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        if (existing.Fence != parent.Fence) throw new ZipFenceException();
        if (existing.Plan is not null) { if (guard is not null) await guard.FinishAsync(ct); return existing; }

        // The caller retains exclusive ownership of this admitted lease throughout.
        using(var access = ZipPhaseIIWork.Accept(guard,ct))
        if(input.ArtifactId.Value!=parent.Binding.Input.ContentId || input.Revision.Value!=parent.Binding.Input.Revision ||
            input.ReturnedLength!=parent.Binding.Input.Length || input.Content.Length!=input.ReturnedLength ||
            !Convert.ToHexString(SHA256.HashData(input.Content.Span)).Equals(parent.Binding.Input.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ZIP retained-input binding changed.");
        var current=await journal.ReadAsync(parent.Binding.OperationId,ct)??throw new ZipFenceException();
        if(current.Fence!=parent.Fence)throw new ZipFenceException();
        if(current.Plan is not null){if(guard is not null)await guard.FinishAsync(ct);return current;}
        var reservation = new ZipWorkReservation(Guid.NewGuid().ToString("N"),ZipWorkKind.Preflight,null,checked(parent.Binding.Input.Length+65539));
        parent = guard is null ? await journal.ReserveAsync(parent.Fence,reservation,ct)
            : await journal.ReserveAsync(parent.Fence,reservation,guard,ct);
        try
        {
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            ZipCentralDirectoryPreflightResult receipt;
            using(var access = ZipPhaseIIWork.Accept(guard,ct)) receipt=_preflight.Validate(input.Content.Span,parent.Binding.Input,ct);
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            Stream stream;
            using (var access = ZipPhaseIIWork.Accept(guard,ct)) stream=input.OpenReadStream();
            using var ownedStream = stream;
            if(stream.GetType()!=typeof(MemoryStream))throw new InvalidDataException("ZIP preflight requires the admitted same-array MemoryStream.");
            ArchiveConstructionStarting?.Invoke();
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            ZipArchive archive;
            using (var access = ZipPhaseIIWork.Accept(guard,ct)) archive=new ZipArchive(stream,ZipArchiveMode.Read,leaveOpen:true);
            using var ownedArchive = archive;
            if(archive.Entries.Count!=receipt.CentralEntries)throw new InvalidDataException("ZIP library/preflight entry count disagreement.");
            var entries=new List<ZipEntryPlan>(receipt.CentralEntries);var fileOrdinal=0;
            foreach(var entry in archive.Entries)
            {
                await ZipPhaseIIWork.CheckAsync(guard,ct);using var access=ZipPhaseIIWork.Accept(guard,ct);ValidatePath(entry.FullName);
                var directory=string.IsNullOrEmpty(entry.Name);
                entries.Add(new(entries.Count,directory?null:fileOrdinal++,entry.FullName,entry.FullName.Normalize(NormalizationForm.FormC),
                    entry.CompressedLength,entry.Length,entry.Crc32,entry.IsEncrypted,directory,
                    directory?null:Guid.NewGuid().ToString("N"),directory?null:Guid.NewGuid().ToString("N")));
            }
            var json=JsonSerializer.Serialize(receipt);
            var plan = new ZipPlan(ZipPlanBinding.Compute(json,entries),json,entries);
            parent = guard is null ? await journal.AdmitPlanAsync(parent.Fence,plan,ct) : await journal.AdmitPlanAsync(parent.Fence,plan,guard,ct);
            if(guard is not null) await guard.FinishAsync(ct);
            return parent;
        }
        catch(InvalidDataException) when(attempt is null)
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
