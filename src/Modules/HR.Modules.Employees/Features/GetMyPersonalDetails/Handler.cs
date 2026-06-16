using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyPersonalDetails;

internal sealed class GetMyPersonalDetailsHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetMyPersonalDetailsResponse>> HandleAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == userId)
            .Select(e => new GetMyPersonalDetailsResponse(
                e.Id,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.DateOfBirth,
                e.Nationality,
                e.Gender))
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result.Failure<GetMyPersonalDetailsResponse>(
                Error.NotFound("No employee record is linked to this user."));

        return Result.Success(result);
    }
}
