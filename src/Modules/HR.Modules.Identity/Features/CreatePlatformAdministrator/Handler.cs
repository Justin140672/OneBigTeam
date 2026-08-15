using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

// Creates a local authorization record only. Only an enabled PlatformOwner may create new
// platform administrator accounts (defense-in-depth handler-level gate — see remarks below).
internal sealed class CreatePlatformAdministratorHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreatePlatformAdministratorResponse>> HandleAsync(
        CreatePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var alreadyExists = await db.PlatformAdministrators
            .AnyAsync(a => a.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Conflict("A platform administrator with this email already exists."));

        var now = clock.UtcNow;

        // TODO: this only creates the local authorization record. Provisioning a real Supabase
        // Auth user for this administrator (or linking to an existing one) is a follow-up —
        // SupabaseAuthUserId is left null here.
        var administrator = PlatformAdministrator.Create(
            normalizedEmail, request.Role, now, createdByUserId: currentUser.UserId);

        db.PlatformAdministrators.Add(administrator);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorCreatedAuditEvent(
                administrator.Id, administrator.Email, administrator.Role, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new CreatePlatformAdministratorResponse(
            administrator.Id, administrator.Email, administrator.Role, administrator.IsEnabled, administrator.CreatedAt));
    }

    internal static async Task<bool> IsEnabledPlatformOwnerAsync(
        IdentityDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Email))
            return false;

        var normalizedEmail = currentUser.Email.Trim().ToLowerInvariant();
        return await db.PlatformAdministrators.AnyAsync(
            a => a.Email == normalizedEmail && a.IsEnabled && a.Role == PlatformAdministratorRole.PlatformOwner,
            cancellationToken);
    }
}
