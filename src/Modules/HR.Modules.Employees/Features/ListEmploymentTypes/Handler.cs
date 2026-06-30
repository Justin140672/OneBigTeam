using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListEmploymentTypes;

internal sealed class ListEmploymentTypesHandler(EmployeesDbContext db)
{
    public async Task<Result<ListEmploymentTypesResponse>> HandleAsync(
        ListEmploymentTypesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.EmploymentTypes
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId);

        if (request.IsActive is not null)
            query = query.Where(e => e.IsActive == request.IsActive);

        var items = await query
            .OrderBy(e => e.Name)
            .Select(e => new EmploymentTypeItem(e.Id, e.CompanyId, e.Name, e.Description, e.IsActive, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListEmploymentTypesResponse(items));
    }
}
