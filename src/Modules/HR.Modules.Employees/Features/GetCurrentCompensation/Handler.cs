using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetCurrentCompensation;

internal sealed class GetCurrentCompensationHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<GetCurrentCompensationResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetCurrentCompensationResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var today = DateOnly.FromDateTime(clock.UtcNow);

        var current = await dbContext.Compensations
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId &&
                        c.EffectiveFrom <= today && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .OrderByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return Result.Failure<GetCurrentCompensationResponse>(
                Error.NotFound("No current compensation record found for this employee."));

        return Result.Success(new GetCurrentCompensationResponse(
            current.Id,
            current.CompanyId,
            current.EmployeeId,
            current.EffectiveFrom,
            current.EffectiveTo,
            current.SalaryType.ToString(),
            current.Salary,
            current.Currency,
            current.HoursPerWeek,
            current.FTE,
            current.Notes,
            current.CreatedAt,
            current.UpdatedAt));
    }
}
