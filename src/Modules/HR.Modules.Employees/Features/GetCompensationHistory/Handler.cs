using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetCompensationHistory;

internal sealed class GetCompensationHistoryHandler(EmployeesDbContext dbContext, IEmployeeNameReader employeeNameReader)
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

        var records = await dbContext.Compensations
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId)
            .OrderByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var creatorIds = records.Select(c => c.CreatedBy).Distinct().ToList();
        var creatorNames = await employeeNameReader.GetNamesAsync(companyId, creatorIds, cancellationToken);

        var items = records
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
                creatorNames.TryGetValue(c.CreatedBy, out var name) ? name : "Unknown",
                c.CreatedAt))
            .ToList();

        return Result.Success(new GetCompensationHistoryResponse(items));
    }
}
