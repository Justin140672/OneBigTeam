using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyContactDetails;

internal sealed class GetMyContactDetailsHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetMyContactDetailsResponse>> HandleAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == userId)
            .Select(e => new GetMyContactDetailsResponse(
                e.WorkEmail,
                e.PersonalEmail,
                e.PhoneNumber,
                e.HomePhone,
                e.AddressLine1,
                e.AddressLine2,
                e.City,
                e.County,
                e.PostCode,
                e.Country))
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result.Failure<GetMyContactDetailsResponse>(
                Error.NotFound("No employee record is linked to this user."));

        return Result.Success(result);
    }
}
