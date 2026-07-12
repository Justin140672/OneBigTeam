using HR.Modules.Employees.Domain;
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
                e.LocationId,
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
                LocationName = _dbContext.Locations
                    .Where(l => l.Id == e.LocationId)
                    .Select(l => l.Name)
                    .FirstOrDefault(),
                PositionTitle = _dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.Title)
                    .FirstOrDefault(),
                ManagerFullName = _dbContext.Employees
                    .Where(m => m.Id == e.ManagerId)
                    .Select(m => m.FirstName + " " + m.LastName)
                    .FirstOrDefault(),
                DirectReportsCount = _dbContext.Employees
                    .Count(r => r.ManagerId == e.Id && r.Status != EmploymentStatus.Terminated)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return Result.Failure<GetEmployeeResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        var reportingChain = await BuildReportingChainAsync(
            request.CompanyId, result.Id, result.ManagerId, cancellationToken);

        return Result.Success(new GetEmployeeResponse(
            result.Id,
            result.CompanyId,
            result.DepartmentId,
            result.DepartmentName,
            result.LocationId,
            result.LocationName,
            result.PositionProfileId,
            result.PositionTitle,
            result.ManagerId,
            result.ManagerFullName,
            result.DirectReportsCount,
            reportingChain,
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

    // Walks the ManagerId chain from the employee's own manager up to the root, using an
    // in-memory visited set so a corrupt/circular manager reference can't cause an infinite loop.
    private async Task<IReadOnlyList<ReportingChainItem>> BuildReportingChainAsync(
        Guid companyId, Guid employeeId, Guid? managerId, CancellationToken cancellationToken)
    {
        if (managerId is null)
            return [];

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.ManagerId, e.PositionProfileId })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var positionTitles = await _dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var chain = new List<ReportingChainItem>();
        var visited = new HashSet<Guid> { employeeId };
        var currentManagerId = managerId;

        while (currentManagerId is Guid id && visited.Add(id) && employees.TryGetValue(id, out var manager))
        {
            var jobTitle = manager.PositionProfileId is Guid profileId && positionTitles.TryGetValue(profileId, out var title)
                ? title
                : null;

            chain.Add(new ReportingChainItem(manager.Id, $"{manager.FirstName} {manager.LastName}", jobTitle));
            currentManagerId = manager.ManagerId;
        }

        chain.Reverse();
        return chain;
    }
}
