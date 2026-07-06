using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetOutstandingTaskCount;

internal sealed class GetOutstandingTaskCountHandler(TasksDbContext dbContext)
{
    public async Task<Result<GetOutstandingTaskCountResponse>> HandleAsync(
        GetOutstandingTaskCountRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled);

        if (request.Source.HasValue)
            query = query.Where(t => t.Source == request.Source.Value);

        if (request.ActionType.HasValue)
            query = query.Where(t => t.ActionType == request.ActionType.Value);

        var count = await query.CountAsync(cancellationToken);

        return Result.Success(new GetOutstandingTaskCountResponse(count));
    }
}
