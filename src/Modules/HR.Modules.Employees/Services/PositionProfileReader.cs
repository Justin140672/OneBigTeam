using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class PositionProfileReader(EmployeesDbContext dbContext)
    : IPositionProfileReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        return dbContext.PositionProfiles
            .AsNoTracking()
            .AnyAsync(
                p => p.Id == positionProfileId &&
                     p.CompanyId == companyId &&
                     p.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId,
        Guid? departmentId,
        string title,
        CancellationToken cancellationToken)
    {
        var normalizedTitle = title.Trim();

        var query = dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p =>
                p.CompanyId == companyId &&
                p.IsActive &&
                p.Title.ToLower() == normalizedTitle.ToLower());

        if (departmentId.HasValue)
            query = query.Where(p => p.DepartmentId == departmentId.Value);

        return await query
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        return dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.Id == positionProfileId && p.CompanyId == companyId && p.IsActive)
            .Select(p => p.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        // Deliberately not filtered by IsActive — see PositionProfileSummary's remarks: read-time
        // summaries must still resolve for profiles that have since been deactivated. LocationName is
        // resolved here via a same-schema left join against employees.locations (Location is owned by
        // this module too), never filtered by Location.IsActive for the same "still resolve for
        // historical/deactivated records" reason.
        return (
                from p in dbContext.PositionProfiles.AsNoTracking()
                where p.Id == positionProfileId && p.CompanyId == companyId
                join l in dbContext.Locations.AsNoTracking() on p.LocationId equals l.Id into locations
                from location in locations.DefaultIfEmpty()
                select new PositionProfileSummary(
                    p.Id, p.Title, p.DepartmentId, p.Description, p.IsActive, p.LocationId, location.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken)
    {
        if (positionProfileIds.Count == 0)
            return [];

        return await (
                from p in dbContext.PositionProfiles.AsNoTracking()
                where p.CompanyId == companyId && positionProfileIds.Contains(p.Id)
                join l in dbContext.Locations.AsNoTracking() on p.LocationId equals l.Id into locations
                from location in locations.DefaultIfEmpty()
                select new PositionProfileSummary(
                    p.Id, p.Title, p.DepartmentId, p.Description, p.IsActive, p.LocationId, location.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(
        Guid companyId, Guid departmentId, CancellationToken cancellationToken)
    {
        return await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.DepartmentId == departmentId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(
        Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        return (
                from p in dbContext.PositionProfiles.AsNoTracking()
                where p.Id == positionProfileId && p.CompanyId == companyId
                join l in dbContext.Locations.AsNoTracking() on p.LocationId equals l.Id into locations
                from location in locations.DefaultIfEmpty()
                select new PositionProfileEmploymentDefaults(
                    p.Id,
                    p.Title,
                    p.SalaryMin,
                    p.SalaryMax,
                    p.SalaryType == null ? null : p.SalaryType.ToString(),
                    p.WorkingDaysOverride,
                    p.HoursPerDayOverride,
                    p.ProbationMonthsOverride,
                    p.DefaultLeavePolicyId,
                    p.LocationId,
                    location.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
