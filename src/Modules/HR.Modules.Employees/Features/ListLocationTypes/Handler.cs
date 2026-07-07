using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListLocationTypes;

internal sealed class ListLocationTypesHandler(EmployeesDbContext db)
{
    public async Task<Result<ListLocationTypesResponse>> HandleAsync(
        ListLocationTypesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.LocationTypes
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId);

        if (request.IsActive is not null)
            query = query.Where(e => e.IsActive == request.IsActive);

        var items = await query
            .OrderBy(e => e.Name)
            .Select(e => new LocationTypeItem(e.Id, e.CompanyId, e.Name, e.Description, e.IsActive, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListLocationTypesResponse(items));
    }
}
