using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed class UpdateSupportRequestStatusHandler(SupportDbContext db, IClock clock)
{
    public async Task<Result<UpdateSupportRequestStatusResponse>> HandleAsync(
        UpdateSupportRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.SupportRequests
            .SingleOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<UpdateSupportRequestStatusResponse>(Error.NotFound("Support request not found."));

        if (!entity.CanTransitionTo(request.Status))
            return Result.Failure<UpdateSupportRequestStatusResponse>(
                Error.Conflict("A closed support request cannot be reopened directly back to Submitted."));

        var now = clock.UtcNowOffset();
        entity.ChangeStatus(request.Status, now);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateSupportRequestStatusResponse(entity.Id, entity.Status.ToString(), entity.UpdatedAt));
    }
}
