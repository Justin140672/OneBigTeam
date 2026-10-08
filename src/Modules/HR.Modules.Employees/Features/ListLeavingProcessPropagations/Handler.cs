using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListLeavingProcessPropagations;

internal sealed class ListLeavingProcessPropagationsHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<ListLeavingProcessPropagationsResponse>> HandleAsync(
        ListLeavingProcessPropagationsRequest request,
        CancellationToken cancellationToken)
    {
        var scoped = dbContext.LeavingProcessPropagations
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId);

        if (request.EmployeeId is { } employeeId)
            scoped = scoped.Where(p => p.EmployeeId == employeeId);

        var counts = await scoped
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var filtered = request.Status is { } status ? scoped.Where(p => p.Status == status) : scoped;

        var items = await filtered
            .OrderByDescending(p => p.CreatedAt)
            .Take(request.Limit)
            .Select(p => new LeavingProcessPropagationItem(
                p.Id, p.EmployeeId, p.LeavingProcessId, p.OperationType, p.LeavingDate, p.LastWorkingDay,
                p.Status, p.AttemptCount, p.LastError, p.LastAttemptAt, p.NextAttemptAt, p.CreatedAt,
                p.ProcessedAt, p.CorrelationId, p.CausationId))
            .ToListAsync(cancellationToken);

        int CountOf(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return Result.Success(new ListLeavingProcessPropagationsResponse(
            CountOf(LeavingProcessPropagation.StatusPending),
            CountOf(LeavingProcessPropagation.StatusFailed),
            CountOf(LeavingProcessPropagation.StatusProcessed),
            items));
    }
}
