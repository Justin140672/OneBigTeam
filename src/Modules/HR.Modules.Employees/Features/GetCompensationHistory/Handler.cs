using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetCompensationHistory;

internal sealed class GetCompensationHistoryHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetCompensationHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetCompensationHistoryResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var items = await dbContext.Compensations
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId)
            .OrderByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.CreatedAt)
            .Select(c => new CompensationHistoryItem(
                c.Id,
                c.EffectiveFrom,
                c.EffectiveTo,
                c.SalaryType.ToString(),
                c.Salary,
                c.Currency,
                c.HoursPerWeek,
                c.FTE,
                c.Notes,
                c.Reason.ToString(),
                c.CreatedBy,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetCompensationHistoryResponse(items));
    }
}
