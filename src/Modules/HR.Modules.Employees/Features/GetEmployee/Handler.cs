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
        var result = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == request.Id && e.CompanyId == request.CompanyId)
            .Select(e => new
            {
                e.Id,
                e.CompanyId,
                e.DepartmentId,
                e.PositionProfileId,
                e.ManagerId,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.WorkEmail,
                e.PersonalEmail,
                e.StartDate,
                e.DateOfBirth,
                e.Nationality,
                e.Gender,
                e.GenderOther,
                e.PhoneNumber,
                e.HomePhone,
                e.AddressLine1,
                e.AddressLine2,
                e.City,
                e.County,
                e.PostCode,
                e.Country,
                e.Status,
                e.HasSystemAccess,
                e.WorkingDaysOverride,
                e.HoursPerDayOverride,
                e.EmployeeNumber,
                e.EmploymentTypeId,
                EmploymentTypeName = _dbContext.EmploymentTypes
                    .Where(t => t.Id == e.EmploymentTypeId)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                e.ContinuousServiceDate,
                e.ProbationEndDate,
                e.LeavingDate,
                e.Notes,
                e.CreatedAt,
                e.UpdatedAt,
                DepartmentName = _dbContext.Departments
                    .Where(d => d.Id == e.DepartmentId)
                    .Select(d => d.Name)
                    .FirstOrDefault(),
                PositionTitle = _dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.Title)
                    .FirstOrDefault(),
                ManagerFullName = _dbContext.Employees
                    .Where(m => m.Id == e.ManagerId)
                    .Select(m => m.FirstName + " " + m.LastName)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return Result.Failure<GetEmployeeResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        return Result.Success(new GetEmployeeResponse(
            result.Id,
            result.CompanyId,
            result.DepartmentId,
            result.DepartmentName,
            result.PositionProfileId,
            result.PositionTitle,
            result.ManagerId,
            result.ManagerFullName,
            result.FirstName,
            result.LastName,
            result.PreferredName,
            result.WorkEmail,
            result.PersonalEmail,
            result.StartDate,
            result.DateOfBirth,
            result.Nationality,
            result.Gender,
            result.GenderOther,
            result.PhoneNumber,
            result.HomePhone,
            result.AddressLine1,
            result.AddressLine2,
            result.City,
            result.County,
            result.PostCode,
            result.Country,
            result.Status,
            result.HasSystemAccess,
            result.WorkingDaysOverride,
            result.HoursPerDayOverride,
            result.EmployeeNumber,
            result.EmploymentTypeId,
            result.EmploymentTypeName,
            result.ContinuousServiceDate,
            result.ProbationEndDate,
            result.LeavingDate,
            result.Notes,
            result.CreatedAt,
            result.UpdatedAt));
    }
}
