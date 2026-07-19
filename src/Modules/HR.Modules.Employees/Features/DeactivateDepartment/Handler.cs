using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivateDepartment;

internal sealed class DeactivateDepartmentHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public DeactivateDepartmentHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(
        DeactivateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await _dbContext.Departments
            .SingleOrDefaultAsync(
                d => d.Id == request.Id && d.CompanyId == request.CompanyId && d.IsActive,
                cancellationToken);

        if (department is null)
            return Result.Failure(Error.NotFound($"Department '{request.Id}' was not found."));

        var currentEmployeeCount = await _dbContext.Employees
            .CountAsync(
                e => e.DepartmentId == request.Id
                  && e.CompanyId == request.CompanyId
                  && e.Status != EmploymentStatus.Terminated,
                cancellationToken);

        if (currentEmployeeCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{department.Name}' — it is currently assigned to " +
                $"{currentEmployeeCount} active employee{(currentEmployeeCount == 1 ? "" : "s")}."));
        }

        department.Deactivate(_clock.UtcNowOffset());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
