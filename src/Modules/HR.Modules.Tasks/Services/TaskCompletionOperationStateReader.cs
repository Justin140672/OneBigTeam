using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompletionOperationStateReader(TasksDbContext dbContext) : ITaskCompletionOperationStateReader
{
    public async Task<TaskCompletionOperationState?> GetAsync(
        Guid companyId, Guid operationId, CancellationToken cancellationToken)
    {
        var programmatic = await dbContext.ProgrammaticTaskCompletions.AsNoTracking()
            .Where(c => c.OperationId == operationId && c.CompanyId == companyId)
            .Select(c => new TaskCompletionOperationState(
                c.OperationId, c.TaskId, c.TerminalFailureAt != null, false, c.ResetCount, c.LastResetBy, c.LastResetAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (programmatic is not null)
            return programmatic;

        return await dbContext.TaskCompletionOperations.AsNoTracking()
            .Where(o => o.Id == operationId && o.CompanyId == companyId)
            .Select(o => new TaskCompletionOperationState(
                o.Id, o.TaskId,
                o.Status == TaskCompletionOperation.StatusEffectsTerminalFailure
                    || o.Status == TaskCompletionOperation.StatusDataIntegrityFailure,
                o.Status == TaskCompletionOperation.StatusDataIntegrityFailure,
                o.ResetCount, o.LastResetBy, o.LastResetAt,
                o.AdjudicationCount, o.ResolutionType, o.Status == TaskCompletionOperation.StatusWaived,
                o.LastAdjudicatedBy))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
