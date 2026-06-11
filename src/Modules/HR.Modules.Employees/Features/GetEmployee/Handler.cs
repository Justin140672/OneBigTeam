using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed class GetEmployeeHandler
{
    private readonly EmployeesDbContext _dbContext;

    public GetEmployeeHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetEmployeeResponse>> HandleAsync(
        GetEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
        {
            return Result.Failure<GetEmployeeResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        return Result.Success(new GetEmployeeResponse(
            employee.Id,
            employee.CompanyId,
            employee.DepartmentId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.Status,
            employee.HasSystemAccess,
            employee.CreatedAt,
            employee.UpdatedAt));
    }
}
