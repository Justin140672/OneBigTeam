using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListBlockedInterviewOutcomeReconciliations;

internal sealed class ListBlockedInterviewOutcomeReconciliationsHandler(RecruitmentDbContext db)
{
    private const int MaxItems = 200;

    public async Task<Result<ListBlockedInterviewOutcomeReconciliationsResponse>> HandleAsync(
        ListBlockedInterviewOutcomeReconciliationsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await db.InterviewOutcomeTaskReconciliations
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.BlockedAt != null)
            .OrderBy(r => r.BlockedAt)
            .Take(MaxItems)
            .Select(r => new BlockedInterviewOutcomeReconciliationItem(
                r.Id, r.InterviewId, r.ApplicationId, r.BlockedTaskId, r.BlockedTasksOperationId,
                r.BlockedCategory, r.FailureReason, r.AttemptCount, r.BlockedAt!.Value))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListBlockedInterviewOutcomeReconciliationsResponse(items));
    }
}
