using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetLeavingProcessHistory;

internal sealed class GetLeavingProcessHistoryHandler(
    EmployeesDbContext dbContext)
{
    public async Task<Result<GetLeavingProcessHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetLeavingProcessHistoryResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var leavingProcesses = await dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.Status,
                p.ResignationReceivedDate,
                p.LeavingDate,
                p.LastWorkingDay,
                p.LeavingReason,
                p.Notes,
                p.ReplacementManagerEmployeeId,
                p.StartedAt,
                p.CancelledAt,
                p.CancellationReason,
                p.FinalisationCompletedAt,
                p.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        // If there are any replacement manager IDs, fetch the manager names
        var replacementManagerIds = leavingProcesses
            .Where(p => p.ReplacementManagerEmployeeId.HasValue)
            .Select(p => p.ReplacementManagerEmployeeId!.Value)
            .Distinct()
            .ToList();

        var managerNames = replacementManagerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Employees
                .AsNoTracking()
                .Where(e => e.CompanyId == companyId && replacementManagerIds.Contains(e.Id))
                .ToDictionaryAsync(
                    e => e.Id,
                    e => $"{e.FirstName} {e.LastName}",
                    cancellationToken);

        var items = leavingProcesses
            .Select(p => new LeavingProcessHistoryItem(
                p.Id,
                p.Status.ToString(),
                p.ResignationReceivedDate,
                p.LeavingDate,
                p.LastWorkingDay,
                p.LeavingReason.ToString(),
                p.Notes,
                p.ReplacementManagerEmployeeId.HasValue && managerNames.TryGetValue(p.ReplacementManagerEmployeeId.Value, out var name)
                    ? name
                    : null,
                p.StartedAt,
                p.CancelledAt,
                p.CancellationReason,
                p.FinalisationCompletedAt,
                p.UpdatedAt,
                p.Status.ToString() == "InProgress"))
            .ToList();

        return Result.Success(new GetLeavingProcessHistoryResponse(items));
    }
}
