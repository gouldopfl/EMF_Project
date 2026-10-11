using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

// Future authenticated hosts use this boundary; stock composition never supplies
// a grant. Recorded approval is insufficient, even when its expiry is in future.
public sealed class ZipAuthorizedOwnershipService(IZipParentAdmissionJournal admissions,
    IZipOwnershipJournal parents, ZipDurableProfile profile, TimeProvider? timeProvider = null)
{
    public async Task<ZipParentSnapshot> ClaimAsync(IZipAuthorizedExecution grant, ZipOwnershipClaimRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant); ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (request.Purpose != grant.Purpose || request.ExpectedAdmission != grant.Admission ||
            string.IsNullOrWhiteSpace(grant.ExecutingActorId))
            throw new UnauthorizedAccessException("ZIP ownership grant anchor or purpose changed.");
        if (request.Duration > profile.OwnershipLease)
            throw new InvalidDataException("ZIP ownership lease exceeds the frozen profile.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, grant.Revoked);
        ct = linked.Token; ct.ThrowIfCancellationRequested();
        profile.ValidateRecoveryBinding(grant.Admission.Binding.ProfileJson, grant.Admission.Binding.ProfileHash);
        var clock = timeProvider ?? TimeProvider.System;
        var grantDeadline = grant.ValidUntilUtc;
        var remaining = grantDeadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            throw new UnauthorizedAccessException("ZIP execution grant expired.");
        using var deadline = new CancellationTokenSource(remaining, clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        ct = bounded.Token;
        await grant.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        var current = await admissions.ReadAdmissionAsync(grant.Admission.OperationId, ct)
            ?? throw new InvalidDataException("Missing original ZIP admission.");
        if (current != grant.Admission || current.State != ZipAdmissionState.ParentBound ||
            current.LatestAuthorityEvidence is null ||
            (current.LatestAuthorityEvidence.Capabilities & (request.Purpose == ZipOwnershipPurpose.InitialExecution
                ? ZipAuthorityCapabilities.Admit : ZipAuthorityCapabilities.Recover)) == 0)
            throw new UnauthorizedAccessException("ZIP ownership authority or admission changed.");
        ct.ThrowIfCancellationRequested();
        ZipParentSnapshot claimed;
        DateTimeOffset scopeExpires;
        await using (var scope = await grant.AuthorizeOwnershipEffectAsync(request, ct))
        {
            scopeExpires = scope.ValidUntilUtc;
            if (!ReferenceEquals(scope.Root, grant) || scope.Request != request ||
                scope.ExecutingActorId != grant.ExecutingActorId || scopeExpires > grantDeadline)
                throw new UnauthorizedAccessException("ZIP ownership scope does not match its attempt.");
            remaining = scopeExpires - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw new UnauthorizedAccessException("ZIP ownership scope expired.");
            using var scopeDeadline = new CancellationTokenSource(remaining, clock);
            using var scopeBounded = CancellationTokenSource.CreateLinkedTokenSource(ct, scope.Revoked, scopeDeadline.Token);
            var scopeToken = scopeBounded.Token;
            // Only the atomic purpose-aware route; no compatibility-claim fallback.
            claimed = await scope.ExecuteAsync(async effectToken =>
            {
                using var effectBounded = CancellationTokenSource.CreateLinkedTokenSource(scopeToken, effectToken);
                var token = effectBounded.Token;
                token.ThrowIfCancellationRequested();
                await grant.RevalidateAsync(token);
                token.ThrowIfCancellationRequested();
                if (clock.GetUtcNow() >= scopeExpires || clock.GetUtcNow() >= grantDeadline ||
                    grant.ValidUntilUtc != grantDeadline || scope.ValidUntilUtc != scopeExpires)
                    throw new UnauthorizedAccessException("ZIP ownership authority expired.");
                return await parents.ClaimOwnershipAsync(request, token);
            }, scopeToken);
            scopeToken.ThrowIfCancellationRequested();
            if (scope.ValidUntilUtc != scopeExpires)
                throw new UnauthorizedAccessException("ZIP ownership scope changed.");
        }
        // Disposal can await too. Finish it before the final root-authority check;
        // intentional scope disposal ends this one effect, not the root attempt.
        ct.ThrowIfCancellationRequested();
        await grant.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (grantDeadline <= clock.GetUtcNow() || scopeExpires <= clock.GetUtcNow() ||
            grant.ValidUntilUtc != grantDeadline)
            throw new UnauthorizedAccessException("ZIP execution grant expired.");
        return claimed;
    }
}
