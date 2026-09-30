using System.Net.Http.Json;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class EmployeeReferenceDataSeeder
{
    public sealed record ReferenceData(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId);

    public static async Task<ReferenceData> SeedAsync(ApiWebApplicationFactory factory, Guid companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await SeedAsync(db, companyId);
    }

    public static async Task<ReferenceData> SeedAsync(EmployeesDbContext db, Guid companyId)
    {
        var now = DateTimeOffset.UtcNow;

        var department = Department.Create(Guid.NewGuid(), companyId, $"Dept-{Guid.NewGuid():N}", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, $"LocType-{Guid.NewGuid():N}", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, $"Loc-{Guid.NewGuid():N}", null, now);
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, $"Role-{Guid.NewGuid():N}", probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), now);
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, $"EmpType-{Guid.NewGuid():N}", null, now);

        db.Departments.Add(department);
        db.LocationTypes.Add(locationType);
        db.Locations.Add(location);
        db.PositionProfiles.Add(positionProfile);
        db.EmploymentTypes.Add(employmentType);

        await db.SaveChangesAsync();

        return new ReferenceData(department.Id, location.Id, positionProfile.Id, employmentType.Id);
    }

    public static async Task<ReferenceData> SeedViaApiAsync(HttpClient client, Guid companyId)
    {
        var departmentResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"Dept-{Guid.NewGuid():N}"
        });
        departmentResponse.EnsureSuccessStatusCode();
        var departmentId = (await departmentResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = $"LocType-{Guid.NewGuid():N}"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationTypeId = (await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locationResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = $"Loc-{Guid.NewGuid():N}",
            locationTypeId
        });
        locationResponse.EnsureSuccessStatusCode();
        var locationId = (await locationResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"LeavePolicy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var positionProfileResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            title = $"Role-{Guid.NewGuid():N}",
            defaultLeavePolicyId
        });
        positionProfileResponse.EnsureSuccessStatusCode();
        var positionProfileId = (await positionProfileResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var employmentTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = $"EmpType-{Guid.NewGuid():N}"
        });
        employmentTypeResponse.EnsureSuccessStatusCode();
        var employmentTypeId = (await employmentTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return new ReferenceData(departmentId, locationId, positionProfileId, employmentTypeId);
    }

    public static object BuildCreateEmployeeRequest(
        Guid companyId,
        ReferenceData referenceData,
        string firstName,
        string lastName,
        string workEmail,
        string? employeeNumber = null,
        DateOnly? startDate = null,
        DateOnly? dateOfBirth = null,
        string nationality = "British",
        string gender = "Prefer not to say",
        Guid? managerId = null) =>
        new
        {
            companyId,
            firstName,
            lastName,
            workEmail,
            startDate = (startDate ?? new DateOnly(2026, 7, 1)).ToString("yyyy-MM-dd"),
            dateOfBirth = (dateOfBirth ?? new DateOnly(1990, 1, 1)).ToString("yyyy-MM-dd"),
            nationality,
            gender,
            employeeNumber = employeeNumber ?? $"EMP-{Guid.NewGuid():N}",
            departmentId = referenceData.DepartmentId,
            locationId = referenceData.LocationId,
            positionProfileId = referenceData.PositionProfileId,
            employmentTypeId = referenceData.EmploymentTypeId,
            managerId,
        };

    public static async Task SetEmployeeNumberModeManualAsync(HttpClient client, Guid companyId)
    {
        for (var version = 0; version < 10; version++)
        {
            var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
            {
                id = companyId,
                workingDays = 31,
                hoursPerDay = 7.5,
                leaveYearStartMonth = 1,
                defaultHolidayAllowance = 25,
                probationMonths = 6,
                employeeNumberMode = "Manual",
                employeeNumberPrefix = (string?)null,
                nextEmployeeNumber = 1,
                employeeNumberMinimumLength = 1,
                version
            });

            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
                response.EnsureSuccessStatusCode();
        }
    }

    private sealed record IdPayload(Guid Id);
}
