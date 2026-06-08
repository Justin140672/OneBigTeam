using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AssignManager;

internal sealed class AssignManagerHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public AssignManagerHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<AssignManagerResponse>> HandleAsync(
        AssignManagerRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
        {
            return Result.Failure<AssignManagerResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        string? managerFullName = null;

        if (request.ManagerId is not null)
        {
            var manager = await _dbContext.Employees
                .SingleOrDefaultAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.Terminated,
                    cancellationToken);

            if (manager is null)
            {
                return Result.Failure<AssignManagerResponse>(
                    Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));
            }

            // Circular hierarchy check: walk up the proposed manager's chain.
            // If we reach the employee being updated, the assignment would create a cycle.
            var allEmployees = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.CompanyId == request.CompanyId)
                .Select(e => new { e.Id, e.ManagerId })
                .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

            var visited = new HashSet<Guid>();
            var cursor = request.ManagerId;

            while (cursor is not null)
            {
                if (cursor == request.Id)
                {
                    return Result.Failure<AssignManagerResponse>(
                        Error.Conflict("This assignment would create a circular management hierarchy."));
                }

                if (!visited.Add(cursor.Value))
                    break; // Existing cycle in data — stop to avoid infinite loop

                cursor = allEmployees.TryGetValue(cursor.Value, out var nextManagerId) ? nextManagerId : null;
            }

            managerFullName = $"{manager.FirstName} {manager.LastName}";
        }

        var now = _clock.UtcNowOffset();

        employee.Assign(employee.DepartmentId, employee.PositionProfileId, request.ManagerId, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new AssignManagerResponse(
            employee.Id,
            employee.CompanyId,
            employee.ManagerId,
            managerFullName,
            employee.UpdatedAt));
    }
}
