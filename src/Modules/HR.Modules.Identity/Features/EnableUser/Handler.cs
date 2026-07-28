using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.EnableUser;

internal sealed class EnableUserHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<EnableUserResponse>> HandleAsync(
        EnableUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<EnableUserResponse>(Error.NotFound("User was not found."));

        if (user.IsActive)
            return Result.Failure<EnableUserResponse>(Error.Conflict("User account is already active."));

        var now = clock.UtcNow;
        user.Reactivate(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new UserEnabledAuditEvent(request.CompanyId, user.Id, user.Id, actorUserId, now),
            cancellationToken);

        return Result.Success(new EnableUserResponse(user.Id, user.IsActive));
    }
}
