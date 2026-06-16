using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListNationalities;

internal sealed class ListNationalitiesHandler(EmployeesDbContext dbContext)
{
    public async Task<ListNationalitiesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.Nationalities
            .AsNoTracking()
            .OrderBy(n => n.Name)
            .Select(n => new NationalityItem(n.Id, n.Name))
            .ToListAsync(cancellationToken);

        return new ListNationalitiesResponse(items);
    }
}
