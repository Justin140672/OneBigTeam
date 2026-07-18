using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Resolves Department/EmploymentType/Location/PositionProfile references supplied by name/title
/// during employee import validation, auto-creating any that do not already exist for the company.
/// Scoped per request; small in-memory caches avoid redundant lookups for names repeated across
/// many rows of the same import file.
/// </summary>
internal sealed class ImportLookupResolver(EmployeesDbContext dbContext, IClock clock) : IImportLookupResolver
{
    private const string DefaultLocationTypeName = "General";

    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _departmentCache = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _employmentTypeCache = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _locationCache = new();
    private readonly Dictionary<Guid, Guid> _defaultLocationTypeCache = new();

    public async Task<ImportLookupResult> GetOrCreateDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var cacheKey = (companyId, trimmed.ToLowerInvariant());

        if (_departmentCache.TryGetValue(cacheKey, out var cachedId))
            return new ImportLookupResult(cachedId, WasCreated: false);

        var existing = await dbContext.Departments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.Name.ToLower() == trimmed.ToLower())
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            _departmentCache[cacheKey] = existing.Value;
            return new ImportLookupResult(existing.Value, WasCreated: false);
        }

        var now = clock.UtcNowOffset();
        var department = Department.Create(Guid.NewGuid(), companyId, trimmed, description: null, now);

        dbContext.Departments.Add(department);
        await dbContext.SaveChangesAsync(cancellationToken);

        _departmentCache[cacheKey] = department.Id;
        return new ImportLookupResult(department.Id, WasCreated: true);
    }

    public async Task<ImportLookupResult> GetOrCreateEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var cacheKey = (companyId, trimmed.ToLowerInvariant());

        if (_employmentTypeCache.TryGetValue(cacheKey, out var cachedId))
            return new ImportLookupResult(cachedId, WasCreated: false);

        var existing = await dbContext.EmploymentTypes
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Name.ToLower() == trimmed.ToLower())
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            _employmentTypeCache[cacheKey] = existing.Value;
            return new ImportLookupResult(existing.Value, WasCreated: false);
        }

        var now = clock.UtcNowOffset();
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, trimmed, description: null, now);

        dbContext.EmploymentTypes.Add(employmentType);
        await dbContext.SaveChangesAsync(cancellationToken);

        _employmentTypeCache[cacheKey] = employmentType.Id;
        return new ImportLookupResult(employmentType.Id, WasCreated: true);
    }

    public async Task<ImportLookupResult> GetOrCreateLocationAsync(Guid companyId, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var cacheKey = (companyId, trimmed.ToLowerInvariant());

        if (_locationCache.TryGetValue(cacheKey, out var cachedId))
            return new ImportLookupResult(cachedId, WasCreated: false);

        var existing = await dbContext.Locations
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.Name.ToLower() == trimmed.ToLower())
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            _locationCache[cacheKey] = existing.Value;
            return new ImportLookupResult(existing.Value, WasCreated: false);
        }

        var locationTypeId = await GetOrCreateDefaultLocationTypeIdAsync(companyId, cancellationToken);

        var now = clock.UtcNowOffset();
        var location = Location.Create(Guid.NewGuid(), companyId, locationTypeId, trimmed, description: null, now);

        dbContext.Locations.Add(location);
        await dbContext.SaveChangesAsync(cancellationToken);

        _locationCache[cacheKey] = location.Id;
        return new ImportLookupResult(location.Id, WasCreated: true);
    }

    public async Task<PositionProfileImportLookupResult> GetOrCreatePositionProfileAsync(
        Guid companyId, string title, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken)
    {
        var trimmed = title.Trim();

        var existing = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Title.ToLower() == trimmed.ToLower())
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
            return new PositionProfileImportLookupResult(existing.Value, WasCreated: false, Skipped: false);

        // Department, Location and DefaultLeavePolicyId are now mandatory on PositionProfile. The
        // import flow has no source for a default leave policy, so a brand-new position profile can
        // never be safely auto-created here — always skip, same as the existing missing
        // department/location guard. Callers must create the position profile (with a leave policy)
        // through CreatePositionProfile before importing employees who reference it by title.
        return new PositionProfileImportLookupResult(Id: null, WasCreated: false, Skipped: true);
    }

    private async Task<Guid> GetOrCreateDefaultLocationTypeIdAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_defaultLocationTypeCache.TryGetValue(companyId, out var cachedId))
            return cachedId;

        var existing = await dbContext.LocationTypes
            .AsNoTracking()
            .Where(lt => lt.CompanyId == companyId && lt.Name.ToLower() == DefaultLocationTypeName.ToLower())
            .Select(lt => (Guid?)lt.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            _defaultLocationTypeCache[companyId] = existing.Value;
            return existing.Value;
        }

        var now = clock.UtcNowOffset();
        var locationType = LocationType.Create(
            Guid.NewGuid(),
            companyId,
            DefaultLocationTypeName,
            "Default location type auto-created for imported locations without an explicit type.",
            now);

        dbContext.LocationTypes.Add(locationType);
        await dbContext.SaveChangesAsync(cancellationToken);

        _defaultLocationTypeCache[companyId] = locationType.Id;
        return locationType.Id;
    }
}
