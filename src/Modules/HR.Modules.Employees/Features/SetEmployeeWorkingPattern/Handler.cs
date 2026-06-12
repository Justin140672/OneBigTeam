using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed class SetEmployeeWorkingPatternHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<SetEmployeeWorkingPatternResponse>> HandleAsync(
        SetEmployeeWorkingPatternRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<SetEmployeeWorkingPatternResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var now = clock.UtcNowOffset();
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new SetEmployeeWorkingPatternResponse(
            employee.Id,
            employee.CompanyId,
            employee.WorkingDaysOverride,
            employee.HoursPerDayOverride,
            employee.UpdatedAt));
    }
}
