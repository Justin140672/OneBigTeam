using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class CompanyDefaultDataSeeder(
    EmployeesDbContext dbContext,
    IClock clock,
    ILeavePolicyProvisioner leavePolicyProvisioner,
    ILeaveTypeDefaultsProvisioner leaveTypeDefaultsProvisioner,
    ISicknessCategoryDefaultsProvisioner sicknessCategoryDefaultsProvisioner,
    IDocumentTypeDefaultsProvisioner documentTypeDefaultsProvisioner) : ICompanyDefaultDataSeeder
{
    internal const string HomeLocationName = "Home";
    internal const string HomeLocationTypeName = "Remote";

    public async Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var department = await dbContext.Departments
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Name == "General", cancellationToken);
        if (department is null)
        {
            department = Department.Create(Guid.NewGuid(), companyId, "General", null, now);
            dbContext.Departments.Add(department);
        }

        var location = await EnsureLocationAsync(companyId, "Office", "Head Office", now, cancellationToken);
        await EnsureLocationAsync(companyId, HomeLocationTypeName, HomeLocationName, now, cancellationToken);

        var employmentType = await EnsureEmploymentTypeAsync(companyId, "Permanent", now, cancellationToken);
        await EnsureEmploymentTypeAsync(companyId, "Fixed Term", now, cancellationToken);
        await EnsureEmploymentTypeAsync(companyId, "Contractor", now, cancellationToken);
        await EnsureEmploymentTypeAsync(companyId, "Casual", now, cancellationToken);
        await EnsureEmploymentTypeAsync(companyId, "Apprentice", now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var defaultLeavePolicyId = await leavePolicyProvisioner.EnsureDefaultLeavePolicyAsync(companyId, cancellationToken);

        await leaveTypeDefaultsProvisioner.EnsureDefaultLeaveTypesAsync(companyId, cancellationToken);
        await sicknessCategoryDefaultsProvisioner.EnsureDefaultSicknessCategoriesAsync(companyId, cancellationToken);
        await documentTypeDefaultsProvisioner.EnsureDefaultDocumentTypesAsync(companyId, cancellationToken);

        var positionProfile = await dbContext.PositionProfiles
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Title == "Administrator", cancellationToken);
        if (positionProfile is null)
        {
            positionProfile = PositionProfile.Create(
            Guid.NewGuid(),
            companyId,
            department.Id,
            location.Id,
            "Administrator",
            probationMonthsOverride: null,
            workingDaysOverride: null,
            hoursPerDayOverride: null,
            salaryMin: null,
            salaryMax: null,
            salaryType: null,
            defaultLeavePolicyId,
            now);
            dbContext.PositionProfiles.Add(positionProfile);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompanyDefaultDataResult(department.Id, location.Id, positionProfile.Id, employmentType.Id);
    }

    private async Task<EmploymentType> EnsureEmploymentTypeAsync(
        Guid companyId, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await dbContext.EmploymentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = EmploymentType.Create(Guid.NewGuid(), companyId, name, null, now);
        dbContext.EmploymentTypes.Add(created);
        return created;
    }

    private async Task<Location> EnsureLocationAsync(
        Guid companyId, string typeName, string locationName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existingLocation = await dbContext.Locations
            .SingleOrDefaultAsync(l => l.CompanyId == companyId && l.Name == locationName, cancellationToken);
        if (existingLocation is not null)
        {
            return existingLocation;
        }

        var locationType = await dbContext.LocationTypes
            .SingleOrDefaultAsync(t => t.CompanyId == companyId && t.Name == typeName, cancellationToken);
        if (locationType is null)
        {
            locationType = LocationType.Create(Guid.NewGuid(), companyId, typeName, null, now);
            dbContext.LocationTypes.Add(locationType);
        }

        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, locationName, null, now);
        dbContext.Locations.Add(location);
        return location;
    }
}
