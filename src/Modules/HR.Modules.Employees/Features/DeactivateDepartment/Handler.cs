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

        department.Deactivate(_clock.UtcNowOffset());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
