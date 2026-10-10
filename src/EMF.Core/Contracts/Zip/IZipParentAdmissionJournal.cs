namespace EMF.Core.Contracts.Zip;

// Administrative persistence capability, supplied only to a governed host.
// No method authenticates an actor, generates approval, or activates stock routing.
public interface IZipParentAdmissionJournal
{
    Task<ZipParentAdmission> ResolveOrReserveAdmissionAsync(ZipAdmissionKey key, ZipAdmissionBinding binding, CancellationToken ct = default);
    Task<ZipParentAdmission?> ReadAdmissionAsync(string operationId, CancellationToken ct = default);
    Task<ZipParentAdmission?> ReadAdmissionByKeyAsync(ZipAdmissionKey key, CancellationToken ct = default);
    Task<IReadOnlyList<ZipParentAdmission>> ReadRecoveryAdmissionsAsync(string? afterOperationId, int limit, CancellationToken ct = default);
    Task<ZipParentAdmission> AppendAdmissionEventAsync(ZipParentAdmission expected, ZipAdmissionEvent evidence, CancellationToken ct = default);
    // Process-wide/cross-process serialization for pre-journal retention; not authority.
    Task<IDisposable> AcquireAdmissionExecutionAsync(string operationId, CancellationToken ct = default);
}
