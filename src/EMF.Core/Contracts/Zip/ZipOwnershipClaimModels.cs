namespace EMF.Core.Contracts.Zip;

public enum ZipOwnershipPurpose { InitialExecution = 1, Recovery = 2 }

// Expected durable evidence and concurrency state, never authentication.
// Expiry is included because renewal intentionally leaves the fence unchanged.
public sealed record ZipOwnershipClaimRequest(ZipOwnershipPurpose Purpose,
    ZipParentAdmission ExpectedAdmission, ZipFence ExpectedFence,
    DateTimeOffset ExpectedOwnerUntil, string Owner, TimeSpan Duration)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Purpose)) throw new ArgumentOutOfRangeException(nameof(Purpose));
        ArgumentNullException.ThrowIfNull(ExpectedAdmission);
        ArgumentNullException.ThrowIfNull(ExpectedFence);
        ZipAdmissionValidation.Text(Owner);
        ZipAdmissionValidation.Text(ExpectedAdmission.OperationId);
        ZipAdmissionValidation.Binding(ExpectedAdmission.Binding);
        if (ExpectedFence.OperationId != ExpectedAdmission.OperationId)
            throw new InvalidDataException("ZIP ownership request operation changed.");
        if (Duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Duration));
    }
}
