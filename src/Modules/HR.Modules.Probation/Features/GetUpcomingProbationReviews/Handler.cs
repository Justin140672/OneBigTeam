using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed class GetUpcomingProbationReviewsHandler(
    ProbationDbContext dbContext,
    IOpenTaskBySourceEntityReader openTaskReader,
    IClock clock)
{
    public async Task<Result<GetUpcomingProbationReviewsResponse>> HandleAsync(
        GetUpcomingProbationReviewsRequest request,
        IReadOnlySet<Guid>? authorizedEmployeeIds,
        CancellationToken cancellationToken)
    {
        // authorizedEmployeeIds is null for HR Administrators (company-wide, unrestricted). For
        // managers it is their full reporting hierarchy — resolved server-side by the endpoint
        // via ProbationResourceAuthorizer, never trusted from the client (PROB-02).
        if (authorizedEmployeeIds is not null && authorizedEmployeeIds.Count == 0)
            return Result.Success(new GetUpcomingProbationReviewsResponse([]));

        var today   = DateOnly.FromDateTime(clock.UtcNowOffset().DateTime);
        var cutoff  = today.AddDays(30);

        var rows = await (
            from review in dbContext.ProbationReviews.AsNoTracking()
            join record in dbContext.ProbationRecords.AsNoTracking()
                on review.ProbationRecordId equals record.Id
            where review.CompanyId == request.CompanyId
               && review.Status    == ProbationReviewStatus.Pending
               && review.DueDate   <= cutoff
               && (authorizedEmployeeIds == null || authorizedEmployeeIds.Contains(record.EmployeeId))
            orderby review.DueDate
            select new
            {
                review.Id,
                review.ProbationRecordId,
                record.EmployeeId,
                ReviewType = review.ReviewType,
                review.DueDate,
            }
        ).ToListAsync(cancellationToken);

        // GenerateDueProbationReviewsJob creates one Review-action task per review with
        // sourceEntityId = review.Id, so the same review.Id used to build this projection
        // resolves the open task directly — mirrors GetRecentLeaveRequestsHandler's TaskId lookup.
        var reviewIds = rows.Select(r => r.Id).ToList();
        var openTaskIds = await openTaskReader.GetOpenTaskIdsAsync(
            request.CompanyId, reviewIds, cancellationToken, TaskActionType.Review);

        var items = rows
            .Select(r => new UpcomingProbationReviewItem(
                r.Id,
                r.ProbationRecordId,
                r.EmployeeId,
                r.ReviewType.ToString(),
                r.DueDate,
                openTaskIds.TryGetValue(r.Id, out var taskId) ? taskId : (Guid?)null))
            .ToList();

        return Result.Success(new GetUpcomingProbationReviewsResponse(items));
    }
}
