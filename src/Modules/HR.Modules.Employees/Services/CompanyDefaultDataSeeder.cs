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
    ILeavePolicyProvisioner leavePolicyProvisioner,
    ILeaveTypeDefaultsProvisioner leaveTypeDefaultsProvisioner,
    ISicknessCategoryDefaultsProvisioner sicknessCategoryDefaultsProvisioner,
    IDocumentTypeDefaultsProvisioner documentTypeDefaultsProvisioner) : ICompanyDefaultDataSeeder
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

        // Full default set (matches the dev/E2E seed data's canonical Employment Types exactly —
        // see EmployeesModule's own seed block) rather than a single placeholder "Full-time" type.
        // "Permanent" is designated the default assigned to the admin employee created immediately
        // after this returns, same role "Full-time" previously played.
        var employmentTypePermanent  = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, now);
        var employmentTypeFixedTerm  = EmploymentType.Create(Guid.NewGuid(), companyId, "Fixed Term", null, now);
        var employmentTypeContractor = EmploymentType.Create(Guid.NewGuid(), companyId, "Contractor", null, now);
        var employmentTypeCasual     = EmploymentType.Create(Guid.NewGuid(), companyId, "Casual", null, now);
        var employmentTypeApprentice = EmploymentType.Create(Guid.NewGuid(), companyId, "Apprentice", null, now);
        dbContext.EmploymentTypes.AddRange(
            employmentTypePermanent, employmentTypeFixedTerm, employmentTypeContractor,
            employmentTypeCasual, employmentTypeApprentice);
        var employmentType = employmentTypePermanent;

        // Persist these first so the leave-policy provisioner's own SaveChangesAsync (a different
        // DbContext/connection — no cross-module transaction) can't race ahead of them, and so the
        // PositionProfile insert below has valid Department/Location ids to reference.
        await dbContext.SaveChangesAsync(cancellationToken);

        var defaultLeavePolicyId = await leavePolicyProvisioner.EnsureDefaultLeavePolicyAsync(companyId, cancellationToken);

        // Same "genuine pre-existing gap" rationale as ILeavePolicyProvisioner's own doc comment —
        // a brand-new company previously got no default Leave Types, Sickness Categories, or
        // Document Types at all. These three don't block PositionProfile creation the way the
        // leave policy does, so they're fire-and-forget-safe here (order relative to the
        // PositionProfile insert below doesn't matter), but are still awaited so a failure here
        // surfaces clearly rather than as a silent gap discovered later.
        await leaveTypeDefaultsProvisioner.EnsureDefaultLeaveTypesAsync(companyId, cancellationToken);
        await sicknessCategoryDefaultsProvisioner.EnsureDefaultSicknessCategoriesAsync(companyId, cancellationToken);
        await documentTypeDefaultsProvisioner.EnsureDefaultDocumentTypesAsync(companyId, cancellationToken);

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
