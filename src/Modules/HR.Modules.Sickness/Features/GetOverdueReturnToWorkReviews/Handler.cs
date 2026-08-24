using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed class GetOverdueReturnToWorkReviewsHandler(
    SicknessDbContext dbContext,
    IOpenTaskBySourceEntityReader openTaskReader)
{
    public async Task<GetOverdueReturnToWorkReviewsResponse> HandleAsync(
        GetOverdueReturnToWorkReviewsRequest request,
        IReadOnlySet<Guid>? authorizedEmployeeIds,
        CancellationToken cancellationToken)
    {
        // authorizedEmployeeIds is null for HR Administrators (company-wide, unrestricted).
        // For managers it is their full reporting hierarchy — resolved server-side by the
        // endpoint via SicknessResourceAuthorizer, never trusted from the client (SICK-02).
        if (authorizedEmployeeIds is not null && authorizedEmployeeIds.Count == 0)
            return new GetOverdueReturnToWorkReviewsResponse([]);

        var rows = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId
                     && r.Status == ReturnToWorkReviewStatus.Overdue
                     && (authorizedEmployeeIds == null || authorizedEmployeeIds.Contains(r.EmployeeId)))
            .OrderBy(r => r.DueDate)
            .Select(r => new
            {
                r.Id,
                r.EmployeeId,
                r.SicknessRecordId,
                r.DueDate,
            })
            .ToListAsync(cancellationToken);

        // CloseSicknessRecordHandler creates one Review-action task per review with
        // sourceEntityId = review.Id, so the same review.Id used to build this projection
        // resolves the open task directly — mirrors GetUpcomingProbationReviewsHandler.
        var reviewIds = rows.Select(r => r.Id).ToList();
        var openTaskIds = await openTaskReader.GetOpenTaskIdsAsync(
            request.CompanyId, reviewIds, cancellationToken, TaskActionType.Review);

        var items = rows
            .Select(r => new OverdueReturnToWorkReviewItem(
                r.Id,
                r.EmployeeId,
                r.SicknessRecordId,
                r.DueDate,
                openTaskIds.TryGetValue(r.Id, out var taskId) ? taskId : (Guid?)null))
            .ToList();

        return new GetOverdueReturnToWorkReviewsResponse(items);
    }
}
