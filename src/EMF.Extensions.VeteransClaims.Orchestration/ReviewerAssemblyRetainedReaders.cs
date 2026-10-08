using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using System.Security.Cryptography;
using static EMF.Extensions.VeteransClaims.Orchestration.ReviewerAssemblyRetainedReaderBudget;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Internal scoped synthetic facades. Returned controlled bytes borrow session lifetime.
// Callers must join consumption before disposal. No production composition is registered.
internal sealed class ReviewerAssemblyRetainedReaders : IDisposable, IAsyncDisposable
{
    private readonly ReviewerAssemblyInputOwner owner;
    private readonly ReviewerAssemblyFoundationLimits foundation;
    private readonly ReviewerAssemblyRetainedReaderBudget budget;
    private readonly TaskCompletionSource closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing, closed, parentStarted, parentDone;
    private int pending, dependents;
    private ReviewerAssemblyRetainedReaders(ReviewerAssemblyInputOwner owner, ReviewerAssemblyFoundationLimits foundation,
        ReviewerAssemblyRetainedReaderBudget budget) { this.owner=owner;this.foundation=foundation;this.budget=budget; }
    internal static ReviewerAssemblyRetainedReaders FromEncoded(ReadOnlyMemory<byte> encoded,
        ReviewerAssemblyFoundationLimits foundation, ReviewerAssemblyRetainedReaderLimits limits, CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(encoded.Length==0 || encoded.Length>foundation.MaximumEncodedBytes)throw Error("EncodedCapacity");
        limits.Validate(foundation,encoded.Length);
        long live=ConstructionLive(encoded.Length,foundation,limits),spent=ConstructionWork(encoded.Length,foundation,limits);
        var budget=new ReviewerAssemblyRetainedReaderBudget(limits,live,spent);
        ReviewerAssemblyInputOwner? owner=null;
        try
        {
            owner=ReviewerAssemblyInputOwner.FromEncoded(encoded,ReviewerAssemblyDraftIdentity.Foundation,foundation,ct);
            ct.ThrowIfCancellationRequested();
            var result=new ReviewerAssemblyRetainedReaders(owner,foundation,budget);
            lock(budget.Gate)budget.ReleaseLive(null,checked(live-FixedContext(limits)-foundation.MaximumEncodedBytes));
            return result;
        }
        catch {owner?.Dispose();lock(budget.Gate)budget.ReleaseLive(null,budget.Live);throw;}
    }
    internal ReviewerAssemblyRetainedAccounting Accounting {get{lock(budget.Gate)return budget.Snapshot();}}
    internal Session Acquire(CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();Account account;
        long live=SessionLive(foundation,budget.Limits),work=SessionWork(foundation,budget.Limits);
        lock(budget.Gate)
        {
            ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);
            if(budget.Sessions>=budget.Limits.MaximumSessions)throw Error("SessionCapacity");
            budget.ReserveLive(null,live);
            try
            {
                budget.Spend(null,work);account=new(){Live=live,Spent=work};
                budget.ReserveWork(account,budget.Limits.MaximumSessionAdmissionWork);
            }
            catch{budget.ReleaseLive(null,live);throw;}
            budget.Sessions=checked(budget.Sessions+1);pending=checked(pending+1);dependents=checked(dependents+1);
        }
        IReviewerAssemblyInputCopyLease? lease=null;Meter? meter=null;
        try
        {
            lease=owner.Acquire(ct);var graph=lease.ReadCopy(ct);
            meter=new(budget,account,budget.Limits.MaximumSessionAdmissionWork,ct);
            meter.Tick(2);
            var lookup=new Dictionary<string,int>(foundation.MaximumMembers,StringComparer.Ordinal);
            var frames=new ReviewerAssemblyRetainedReaderAdmission.Frame[checked(budget.Limits.MaximumMetadataDepth+1)];
            var entries=ReviewerAssemblyRetainedReaderAdmission.BuildIndex(graph,lookup,frames,foundation,budget.Limits,meter,ct);
            ct.ThrowIfCancellationRequested();
            var session=new Session(this,lease,graph,entries,lookup,frames,account);
            lock(budget.Gate){budget.ReleaseWork(account,checked(budget.Limits.MaximumSessionAdmissionWork-meter.Used));pending=checked(pending-1);}
            TryFinishClose();return session;
        }
        catch
        {
            lease?.Dispose();
            lock(budget.Gate)
            {
                budget.ReleaseWork(account,account.Reserved);budget.ReleaseLive(account,account.Live);
                budget.Sessions=checked(budget.Sessions-1);pending=checked(pending-1);dependents=checked(dependents-1);
            }
            TryFinishClose();throw;
        }
    }
    private void EndSession(Account account)
    {
        lock(budget.Gate){budget.ReleaseLive(account,account.Live);budget.Sessions=checked(budget.Sessions-1);dependents=checked(dependents-1);}
        TryFinishClose();
    }
    public void Dispose(){lock(budget.Gate)closing=true;TryFinishClose();}
    public async ValueTask DisposeAsync(){Dispose();await closeCompletion.Task.ConfigureAwait(false);}
    private void TryFinishClose()
    {
        bool closeParent=false,finish=false;
        lock(budget.Gate)
        {
            if(!closing)return;
            if(pending==0&&!parentStarted){parentStarted=true;closeParent=true;}
        }
        if(closeParent){owner.Dispose();lock(budget.Gate)parentDone=true;}
        lock(budget.Gate)
        {
            if(parentDone&&dependents==0&&!closed)
            {budget.ReleaseLive(null,checked(FixedContext(budget.Limits)+foundation.MaximumEncodedBytes));closed=true;finish=true;}
        }
        if(finish)closeCompletion.TrySetResult();
    }
    internal sealed class Session : IDisposable,IAsyncDisposable, IEvidencePackageRepository, IClaimIssueRepository, IClaimRepository, IEvidenceRepository, IEvidenceClassificationRepository, IMedicalLiteratureRepository, IConditionRepository, IMedicationRepository, ISourceClarificationRepository, IClinicalProgressionRepository, IServiceConnectionRepository, IClaimIssueAdjudicationDetailsService, IVeteransReviewerRegulatoryTextProvider, IArtifactContentStore, IArtifactTextExtractor, IArtifactPrintRenderer
    {
        private readonly ReviewerAssemblyRetainedReaders context;
        private readonly ReviewerAssemblyRetainedReaderBudget budget;
        private readonly Account account;
        private IReviewerAssemblyInputCopyLease? lease;
        private ReviewerAssemblyCaptureManifest? graph;
        private ReviewerAssemblyRetainedReaderAdmission.Entry[]? entries;
        private Dictionary<string,int>? lookup;
        private readonly ReviewerAssemblyRetainedReaderAdmission.Frame[] frames;
        private readonly Dictionary<byte[],byte[]?> buffers;
        private readonly Func<byte[],byte[]> copyBuffer;
        private readonly TaskCompletionSource disposal=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Request? active,waiter;
        private bool closing,closed,cleanupStarted;
        private long generation;
        // Test-only observers run outside every ownership lock. They must not synchronously
        // join disposal of the projection they are observing. No foundation hooks are installed.
        internal Func<string,Task>? ProjectionCheckpoint {get;set;}
        internal Action<byte[]>? BufferAllocated {get;set;}
        // Fixed session256 covers the sole active control record/meter/completion protocol;
        // the sole waiter256 includes its installation/cancellation/completion state.
        private sealed class Request
        {
            internal readonly RetainedQuery Query;internal readonly int Index;internal readonly CancellationToken Token;
            internal readonly long Generation;
            internal readonly TaskCompletionSource<object?> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource? Installation;
            internal byte[]? BufferHead;
            internal CancellationTokenRegistration Registration;
            internal bool Registered,Terminal,WasWaiter;
            internal long Used;
            internal Request(RetainedQuery query,int index,CancellationToken token,long generation)
            {Query=query;Index=index;Token=token;Generation=generation;}
        }
        internal Session(ReviewerAssemblyRetainedReaders context,IReviewerAssemblyInputCopyLease lease,
            ReviewerAssemblyCaptureManifest graph,ReviewerAssemblyRetainedReaderAdmission.Entry[] entries,
            Dictionary<string,int> lookup,ReviewerAssemblyRetainedReaderAdmission.Frame[] frames,Account account)
        {
            this.context=context;budget=context.budget;this.lease=lease;this.graph=graph;this.entries=entries;
            this.lookup=lookup;this.frames=frames;this.account=account;
            buffers=new(budget.Limits.MaximumRegisteredBuffersPerSession,ReferenceEqualityComparer.Instance);
            copyBuffer=CopyBuffer;
        }
        internal ReviewerAssemblyRetainedAccounting Accounting {get{lock(budget.Gate)return budget.Snapshot(account);}}
        private async Task<T> Query<T>(RetainedQuery query,string? key,CancellationToken ct) where T:class
        {var value=await Enter(query,key,ct).ConfigureAwait(false);return value as T??throw Error("ProjectionType");}
        private async Task<T?> QueryOptional<T>(RetainedQuery query,string? key,CancellationToken ct) where T:class
        {var value=await Enter(query,key,ct).ConfigureAwait(false);return value is null?null:value as T??throw Error("ProjectionType");}
        private Task<object?> Enter(RetainedQuery query,string? key,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Request request;bool queued;
            lock(budget.Gate)
            {
                ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);
                budget.Spend(account,1);
                if(string.IsNullOrEmpty(key)||key.Length>128)throw Error("QueryIdentity");
                budget.Spend(account,checked(3L*key.Length+128L*graph!.Members.Rows.Length));
                bool valid=true;
                foreach(char ch in key)if(!(char.IsAsciiLetterOrDigit(ch)||ch is '-' or '_' or '.' or ':'))valid=false;
                int index=-1;
                if(query is RetainedQuery.Artifact or RetainedQuery.Provenance or RetainedQuery.Classifications or RetainedQuery.Relationships or
                    RetainedQuery.Literature or RetainedQuery.Source or RetainedQuery.Text or RetainedQuery.Print)
                    valid&=lookup!.TryGetValue(key,out index);
                else
                {
                    string expected=query switch
                    {
                        RetainedQuery.Package or RetainedQuery.Pending or RetainedQuery.Snapshot or RetainedQuery.Members=>graph.Package.Rows[0].Id,
                        RetainedQuery.Issue or RetainedQuery.Conditions or RetainedQuery.Clarifications or RetainedQuery.Progression=>graph.Issue.Rows[0].Id,
                        RetainedQuery.Claim=>graph.Claim.Rows[0].Id,RetainedQuery.Ledgers=>graph.Claim.Rows[0].VeteranId,
                        _=>throw new NotSupportedException()
                    };
                    valid&=string.Equals(key,expected,StringComparison.Ordinal);
                }
                ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);
                if(!valid)throw Error("QueryIdentity");
                if(waiter is not null)throw new InvalidOperationException("SessionOverlap");
                queued=active is not null;
                long nextGeneration;try{nextGeneration=checked(generation+1);}catch(OverflowException){throw Error("ArithmeticOverflow");}
                bool workReserved=false,liveReserved=false,queueReserved=false;
                try
                {
                    budget.ReserveWork(account,budget.Limits.MaximumWorkPerQuery);workReserved=true;
                    if(queued){budget.ReserveLive(account,256);liveReserved=true;budget.ReserveQueue(account);queueReserved=true;budget.Spend(account,1);}
                    request=new(query,index,ct,nextGeneration){WasWaiter=queued,Installation=queued?new(TaskCreationOptions.RunContinuationsAsynchronously):null};generation=nextGeneration;
                }
                catch
                {
                    if(queueReserved)budget.ReleaseQueue(account);
                    if(liveReserved)budget.ReleaseLive(account,256);
                    if(workReserved)budget.ReleaseWork(account,budget.Limits.MaximumWorkPerQuery);throw;
                }
                if(queued)waiter=request;else active=request;
            }
            if(queued)InstallWaiter(request);else _=Process(request);
            return request.Completion.Task;
        }
        private void InstallWaiter(Request request)
        {
            try
            {
                request.Registration=request.Token.UnsafeRegister(_=>{_=CancelWaiter(request);},null);
                lock(budget.Gate)request.Registered=true;
            }
            catch(Exception ex){_=CancelWaiter(request,ex);}
            finally{request.Installation!.TrySetResult();}
            Promote();
        }
        private async Task CancelWaiter(Request request,Exception? failure=null)
        {
            Exception error;
            lock(budget.Gate)
            {
                if(waiter!=request||request.Terminal)return;
                if(failure is null&&!closing&&!request.Token.IsCancellationRequested)return;
                request.Terminal=true;
                error=request.Token.IsCancellationRequested?new OperationCanceledException(request.Token):failure??new ObjectDisposedException(nameof(Session));
            }
            await request.Installation!.Task.ConfigureAwait(false);
            await request.Registration.DisposeAsync().ConfigureAwait(false);
            lock(budget.Gate)
            {
                if(waiter==request)waiter=null;
                budget.ReleaseQueue(account);budget.ReleaseLive(account,256);
                budget.ReleaseWork(account,checked(budget.Limits.MaximumWorkPerQuery-request.Used));
            }
            request.Completion.TrySetException(error);TryCleanup();Promote();
        }
        private void Promote()
        {
            Request? next=null,cancel=null;
            lock(budget.Gate)
            {
                if(active is null&&waiter is {Registered:true,Terminal:false} candidate)
                {
                    if(closing||candidate.Token.IsCancellationRequested)cancel=candidate;
                    else{next=candidate;waiter=null;active=candidate;budget.ReleaseQueue(account);}
                }
            }
            if(cancel is not null)_=CancelWaiter(cancel);
            if(next is not null)_=Process(next);
        }
        private async Task Process(Request request)
        {
            Ledger ledger=default;bool outputReserved=false;object? result=null;Exception? error=null;
            try
            {
                if(request.WasWaiter)
                {
                    await request.Installation!.Task.ConfigureAwait(false);
                    await request.Registration.DisposeAsync().ConfigureAwait(false);
                    lock(budget.Gate)budget.ReleaseLive(account,256);
                }
                if(ProjectionCheckpoint is { } before)await before("BeforePrecount").ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                var meter=new Meter(budget,account,budget.Limits.MaximumPrecountWorkPerQuery,request.Token);
                try{ledger=ReviewerAssemblyRetainedReaderAdmission.Count(request.Query,graph!,request.Index<0?null:entries![request.Index],frames,budget.Limits,meter,request.Token);}
                finally{request.Used=meter.Used;}
                long work=ledger.ProjectionWork;
                if(work>budget.Limits.MaximumProjectionWorkPerQuery)throw Error("WorkCapacity");
                lock(budget.Gate)
                {
                    request.Token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);
                    budget.ReserveOutput(account,ledger);outputReserved=true;
                    budget.Consume(account,work);request.Used=checked(request.Used+work);
                }
                if(ProjectionCheckpoint is { } project)await project("BeforeProject").ConfigureAwait(false);
                result=ReviewerAssemblyRetainedReaderAdmission.Project(request.Query,graph!,request.Index<0?null:entries![request.Index],frames,copyBuffer,request.Token);
                if(ProjectionCheckpoint is { } after)await after("AfterProject").ConfigureAwait(false);
            }
            catch(Exception ex){error=ex is OverflowException?Error("ArithmeticOverflow"):ex;}
            lock(budget.Gate)
            {
                if(request.Token.IsCancellationRequested)error=new OperationCanceledException(request.Token);
                else if(closing)error=new ObjectDisposedException(nameof(Session));
                request.Terminal=true; // terminal outcome linearizes before a subsequent close
            }
            if(error is not null&&outputReserved)
            {
                ClearProjection(request);result=null;
                lock(budget.Gate){budget.ReleaseLive(account,ledger.Live);account.Buffers=checked(account.Buffers-ledger.Buffers);}
            }
            lock(budget.Gate)
            {budget.ReleaseWork(account,checked(budget.Limits.MaximumWorkPerQuery-request.Used));active=null;}
            if(error is null)request.Completion.TrySetResult(result);else request.Completion.TrySetException(error);
            Promote();TryCleanup();
        }
        private byte[] CopyBuffer(byte[] source)
        {
            var request=active??throw new InvalidOperationException("Missing projection authority.");
            request.Token.ThrowIfCancellationRequested();var result=new byte[source.Length];
            try
            {
                buffers.Add(result,request.BufferHead);request.BufferHead=result; // prepaid identity entry, including bounded cleanup link
                BufferAllocated?.Invoke(result);
                for(int offset=0;offset<source.Length;)
                {request.Token.ThrowIfCancellationRequested();int count=Math.Min(4096,source.Length-offset);source.AsSpan(offset,count).CopyTo(result.AsSpan(offset,count));offset=checked(offset+count);}
                request.Token.ThrowIfCancellationRequested();return result;
            }
            catch{if(!buffers.ContainsKey(result))CryptographicOperations.ZeroMemory(result);throw;}
        }
        private void ClearProjection(Request request)
        {
            // Only this projection's owned chain is visited. No scan of retained successful outputs.
            // The funded binary-node/registry lifecycle includes this bounded cleanup step.
            while(request.BufferHead is { } buffer)
            {
                if(!buffers.Remove(buffer,out var previous))throw new InvalidOperationException("Missing owned buffer.");
                CryptographicOperations.ZeroMemory(buffer);request.BufferHead=previous;
            }
        }
        public void Dispose(){BeginClose();disposal.Task.GetAwaiter().GetResult();}
        public async ValueTask DisposeAsync(){BeginClose();await disposal.Task.ConfigureAwait(false);}
        private void BeginClose()
        {Request? pending;lock(budget.Gate){closing=true;pending=waiter;}if(pending is not null)_=CancelWaiter(pending);TryCleanup();}
        private void TryCleanup()
        {
            lock(budget.Gate)
            {if(!closing||closed||cleanupStarted||active is not null||waiter is not null)return;cleanupStarted=true;}
            foreach(var pair in buffers)CryptographicOperations.ZeroMemory(pair.Key);buffers.Clear();
            Array.Clear(frames);entries=null;lookup=null;graph=null;lease?.Dispose();lease=null;
            context.EndSession(account);
            lock(budget.Gate){account.Buffers=0;closed=true;}
            disposal.TrySetResult();
        }
        private Task<T> Unsupported<T>(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();lock(budget.Gate){ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);}throw new NotSupportedException();}
        private Task Unsupported(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();lock(budget.Gate){ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(closing,this);}throw new NotSupportedException();}
        Task<ReviewerPackageSnapshotRead> IEvidencePackageRepository.ReadReviewerSnapshotAsync(EvidencePackageId packageId, CancellationToken cancellationToken) => Query<ReviewerPackageSnapshotRead>(RetainedQuery.Pending,packageId.Value,cancellationToken);
        Task<ReviewerPackageSnapshot?> IEvidencePackageRepository.GetReviewerSnapshotAsync(EvidencePackageId packageId, CancellationToken cancellationToken) => QueryOptional<ReviewerPackageSnapshot>(RetainedQuery.Snapshot,packageId.Value,cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerSnapshotAsync(ReviewerPackageSnapshot snapshot, EvidencePackageDetails expectedMembership, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ReviewerPackagePresentationSnapshot?> IEvidencePackageRepository.GetReviewerPresentationAsync(EvidencePackageId packageId, CancellationToken cancellationToken) => Unsupported<ReviewerPackagePresentationSnapshot?>(cancellationToken);
        Task IEvidencePackageRepository.CreateReviewerPresentationVersionAsync(ReviewerPackageSnapshot snapshot, EvidencePackageDetails membership, ReviewerPackagePresentationSnapshot presentation, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.RecoverReviewerPresentationVersionAsync(ReviewerPackageSnapshot snapshot, EvidencePackageDetails membership, ReviewerPackagePresentationSnapshot presentation, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerPresentationAsync(ReviewerPackagePresentationSnapshot presentation, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ReviewerPackageFrozenPdf?> IEvidencePackageRepository.GetReviewerFrozenPdfAsync(EvidencePackageId packageId, CancellationToken cancellationToken) => Unsupported<ReviewerPackageFrozenPdf?>(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerFrozenPdfAsync(ReviewerPackageFrozenPdf pdf, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ReviewerPackageOutputProvenance>> IEvidencePackageRepository.GetReviewerOutputProvenanceAsync(EvidencePackageId packageId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewerPackageOutputProvenance>>(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerOutputProvenanceAsync(ReviewerPackageOutputProvenance provenance, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ReviewerPackageOutputBuildProvenance>> IEvidencePackageRepository.GetReviewerOutputBuildProvenanceAsync(string provenanceId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewerPackageOutputBuildProvenance>>(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerOutputBuildProvenanceAsync(ReviewerPackageOutputBuildProvenance provenance, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerOutputWithBuildProvenanceAsync(ReviewerPackageOutputProvenance outputProvenance, ReviewerPackageOutputBuildProvenance buildProvenance, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.SaveReviewerOutputWithBuildProvenanceAndManifestAsync(ReviewerPackageOutputProvenance outputProvenance, ReviewerPackageOutputBuildProvenance buildProvenance, string buildManifestJson, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.AddEvidencePackageAsync(EvidencePackage evidencePackage, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.AddEvidencePackageAsync(EvidencePackage evidencePackage, IReadOnlyCollection<EvidencePackageArtifact> artifacts, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<EvidencePackage?> IEvidencePackageRepository.GetEvidencePackageAsync(EvidencePackageId evidencePackageId, CancellationToken cancellationToken) => QueryOptional<EvidencePackage>(RetainedQuery.Package,evidencePackageId.Value,cancellationToken);
        Task<IReadOnlyList<EvidencePackage>> IEvidencePackageRepository.GetEvidencePackagesAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidencePackage>>(cancellationToken);
        Task IEvidencePackageRepository.AddEvidencePackageArtifactAsync(EvidencePackageArtifact artifact, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidencePackageRepository.SetReviewerPageSelectionAsync(EvidencePackageId evidencePackageId, ArtifactId artifactId, string? reviewerPageSelection, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<EvidencePackageArtifact>> IEvidencePackageRepository.GetEvidencePackageArtifactsAsync(EvidencePackageId evidencePackageId, CancellationToken cancellationToken) => Query<IReadOnlyList<EvidencePackageArtifact>>(RetainedQuery.Members,evidencePackageId.Value,cancellationToken);
        Task IClaimIssueRepository.AddClaimIssueAsync(ClaimIssue claimIssue, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ClaimIssue?> IClaimIssueRepository.GetClaimIssueAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => QueryOptional<ClaimIssue>(RetainedQuery.Issue,claimIssueId.Value,cancellationToken);
        Task<IReadOnlyList<ClaimIssue>> IClaimIssueRepository.GetClaimIssuesAsync(ClaimId claimId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ClaimIssue>>(cancellationToken);
        Task IClaimRepository.AddClaimAsync(Claim claim, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<Claim?> IClaimRepository.GetClaimAsync(ClaimId claimId, CancellationToken cancellationToken) => QueryOptional<Claim>(RetainedQuery.Claim,claimId.Value,cancellationToken);
        Task<IReadOnlyList<Claim>> IClaimRepository.GetClaimsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<Claim>>(cancellationToken);
        Task IEvidenceRepository.AddArtifactAsync(Artifact artifact, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidenceRepository.AddRelationshipAsync(Relationship relationship, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<Artifact?> IEvidenceRepository.GetArtifactAsync(ArtifactId artifactId, CancellationToken cancellationToken) => QueryOptional<Artifact>(RetainedQuery.Artifact,artifactId.Value,cancellationToken);
        Task<EvidenceAggregate?> IEvidenceRepository.GetEvidenceAggregateAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Unsupported<EvidenceAggregate?>(cancellationToken);
        Task<Artifact?> IEvidenceRepository.FindArtifactAsync(string source, ContentFingerprint fingerprint, CancellationToken cancellationToken) => Unsupported<Artifact?>(cancellationToken);
        Task<IReadOnlyList<Artifact>> IEvidenceRepository.GetArtifactsByMetadataAsync(string key, string value, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<Artifact>>(cancellationToken);
        Task IEvidenceRepository.MergeArtifactMetadataAsync(ArtifactId artifactId, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<Relationship>> IEvidenceRepository.GetRelationshipsAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Query<IReadOnlyList<Relationship>>(RetainedQuery.Relationships,artifactId.Value,cancellationToken);
        Task IEvidenceRepository.AddProvenanceAsync(Provenance provenance, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidenceRepository.AddArtifactWithProvenanceAsync(Artifact artifact, Provenance provenance, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IEvidenceRepository.AddArtifactWithProvenanceAndRelationshipsAsync(Artifact artifact, Provenance provenance, IReadOnlyCollection<Relationship> relationships, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<Provenance>> IEvidenceRepository.GetProvenanceAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Query<IReadOnlyList<Provenance>>(RetainedQuery.Provenance,artifactId.Value,cancellationToken);
        Task IEvidenceClassificationRepository.AddEvidenceClassificationAsync(EvidenceClassification classification, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<EvidenceClassification?> IEvidenceClassificationRepository.GetEvidenceClassificationAsync(EvidenceClassificationId classificationId, CancellationToken cancellationToken) => Unsupported<EvidenceClassification?>(cancellationToken);
        Task<IReadOnlyList<EvidenceClassification>> IEvidenceClassificationRepository.GetEvidenceClassificationsAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Query<IReadOnlyList<EvidenceClassification>>(RetainedQuery.Classifications,artifactId.Value,cancellationToken);
        Task<EvidenceClassification?> IEvidenceClassificationRepository.FindEvidenceClassificationAsync(ArtifactId artifactId, ClaimIssueId? claimIssueId, string classification, CancellationToken cancellationToken) => Unsupported<EvidenceClassification?>(cancellationToken);
        Task IEvidenceClassificationRepository.AddEvidenceClassificationRequirementAsync(EvidenceClassificationRequirement association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<EvidenceClassificationRequirement>> IEvidenceClassificationRepository.GetEvidenceClassificationRequirementsAsync(EvidenceClassificationId classificationId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidenceClassificationRequirement>>(cancellationToken);
        Task<IReadOnlyList<EvidenceClassification>> IEvidenceClassificationRepository.GetEvidenceClassificationsAsync(RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidenceClassification>>(cancellationToken);
        Task<IReadOnlyList<EvidenceClassification>> IEvidenceClassificationRepository.GetEvidenceClassificationsAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidenceClassification>>(cancellationToken);
        Task IEvidenceClassificationRepository.AddEvidenceClassificationFindingAsync(EvidenceClassificationFinding association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<EvidenceClassificationFinding>> IEvidenceClassificationRepository.GetEvidenceClassificationFindingsAsync(EvidenceClassificationId classificationId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidenceClassificationFinding>>(cancellationToken);
        Task<IReadOnlyList<EvidenceClassification>> IEvidenceClassificationRepository.GetEvidenceClassificationsAsync(FindingId findingId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<EvidenceClassification>>(cancellationToken);
        Task<ServiceConnectionBasisId> IMedicalLiteratureRepository.ResolveServiceConnectionBasisAsync(RequirementId requirementId, ServiceConnectionBasisId? serviceConnectionBasisId, CancellationToken cancellationToken) => Unsupported<ServiceConnectionBasisId>(cancellationToken);
        Task<IReadOnlyList<RequirementMedicalLiterature>> IMedicalLiteratureRepository.GetRequirementMedicalLiteratureAsync(ServiceConnectionBasisId serviceConnectionBasisId, RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RequirementMedicalLiterature>>(cancellationToken);
        Task<IReadOnlyList<RequirementMedicalLiterature>> IMedicalLiteratureRepository.GetActiveRequirementMedicalLiteratureAsync(ServiceConnectionBasisId serviceConnectionBasisId, RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RequirementMedicalLiterature>>(cancellationToken);
        Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>> IMedicalLiteratureRepository.GetReviewedClassificationsAsync(ServiceConnectionBasisId serviceConnectionBasisId, RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewedMedicalLiteratureClassification>>(cancellationToken);
        Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>> IMedicalLiteratureRepository.GetReviewedClassificationsAsync(ServiceConnectionBasisId serviceConnectionBasisId, ArtifactId artifactId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewedMedicalLiteratureClassification>>(cancellationToken);
        Task IMedicalLiteratureRepository.AddMedicalLiteratureSourceAsync(MedicalLiteratureSource source, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<MedicalLiteratureSource?> IMedicalLiteratureRepository.GetMedicalLiteratureSourceAsync(MedicalLiteratureSourceId sourceId, CancellationToken cancellationToken) => Unsupported<MedicalLiteratureSource?>(cancellationToken);
        Task IMedicalLiteratureRepository.AddRequirementMedicalLiteratureAsync(RequirementMedicalLiterature literature, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<RequirementMedicalLiterature>> IMedicalLiteratureRepository.GetRequirementMedicalLiteratureAsync(RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RequirementMedicalLiterature>>(cancellationToken);
        Task<IReadOnlyList<RequirementMedicalLiterature>> IMedicalLiteratureRepository.GetActiveRequirementMedicalLiteratureAsync(RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RequirementMedicalLiterature>>(cancellationToken);
        Task IMedicalLiteratureRepository.AddMedicalLiteratureSourceArtifactAsync(MedicalLiteratureSourceArtifact association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ArtifactId>> IMedicalLiteratureRepository.GetArtifactIdsAsync(MedicalLiteratureSourceId sourceId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ArtifactId>>(cancellationToken);
        Task<IReadOnlyList<MedicalLiteratureSourceId>> IMedicalLiteratureRepository.GetMedicalLiteratureSourceIdsAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Query<IReadOnlyList<MedicalLiteratureSourceId>>(RetainedQuery.Literature,artifactId.Value,cancellationToken);
        Task IMedicalLiteratureRepository.AddReviewedClassificationAsync(ReviewedMedicalLiteratureClassification classification, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IMedicalLiteratureRepository.AddReviewedClassificationsAsync(IReadOnlyList<ReviewedMedicalLiteratureClassification> classifications, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IMedicalLiteratureRepository.SupersedeReviewedClassificationsAsync(string supersededCorrelationId, IReadOnlyList<ReviewedMedicalLiteratureClassification> classifications, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IMedicalLiteratureRepository.SupersedeReviewedClassificationAsync(string supersededCorrelationId, ReviewedMedicalLiteratureClassification classification, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>> IMedicalLiteratureRepository.GetReviewedClassificationsAsync(RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewedMedicalLiteratureClassification>>(cancellationToken);
        Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>> IMedicalLiteratureRepository.GetReviewedClassificationsAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ReviewedMedicalLiteratureClassification>>(cancellationToken);
        Task IMedicalLiteratureRepository.UpsertReviewerTextAsync(MedicalLiteratureReviewerText reviewerText, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<MedicalLiteratureReviewerText?> IMedicalLiteratureRepository.GetReviewerTextAsync(MedicalLiteratureSourceId sourceId, ArtifactId artifactId, CancellationToken cancellationToken) => Unsupported<MedicalLiteratureReviewerText?>(cancellationToken);
        Task IConditionRepository.AddClaimedConditionAsync(ClaimedCondition claimedCondition, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ClaimedCondition?> IConditionRepository.GetClaimedConditionAsync(ClaimedConditionId claimedConditionId, CancellationToken cancellationToken) => Unsupported<ClaimedCondition?>(cancellationToken);
        Task<IReadOnlyList<ClaimedCondition>> IConditionRepository.GetClaimedConditionsAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Query<IReadOnlyList<ClaimedCondition>>(RetainedQuery.Conditions,claimIssueId.Value,cancellationToken);
        Task IConditionRepository.AddMedicalConditionAsync(MedicalCondition medicalCondition, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<MedicalCondition?> IConditionRepository.GetMedicalConditionAsync(MedicalConditionId medicalConditionId, CancellationToken cancellationToken) => Unsupported<MedicalCondition?>(cancellationToken);
        Task IConditionRepository.AddVeteranMedicalConditionAsync(VeteranMedicalCondition association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicalConditionId>> IConditionRepository.GetMedicalConditionIdsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicalConditionId>>(cancellationToken);
        Task<IReadOnlyList<VeteranId>> IConditionRepository.GetVeteranIdsAsync(MedicalConditionId medicalConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<VeteranId>>(cancellationToken);
        Task IConditionRepository.AddClaimedConditionMedicalConditionAsync(ClaimedConditionMedicalCondition association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicalConditionId>> IConditionRepository.GetMedicalConditionIdsAsync(ClaimedConditionId claimedConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicalConditionId>>(cancellationToken);
        Task<IReadOnlyList<ClaimedConditionId>> IConditionRepository.GetClaimedConditionIdsAsync(MedicalConditionId medicalConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ClaimedConditionId>>(cancellationToken);
        Task IMedicationRepository.AddMedicationIndicationReconciliationAsync(MedicationIndicationReconciliation reconciliation, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicationIndicationReconciliation>> IMedicationRepository.GetMedicationIndicationReconciliationsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationIndicationReconciliation>>(cancellationToken);
        Task IMedicationRepository.AddMedicationRecordAsync(MedicationRecord medicationRecord, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<MedicationRecord?> IMedicationRepository.GetMedicationRecordAsync(MedicationRecordId medicationRecordId, CancellationToken cancellationToken) => Unsupported<MedicationRecord?>(cancellationToken);
        Task<IReadOnlyList<MedicationRecord>> IMedicationRepository.GetMedicationRecordsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationRecord>>(cancellationToken);
        Task<IReadOnlyList<MedicationRecord>> IMedicationRepository.GetMedicationRecordsAsync(VeteranId veteranId, string medicationName, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationRecord>>(cancellationToken);
        Task IMedicationRepository.AddMedicationLedgerAsync(MedicationLedger medicationLedger, IReadOnlyCollection<MedicationLedgerEntry> entries, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<MedicationLedger?> IMedicationRepository.GetMedicationLedgerAsync(MedicationLedgerId medicationLedgerId, CancellationToken cancellationToken) => Unsupported<MedicationLedger?>(cancellationToken);
        Task<IReadOnlyList<MedicationLedger>> IMedicationRepository.GetMedicationLedgersAsync(VeteranId veteranId, CancellationToken cancellationToken) => Query<IReadOnlyList<MedicationLedger>>(RetainedQuery.Ledgers,veteranId.Value,cancellationToken);
        Task<IReadOnlyList<MedicationLedgerEntry>> IMedicationRepository.GetMedicationLedgerEntriesAsync(MedicationLedgerId medicationLedgerId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationLedgerEntry>>(cancellationToken);
        Task IMedicationRepository.AddMedicationCurrentUseReconciliationAsync(MedicationCurrentUseReconciliation reconciliation, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicationCurrentUseReconciliation>> IMedicationRepository.GetMedicationCurrentUseReconciliationsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationCurrentUseReconciliation>>(cancellationToken);
        Task IMedicationRepository.AddMedicationClinicalContextAsync(MedicationClinicalContext clinicalContext, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IMedicationRepository.UpdateMedicationClinicalContextRecordTitleAsync(MedicationClinicalContextId medicationClinicalContextId, string recordTitle, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IMedicationRepository.SupersedeMedicationClinicalContextAsync(MedicationClinicalContextId supersededMedicationClinicalContextId, MedicationClinicalContextId replacementMedicationClinicalContextId, string reason, DateTimeOffset supersededUtc, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicationClinicalContext>> IMedicationRepository.GetMedicationClinicalContextsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationClinicalContext>>(cancellationToken);
        Task IMedicationRepository.AddMedicationHistoryEventAsync(MedicationHistoryEvent historyEvent, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicationHistoryEvent>> IMedicationRepository.GetMedicationHistoryEventsAsync(VeteranId veteranId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationHistoryEvent>>(cancellationToken);
        Task<IReadOnlyList<MedicationHistoryEvent>> IMedicationRepository.GetMedicationHistoryEventsAsync(VeteranId veteranId, string medicationName, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicationHistoryEvent>>(cancellationToken);
        Task ISourceClarificationRepository.AddAsync(SourceClarification clarification, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<SourceClarification>> ISourceClarificationRepository.GetAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Query<IReadOnlyList<SourceClarification>>(RetainedQuery.Clarifications,claimIssueId.Value,cancellationToken);
        Task ISourceClarificationRepository.SetReviewerCorrectionAsync(SourceClarificationId sourceClarificationId, string reviewerMatchText, string reviewerReplacementText, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task IClinicalProgressionRepository.AddAsync(ClinicalProgressionEvent progressionEvent, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ClinicalProgressionEvent>> IClinicalProgressionRepository.GetAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Query<IReadOnlyList<ClinicalProgressionEvent>>(RetainedQuery.Progression,claimIssueId.Value,cancellationToken);
        Task IServiceConnectionRepository.AddServiceConnectionTheoryAsync(ServiceConnectionTheory theory, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ServiceConnectionTheory?> IServiceConnectionRepository.GetServiceConnectionTheoryAsync(ServiceConnectionTheoryId theoryId, CancellationToken cancellationToken) => Unsupported<ServiceConnectionTheory?>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionTheory>> IServiceConnectionRepository.GetServiceConnectionTheoriesAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionTheory>>(cancellationToken);
        Task IServiceConnectionRepository.AddServiceConnectionBasisAsync(ServiceConnectionBasis basis, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<ServiceConnectionBasis?> IServiceConnectionRepository.GetServiceConnectionBasisAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<ServiceConnectionBasis?>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasis>> IServiceConnectionRepository.GetServiceConnectionBasesAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasis>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasis>> IServiceConnectionRepository.GetServiceConnectionBasesAsync(ServiceConnectionTheoryId theoryId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasis>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisClaimedConditionAsync(ServiceConnectionBasisClaimedCondition association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ClaimedConditionId>> IServiceConnectionRepository.GetClaimedConditionIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ClaimedConditionId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetServiceConnectionBasisIdsAsync(ClaimedConditionId claimedConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisServiceEventAsync(ServiceConnectionBasisServiceEvent association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ServiceEventId>> IServiceConnectionRepository.GetServiceEventIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceEventId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetServiceConnectionBasisIdsAsync(ServiceEventId serviceEventId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisExposureAsync(ServiceConnectionBasisExposure association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ExposureId>> IServiceConnectionRepository.GetExposureIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ExposureId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetServiceConnectionBasisIdsAsync(ExposureId exposureId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisServiceConnectedConditionAsync(ServiceConnectionBasisServiceConnectedCondition association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicalConditionId>> IServiceConnectionRepository.GetServiceConnectedConditionIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicalConditionId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetServiceConnectedConditionBasisIdsAsync(MedicalConditionId serviceConnectedConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisPrescribedMedicationAsync(ServiceConnectionBasisPrescribedMedication association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<string>> IServiceConnectionRepository.GetPrescribedMedicationNamesAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<string>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetPrescribedMedicationBasisIdsAsync(string medicationName, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisPresumptionAsync(ServiceConnectionBasisPresumption association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<RegulatoryProvisionId>> IServiceConnectionRepository.GetPresumptionProvisionIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RegulatoryProvisionId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetPresumptionBasisIdsAsync(RegulatoryProvisionId presumptionProvisionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisPreexistingConditionAsync(ServiceConnectionBasisPreexistingCondition association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<MedicalConditionId>> IServiceConnectionRepository.GetPreexistingConditionIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MedicalConditionId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetPreexistingConditionBasisIdsAsync(MedicalConditionId preexistingConditionId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisMedicalOpinionAsync(ServiceConnectionBasisMedicalOpinion association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisMedicalOpinion>> IServiceConnectionRepository.GetBasisMedicalOpinionsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisMedicalOpinion>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisArtifactAsync(ServiceConnectionBasisArtifact association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisArtifact>> IServiceConnectionRepository.GetBasisArtifactsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisArtifact>>(cancellationToken);
        Task IServiceConnectionRepository.AddBasisRequirementAsync(ServiceConnectionBasisRequirement association, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<IReadOnlyList<RequirementId>> IServiceConnectionRepository.GetRequirementIdsAsync(ServiceConnectionBasisId basisId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<RequirementId>>(cancellationToken);
        Task<IReadOnlyList<ServiceConnectionBasisId>> IServiceConnectionRepository.GetRequirementBasisIdsAsync(RequirementId requirementId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ServiceConnectionBasisId>>(cancellationToken);
        Task<ClaimIssueAdjudicationDetails?> IClaimIssueAdjudicationDetailsService.GetAsync(ClaimIssueId claimIssueId, CancellationToken cancellationToken) => Unsupported<ClaimIssueAdjudicationDetails?>(cancellationToken);
        Task<IReadOnlyList<VeteransReviewerApplicableRegulation>> IVeteransReviewerRegulatoryTextProvider.GetCurrentAsync(IReadOnlyList<string> citations, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<VeteransReviewerApplicableRegulation>>(cancellationToken);
        Task IArtifactContentStore.WriteAsync(ArtifactId artifactId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<byte[]?> IArtifactContentStore.ReadAsync(ArtifactId artifactId, CancellationToken cancellationToken) => QueryOptional<byte[]>(RetainedQuery.Source,artifactId.Value,cancellationToken);
        Task IArtifactContentStore.DeleteAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Unsupported(cancellationToken);
        Task<string?> IArtifactTextExtractor.ExtractTextAsync(ArtifactId artifactId, CancellationToken cancellationToken) => QueryOptional<string>(RetainedQuery.Text,artifactId.Value,cancellationToken);
        Task<IReadOnlyList<PrintableArtifactPage>> IArtifactPrintRenderer.RenderAsync(ArtifactId artifactId, CancellationToken cancellationToken) => Query<IReadOnlyList<PrintableArtifactPage>>(RetainedQuery.Print,artifactId.Value,cancellationToken);
        bool IEvidencePackageRepository.SupportsReviewerPresentationSnapshots => false;
        bool IEvidencePackageRepository.SupportsReviewerOutputProvenance => false;
        bool IEvidencePackageRepository.SupportsReviewerOutputBuildProvenance => false;
    }
}
