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
    public async Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var department = Department.Create(Guid.NewGuid(), companyId, "General", null, now);
        dbContext.Departments.Add(department);

        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        dbContext.LocationTypes.Add(locationType);

        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        dbContext.Locations.Add(location);

        var employmentTypePermanent  = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, now);
        var employmentTypeFixedTerm  = EmploymentType.Create(Guid.NewGuid(), companyId, "Fixed Term", null, now);
        var employmentTypeContractor = EmploymentType.Create(Guid.NewGuid(), companyId, "Contractor", null, now);
        var employmentTypeCasual     = EmploymentType.Create(Guid.NewGuid(), companyId, "Casual", null, now);
        var employmentTypeApprentice = EmploymentType.Create(Guid.NewGuid(), companyId, "Apprentice", null, now);
        dbContext.EmploymentTypes.AddRange(
            employmentTypePermanent, employmentTypeFixedTerm, employmentTypeContractor,
            employmentTypeCasual, employmentTypeApprentice);
        var employmentType = employmentTypePermanent;

        await dbContext.SaveChangesAsync(cancellationToken);

        var defaultLeavePolicyId = await leavePolicyProvisioner.EnsureDefaultLeavePolicyAsync(companyId, cancellationToken);

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
