using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Services;

public sealed partial class ZipExecutionAttempt
{
    private int acknowledgementInvocationUsed;

    internal void EnterAcknowledgementInvocation()
    {
        if (Interlocked.Exchange(ref acknowledgementInvocationUsed, 1) != 0)
            throw new UnauthorizedAccessException("A new ZIP acknowledgement invocation requires a fresh execution attempt.");
    }

    internal ZipPhaseIIBoundary<ZipAcknowledgementAuthorityRequest> AcknowledgementBoundary(
        Func<ZipParentSnapshot> current, int ordinal)
        => Boundary(() =>
        {
            var parent = current(); Validate(parent);
            var entry = parent.Entries.Single(e => e.Plan.FileOrdinal == ordinal);
            if (entry.Ingestion is null || entry.Retained is null)
                throw new InvalidDataException("ZIP acknowledgement requires exact ingestion and retained evidence.");
            return new ZipAcknowledgementAuthorityRequest(admission, parent.Binding, parent.Fence,
                entry.Plan, entry.Retained, entry.Ingestion, actor, purpose, ValidUntilUtc);
        }, (grant, request, ct) => grant.AuthorizeAcknowledgementAsync(request, ct));
}
