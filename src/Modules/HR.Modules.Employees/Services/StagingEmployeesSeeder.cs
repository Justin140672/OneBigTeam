using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Employees.Services;

internal static class StagingEmployeesSeeder
{
    private const int DepartmentKind = 0x01;
    private const int PositionKind = 0x02;
    private const int EmployeeKind = 0x03;
    private const int EmploymentTypeKind = 0x04;
    private const int LocationTypeKind = 0x06;
    private const int LocationKind = 0x07;

    public static Guid EmployeeId(int number) => StagingId(EmployeeKind, number);

    public static Guid PositionProfileId(int number) => StagingId(PositionKind, number);

    public static async Task SeedAsync(IServiceProvider services, StagingSeedOptions options)
    {
        var db = services.GetRequiredService<EmployeesDbContext>();
        var companyId = StagingSeedOptions.CompanyId;
        var now = DateTimeOffset.UtcNow;

        var leavePolicyId = await services.GetRequiredService<ILeavePolicyProvisioner>()
            .EnsureDefaultLeavePolicyAsync(companyId, CancellationToken.None);
        await services.GetRequiredService<ILeaveTypeDefaultsProvisioner>()
            .EnsureDefaultLeaveTypesAsync(companyId, CancellationToken.None);
        await services.GetRequiredService<ISicknessCategoryDefaultsProvisioner>()
            .EnsureDefaultSicknessCategoriesAsync(companyId, CancellationToken.None);
        await services.GetRequiredService<IDocumentTypeDefaultsProvisioner>()
            .EnsureDefaultDocumentTypesAsync(companyId, CancellationToken.None);

        if (await db.Employees.AnyAsync(e => e.CompanyId == companyId))
        {
            return;
        }

        var employmentTypeIds = new Dictionary<string, Guid>();
        for (var i = 0; i < StagingOrgDefinition.EmploymentTypeNames.Count; i++)
        {
            var name = StagingOrgDefinition.EmploymentTypeNames[i];
            var id = StagingId(EmploymentTypeKind, i + 1);
            employmentTypeIds[name] = id;
            db.EmploymentTypes.Add(EmploymentType.Create(id, companyId, name, null, now));
        }

        var departmentIds = new Dictionary<string, Guid>();
        foreach (var department in StagingOrgDefinition.Departments)
        {
            var id = StagingId(DepartmentKind, department.Number);
            departmentIds[department.Name] = id;
            db.Departments.Add(Department.Create(id, companyId, department.Name, department.Description, now));
        }

        var officeTypeId = StagingId(LocationTypeKind, 1);
        var remoteTypeId = StagingId(LocationTypeKind, 2);
        var officeId = StagingId(LocationKind, 1);
        db.LocationTypes.AddRange(
            LocationType.Create(officeTypeId, companyId, StagingOrgDefinition.OfficeLocationTypeName, null, now),
            LocationType.Create(remoteTypeId, companyId, StagingOrgDefinition.RemoteLocationTypeName, null, now));
        db.Locations.AddRange(
            Location.Create(officeId, companyId, officeTypeId, StagingOrgDefinition.OfficeLocationName, null, now),
            Location.Create(StagingId(LocationKind, 2), companyId, remoteTypeId, StagingOrgDefinition.HomeLocationName, null, now));

        var positions = new Dictionary<string, (Guid Id, Guid DepartmentId)>();
        foreach (var position in StagingOrgDefinition.Positions)
        {
            var id = StagingId(PositionKind, position.Number);
            var departmentId = departmentIds[position.DepartmentName];
            positions[position.Name] = (id, departmentId);
            db.PositionProfiles.Add(PositionProfile.Create(
                id, companyId, departmentId, officeId, position.Name,
                null, null, null, null, null, null, null, leavePolicyId, now));
        }

        var hrManagerId = EmployeeId(4);

        foreach (var definition in StagingOrgDefinition.Employees)
        {
            var (positionId, departmentId) = positions[definition.PositionName];
            var employmentTypeId = employmentTypeIds[definition.EmploymentTypeName];
            var employeeId = EmployeeId(definition.Number);
            var managerId = definition.ManagerNumber is { } managerNumber ? EmployeeId(managerNumber) : (Guid?)null;
            var employeeNumber = StagingOrgDefinition.EmployeeNumberFor(
                definition.Number, StagingSeedOptions.EmployeeNumberPrefix, StagingSeedOptions.EmployeeNumberMinimumLength);
            var email = StagingOrgDefinition.EmailFor(definition, options.ResolvedEmailDomain);
            var personalEmail = $"{definition.FirstName}.{definition.LastName}@personal.example".ToLowerInvariant();

            var employee = Employee.Create(
                employeeId, companyId, definition.FirstName, definition.LastName, email, definition.StartDate,
                hasSystemAccess: true, definition.DateOfBirth, definition.Nationality, definition.Gender,
                employeeNumber, employmentTypeId, departmentId, officeId, positionId, now);
            employee.Assign(departmentId, positionId, officeId, managerId, now);
            employee.UpdatePersonalDetails(
                definition.FirstName, definition.DateOfBirth, definition.Nationality, definition.Gender, null, now);
            employee.UpdateContactDetails(
                personalEmail, definition.Phone, null, definition.AddressLine1, definition.AddressLine2,
                definition.City, definition.County, definition.PostCode, "United Kingdom", now);
            employee.UpdateEmploymentDetails(
                employeeNumber, employmentTypeId, definition.StartDate, null, null, null, null, now);
            employee.Activate(now);
            db.Employees.Add(employee);

            db.Compensations.Add(Compensation.Create(
                Guid.NewGuid(), companyId, employeeId, definition.StartDate, SalaryType.Annual, definition.Salary,
                "GBP", 37.5m, 1m, "Starting salary", CompensationChangeReason.NewHire, hrManagerId, now));

            db.EmployeeTimelineEntries.Add(EmployeeTimelineEntry.Create(
                Guid.NewGuid(), companyId, employeeId, definition.StartDate,
                EmployeeTimelineEventType.EmployeeJoined, EmployeeTimelineCategory.Employment,
                "Employee joined", "Employee joined the company.",
                performedByUserId: null, "Employees", sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal, now));
        }

        await db.SaveChangesAsync();
    }

    private static Guid StagingId(int kind, int number) =>
        new($"5A0000{kind:X2}-0000-0000-0000-{number:D12}");
}
