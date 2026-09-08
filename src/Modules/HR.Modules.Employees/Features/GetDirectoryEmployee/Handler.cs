using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetDirectoryEmployee;

internal sealed class GetDirectoryEmployeeHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IProfilePhotoReader _profilePhotoReader;

    public GetDirectoryEmployeeHandler(
        EmployeesDbContext dbContext,
        IProfilePhotoReader profilePhotoReader)
    {
        _dbContext = dbContext;
        _profilePhotoReader = profilePhotoReader;
    }

    public async Task<Result<GetDirectoryEmployeeResponse>> HandleAsync(
        GetDirectoryEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        // A non-active or cross-company employee is simply "not found" for the directory.
        var result = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == request.Id
                && e.CompanyId == request.CompanyId
                && e.Status == EmploymentStatus.Active)
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.WorkEmail,
                WorkPhone = e.PhoneNumber,
                e.StartDate,
                e.DepartmentId,
                e.LocationId,
                e.ManagerId,
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
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return Result.Failure<GetDirectoryEmployeeResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        var photoUrls = await _profilePhotoReader.GetCurrentPhotoUrlsAsync(
            request.CompanyId, [result.Id], cancellationToken);

        return Result.Success(new GetDirectoryEmployeeResponse(
            result.Id,
            result.FirstName,
            result.LastName,
            result.PreferredName,
            result.PositionTitle,
            result.DepartmentId,
            result.DepartmentName,
            result.LocationId,
            result.LocationName,
            result.WorkEmail,
            result.WorkPhone,
            result.StartDate,
            result.ManagerId,
            result.ManagerFullName,
            photoUrls.TryGetValue(result.Id, out var photoUrl) ? photoUrl : null));
    }
}
