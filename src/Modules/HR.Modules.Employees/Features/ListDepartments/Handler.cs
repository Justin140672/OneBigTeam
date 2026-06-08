using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListDepartments;

internal sealed class ListDepartmentsHandler
{
    private readonly EmployeesDbContext _dbContext;

    public ListDepartmentsHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ListDepartmentsResponse>> HandleAsync(
        ListDepartmentsRequest request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Departments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(d => d.IsActive);

        var departments = await query
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var items = departments
            .Select(d => new DepartmentListItem(
                d.Id,
                d.Name,
                d.ParentDepartmentId,
                d.ManagerEmployeeId,
                d.IsActive))
            .ToList();

        return Result.Success(new ListDepartmentsResponse(items));
    }
}
