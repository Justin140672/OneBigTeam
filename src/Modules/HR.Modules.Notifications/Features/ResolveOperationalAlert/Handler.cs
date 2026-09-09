using System.Threading;
using System.Threading.Tasks;

using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed class ResolveOperationalAlertHandler(
    NotificationsDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ResolveOperationalAlertResponse>> HandleAsync(
        ResolveOperationalAlertRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.Unauthorized("The current user could not be resolved."));

        var alert = await dbContext.AdministrativeAlerts
            .SingleOrDefaultAsync(a => a.Id == request.AlertId, cancellationToken);

        if (alert is null)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.NotFound($"Operational alert '{request.AlertId}' was not found."));

        if (alert.Status == AdministrativeAlertStatus.Resolved)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.Conflict("This operational alert has already been resolved."));

        var now = clock.UtcNowOffset();
        alert.Resolve(userId, request.ResolutionNote, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new AdministrativeAlertResolvedAuditEvent(
                alert.CompanyId,
                alert.Id,
                userId,
                alert.ResolutionNote,
                now),
            cancellationToken);

        return Result.Success(new ResolveOperationalAlertResponse(
            alert.Id,
            alert.Status.ToString(),
            alert.ResolvedAt,
            alert.ResolvedByUserId));
    }
}
