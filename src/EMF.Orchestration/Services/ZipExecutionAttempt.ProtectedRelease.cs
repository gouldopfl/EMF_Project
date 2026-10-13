using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Services;

public sealed partial class ZipExecutionAttempt
{
    private int protectedReleaseInvocationUsed;

    internal void EnterProtectedReleaseInvocation()
    {
        if(Interlocked.Exchange(ref protectedReleaseInvocationUsed,1)!=0)
            throw new UnauthorizedAccessException("A new ZIP protected-release invocation requires a fresh execution attempt.");
    }

    internal ZipProtectedReleaseAuthorityRequest ProtectedReleaseRequest(ZipProtectedReleaseEvidence evidence)
    {
        Validate(evidence.Parent);
        var entry=evidence.Entry;
        if(entry.State!=ZipEntryState.Acknowledged||entry.Retained is null||entry.Ingestion is null)
            throw new InvalidDataException("ZIP protected release requires an exact acknowledged occurrence.");
        return new(admission,evidence.Parent.Binding,evidence.Parent.Fence,entry.Plan,entry.Retained,
            entry.Ingestion,evidence.Acknowledgement,evidence.ContainsId,evidence.DerivedFromId,
            evidence.Retention,actor,purpose,ValidUntilUtc);
    }

    internal ZipPhaseIIBoundary<ZipProtectedReleaseAuthorityRequest> ProtectedReleaseBoundary(
        Func<ZipProtectedReleaseEvidence> current)
        => Boundary(()=>ProtectedReleaseRequest(current()),
            (grant,request,ct)=>grant.AuthorizeProtectedReleaseAsync(request,ct));
}
