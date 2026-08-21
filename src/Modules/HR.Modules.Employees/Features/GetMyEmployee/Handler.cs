using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyEmployee;

internal sealed class GetMyEmployeeHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetMyEmployeeResponse>> HandleAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == userId)
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.WorkingDaysOverride,
                e.HoursPerDayOverride,
                e.ProfileImageUrl,
                e.RequiresInitialSetup,
                JobTitle = dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.Title)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result.Failure<GetMyEmployeeResponse>(
                Error.NotFound("No employee record is linked to this user."));

        return Result.Success(new GetMyEmployeeResponse(
            result.Id,
            result.FirstName,
            result.LastName,
            result.JobTitle,
            result.WorkingDaysOverride,
            result.HoursPerDayOverride,
            result.ProfileImageUrl,
            result.RequiresInitialSetup));
    }
}
