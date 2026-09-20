using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ActivatePlatformAdministrator;

/// <summary>
/// P1: completes platform-administrator provisioning by linking the local record to the caller's
/// REAL, currently-authenticated Supabase identity. This is the ownership-verification step for
/// both provisioning paths:
///   - PendingProvisioning (brand-new provider account): the caller reached this endpoint by
///     confirming Supabase's own account-confirmation email and then authenticating — a genuine
///     first login as that new identity.
///   - PendingLinkVerification (pre-existing provider account): the caller reached this endpoint by
///     signing in with their EXISTING credentials — proof of control that a mere email-address
///     match could never provide, and exactly why this codebase's guardrails forbid linking on
///     email match alone.
/// Either way, <see cref="ICurrentUser.UserId"/> here is a real Supabase "sub" claim verified by
/// HR.Api's JWT Bearer pipeline — never client-supplied, never trusted from a route/body value.
/// </summary>
internal sealed class ActivatePlatformAdministratorHandler(IdentityDbContext db, IClock clock, IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ActivatePlatformAdministratorResponse>> HandleAsync(
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } supabaseAuthUserId || string.IsNullOrWhiteSpace(currentUser.Email))
            return Result.Failure<ActivatePlatformAdministratorResponse>(
                Error.Unauthorized("A verified identity-provider session is required to activate an administrator account."));

        var normalizedEmail = currentUser.Email.Trim().ToLowerInvariant();

        var administrator = await db.PlatformAdministrators
            .SingleOrDefaultAsync(a => a.Email == normalizedEmail, cancellationToken);

        if (administrator is null)
            return Result.Failure<ActivatePlatformAdministratorResponse>(
                Error.NotFound("No pending platform administrator invitation was found for this account."));

        // Defence-in-depth beyond the DB's own unique-filtered index on SupabaseAuthUserId: a
        // clear, specific error rather than letting a duplicate hit the DB constraint as a 500.
        var alreadyLinkedElsewhere = await db.PlatformAdministrators.AnyAsync(
            a => a.Id != administrator.Id && a.SupabaseAuthUserId == supabaseAuthUserId, cancellationToken);
        if (alreadyLinkedElsewhere)
            return Result.Failure<ActivatePlatformAdministratorResponse>(
                Error.Conflict("This identity-provider account is already linked to a different platform administrator."));

        var expectedVersion = administrator.Version;
        var now = clock.UtcNowOffset();

        var completeResult = administrator.CompleteProvisioning(supabaseAuthUserId, now);
        if (completeResult.IsFailure)
            return Result.Failure<ActivatePlatformAdministratorResponse>(completeResult.Error);

        // Race-safe: a replayed/concurrent activation attempt for the same row loses the
        // optimistic-concurrency check (Ticket 2 pattern) rather than silently re-succeeding.
        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            administrator, expectedVersion, "This administrator account has already been activated.", cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<ActivatePlatformAdministratorResponse>(saveResult.Error);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorActivatedAuditEvent(administrator.Id, administrator.Email, supabaseAuthUserId, now),
            cancellationToken);

        return Result.Success(new ActivatePlatformAdministratorResponse(administrator.Id, administrator.Email));
    }
}
