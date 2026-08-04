using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Implements ICompanyDefaultDataSeeder for HR.Modules.Identity's self-service SignUp feature —
/// seeds the minimum default setup data a brand-new company needs so its first (admin) Employee
/// record can be created via CreateEmployeeHandler, whose validator requires a Department,
/// Location, PositionProfile, and EmploymentType id.
///
/// PositionProfile.DefaultLeavePolicyId is a required, non-nullable FK into the Leave module's own
/// schema (a cross-module concept — see ILeavePolicyProvisioner's doc comment for why that contract
/// exists) — sequenced here as: LocationType -> Location, Department, EmploymentType (independent),
/// then the Leave module's default policy, then PositionProfile (needs Department + Location +
/// the leave policy id) last.
/// </summary>
internal sealed class CompanyDefaultDataSeeder(
    EmployeesDbContext dbContext,
    IClock clock,
    ILeavePolicyProvisioner leavePolicyProvisioner) : ICompanyDefaultDataSeeder
{
    public async Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var department = Department.Create(Guid.NewGuid(), companyId, "General", null, now);
        dbContext.Departments.Add(department);

        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        dbContext.LocationTypes.Add(locationType);

        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        dbContext.Locations.Add(location);

        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Full-time", null, now);
        dbContext.EmploymentTypes.Add(employmentType);

        // Persist these first so the leave-policy provisioner's own SaveChangesAsync (a different
        // DbContext/connection — no cross-module transaction) can't race ahead of them, and so the
        // PositionProfile insert below has valid Department/Location ids to reference.
        await dbContext.SaveChangesAsync(cancellationToken);

        var defaultLeavePolicyId = await leavePolicyProvisioner.EnsureDefaultLeavePolicyAsync(companyId, cancellationToken);

        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(),
            companyId,
            department.Id,
            location.Id,
            "Administrator",
            description: null,
            probationMonthsOverride: null,
            workingDaysOverride: null,
            hoursPerDayOverride: null,
            salaryMin: null,
            salaryMax: null,
            salaryType: null,
            defaultLeavePolicyId,
            now);

        dbContext.PositionProfiles.Add(positionProfile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompanyDefaultDataResult(department.Id, location.Id, positionProfile.Id, employmentType.Id);
    }
}
