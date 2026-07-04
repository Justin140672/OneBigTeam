using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetDepartment;

internal sealed class GetDepartmentHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetDepartmentResponse>> HandleAsync(
        GetDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.Id == request.Id && d.CompanyId == request.CompanyId,
                cancellationToken);

        if (department is null)
            return Result.Failure<GetDepartmentResponse>(
                Error.NotFound($"Department with id '{request.Id}' was not found."));

        return Result.Success(new GetDepartmentResponse(
            department.Id,
            department.CompanyId,
            department.Name,
            department.Description,
            department.ParentDepartmentId,
            department.ManagerEmployeeId,
            department.IsActive));
    }
}
