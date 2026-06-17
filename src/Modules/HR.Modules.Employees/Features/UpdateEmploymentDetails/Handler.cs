using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed class UpdateEmploymentDetailsHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateEmploymentDetailsHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateEmploymentDetailsResponse>> HandleAsync(
        UpdateEmploymentDetailsRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));

        if (request.DepartmentId.HasValue)
        {
            var deptExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.DepartmentId && d.CompanyId == request.CompanyId && d.IsActive,
                    cancellationToken);

            if (!deptExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Department '{request.DepartmentId}' was not found or is inactive."));
        }

        if (request.PositionProfileId.HasValue)
        {
            var posExists = await _dbContext.PositionProfiles
                .AnyAsync(
                    p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId && p.IsActive,
                    cancellationToken);

            if (!posExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Position profile '{request.PositionProfileId}' was not found or is inactive."));
        }

        if (request.ManagerId.HasValue)
        {
            var managerExists = await _dbContext.Employees
                .AnyAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.Terminated,
                    cancellationToken);

            if (!managerExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));

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
                    return Result.Failure<UpdateEmploymentDetailsResponse>(
                        Error.Conflict("This assignment would create a circular management hierarchy."));

                if (!visited.Add(cursor.Value))
                    break;

                cursor = allEmployees.TryGetValue(cursor.Value, out var next) ? next : null;
            }
        }

        var now = _clock.UtcNowOffset();

        if (employee.Status != request.Status)
        {
            switch (request.Status)
            {
                case EmploymentStatus.Active:     employee.Activate(now);    break;
                case EmploymentStatus.OnLeave:    employee.SetOnLeave(now);  break;
                case EmploymentStatus.Suspended:  employee.Suspend(now);     break;
                case EmploymentStatus.Terminated: employee.Terminate(now);   break;
            }
        }

        employee.UpdateEmploymentDetails(
            request.EmployeeNumber,
            request.EmploymentType,
            request.StartDate,
            request.ContinuousServiceDate,
            request.ProbationEndDate,
            request.LeavingDate,
            request.Notes,
            now);

        employee.Assign(request.DepartmentId, request.PositionProfileId, request.ManagerId, now);
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateEmploymentDetailsResponse(
            employee.Id,
            employee.CompanyId,
            employee.EmployeeNumber,
            employee.EmploymentType,
            employee.Status,
            employee.DepartmentId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.StartDate,
            employee.ContinuousServiceDate,
            employee.ProbationEndDate,
            employee.LeavingDate,
            employee.WorkingDaysOverride,
            employee.HoursPerDayOverride,
            employee.Notes,
            employee.UpdatedAt));
    }
}
