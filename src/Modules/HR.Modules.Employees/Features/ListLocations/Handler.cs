using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListLocations;

internal sealed class ListLocationsHandler
{
    private readonly EmployeesDbContext _dbContext;

    public ListLocationsHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ListLocationsResponse>> HandleAsync(
        ListLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Locations
            .AsNoTracking()
            .Where(l => l.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(l => l.IsActive);

        var locations = await query
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var items = locations
            .Select(l => new LocationListItem(
                l.Id,
                l.Name,
                l.LocationTypeId,
                l.IsActive))
            .ToList();

        return Result.Success(new ListLocationsResponse(items));
    }
}
