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

        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations
                .AnyAsync(
                    l => l.Id == request.LocationId && l.CompanyId == request.CompanyId && l.IsActive,
                    cancellationToken);

            if (!locationExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Location '{request.LocationId}' was not found or is inactive."));
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

        // Draft isn't a selectable option on the Employment tab's status dropdown — it's only
        // ever a brand-new employee's starting state — so the one transition worth rejecting here
        // is someone actively reverting an already-progressed employee back to it. A Draft
        // employee whose edit doesn't touch status at all (e.g. just assigning a manager) still
        // round-trips Status == Draft unchanged, which must be allowed through.
        if (request.Status == EmploymentStatus.Draft && employee.Status != EmploymentStatus.Draft)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.Validation("Cannot set employment status to Draft."));

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

        if (request.EmploymentTypeId.HasValue)
        {
            var etExists = await _dbContext.EmploymentTypes
                .AnyAsync(
                    t => t.Id == request.EmploymentTypeId && t.CompanyId == request.CompanyId && t.IsActive,
                    cancellationToken);

            if (!etExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Employment type '{request.EmploymentTypeId}' was not found or is inactive."));
        }

        employee.UpdateEmploymentDetails(
            request.EmployeeNumber ?? employee.EmployeeNumber,
            request.EmploymentTypeId ?? employee.EmploymentTypeId,
            request.StartDate,
            request.ContinuousServiceDate,
            request.ProbationEndDate,
            request.LeavingDate,
            request.Notes,
            now);

        employee.Assign(
            request.DepartmentId ?? employee.DepartmentId,
            request.PositionProfileId ?? employee.PositionProfileId,
            request.LocationId ?? employee.LocationId,
            request.ManagerId,
            now);
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateEmploymentDetailsResponse(
            employee.Id,
            employee.CompanyId,
            employee.EmployeeNumber,
            employee.EmploymentTypeId,
            employee.Status,
            employee.DepartmentId,
            employee.LocationId,
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
