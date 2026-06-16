using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed class UpdateEmployeeProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateEmployeeProfileHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateEmployeeProfileResponse>> HandleAsync(
        UpdateEmployeeProfileRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
        {
            return Result.Failure<UpdateEmployeeProfileResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        var newEmail = request.WorkEmail.Trim().ToLowerInvariant();

        if (!string.Equals(employee.WorkEmail, newEmail, StringComparison.Ordinal))
        {
            var emailTaken = await _dbContext.Employees
                .AnyAsync(
                    e => e.CompanyId == request.CompanyId &&
                         e.Id != request.Id &&
                         e.WorkEmail == newEmail,
                    cancellationToken);

            if (emailTaken)
            {
                return Result.Failure<UpdateEmployeeProfileResponse>(
                    Error.Conflict($"An employee with work email '{request.WorkEmail.Trim()}' already exists in this company."));
            }
        }

        var now = _clock.UtcNowOffset();

        employee.UpdateProfile(
            request.FirstName.Trim(),
            request.LastName.Trim(),
            newEmail,
            string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            request.StartDate,
            now);

        employee.UpdatePersonalDetails(
            request.PreferredName,
            request.DateOfBirth,
            request.Nationality,
            request.Gender,
            request.GenderOther,
            now);

        employee.Assign(request.DepartmentId, request.PositionProfileId, employee.ManagerId, now);
        employee.SetSystemAccess(request.HasSystemAccess, now);
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateEmployeeProfileResponse(
            employee.Id,
            employee.CompanyId,
            employee.DepartmentId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.Status,
            employee.HasSystemAccess,
            employee.UpdatedAt));
    }
}
