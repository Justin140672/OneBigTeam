using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;

internal sealed class ListTaskCompletionOperationsRequiringInterventionHandler(TasksDbContext dbContext)
{
    public async Task<Result<ListTaskCompletionOperationsRequiringInterventionResponse>> HandleAsync(
        ListTaskCompletionOperationsRequiringInterventionRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TaskCompletionOperations.AsNoTracking()
            .Where(o => o.CompanyId == request.CompanyId);

        query = request.Status is { } status
            ? query.Where(o => o.Status == status)
            : query.Where(o => o.Status == TaskCompletionOperation.StatusEffectsTerminalFailure
                || o.Status == TaskCompletionOperation.StatusDataIntegrityFailure);

        if (!string.IsNullOrWhiteSpace(request.FailureCategory))
            query = query.Where(o => o.FailureCategory == request.FailureCategory);
        if (request.OperationId is { } operationId)
            query = query.Where(o => o.Id == operationId);
        if (request.TaskId is { } taskId)
            query = query.Where(o => o.TaskId == taskId);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(o => o.TerminalFailureAt ?? o.ResolvedAt ?? o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(o => new TaskCompletionOperationInterventionItem(
            o.Id, o.TaskId, o.Status, o.FailureCategory, o.TerminalFailureAt, o.CreatedAt, o.AttemptCount,
            o.ResetCount, o.AdjudicationCount,
            o.Status == TaskCompletionOperation.StatusEffectsTerminalFailure,
            o.Status == TaskCompletionOperation.StatusDataIntegrityFailure,
            o.Status switch
            {
                TaskCompletionOperation.StatusEffectsTerminalFailure => "awaiting_reset",
                TaskCompletionOperation.StatusDataIntegrityFailure => "awaiting_adjudication",
                TaskCompletionOperation.StatusEffectsVerified => "adjudicated_verified",
                TaskCompletionOperation.StatusWaived => "adjudicated_waived",
                _ => "none",
            },
            o.ResolutionType, o.LastAdjudicatedAt)).ToList();

        return Result.Success(new ListTaskCompletionOperationsRequiringInterventionResponse(
            items, total, request.PageNumber, request.PageSize));
    }
}
