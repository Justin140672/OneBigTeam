using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetLocation;

internal sealed class GetLocationHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetLocationResponse>> HandleAsync(
        GetLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await dbContext.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                l => l.Id == request.Id && l.CompanyId == request.CompanyId,
                cancellationToken);

        if (location is null)
            return Result.Failure<GetLocationResponse>(
                Error.NotFound($"Location with id '{request.Id}' was not found."));

        return Result.Success(new GetLocationResponse(
            location.Id,
            location.CompanyId,
            location.Name,
            location.Description,
            location.LocationTypeId,
            location.IsActive));
    }
}
