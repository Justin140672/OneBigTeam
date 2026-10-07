using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetProposedLastWorkingDay;

internal sealed class GetProposedLastWorkingDayHandler(
    EmployeesDbContext dbContext,
    IWorkingPatternProvider workingPatternProvider,
    IPublicHolidayReader publicHolidayReader)
{
    public async Task<Result<GetProposedLastWorkingDayResponse>> HandleAsync(
        GetProposedLastWorkingDayRequest request,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetProposedLastWorkingDayResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var pattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var windowStart = request.LeavingDate.AddDays(-ProposedLastWorkingDayCalculator.LookbackDays);

        var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
            request.CompanyId, windowStart, request.LeavingDate, cancellationToken);

        var proposed = ProposedLastWorkingDayCalculator.Calculate(
            request.LeavingDate, pattern, holidays.Select(h => h.Date).ToHashSet());

        return Result.Success(new GetProposedLastWorkingDayResponse(proposed));
    }
}
