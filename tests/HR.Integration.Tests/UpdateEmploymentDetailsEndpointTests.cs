using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateEmploymentDetailsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("dddddddd-0000-0000-0000-000000000004");

    public UpdateEmploymentDetailsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Employment_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/employment",
            new { employeeNumber = "EMP-001", employmentTypeId = (Guid?)null, status = "Active", startDate = "2026-01-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Updates_Employment_Details()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                continuousServiceDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.NotNull(payload);
        Assert.Equal("EMP-001", payload!.EmployeeNumber);
        Assert.Equal("Active", payload.Status);
        Assert.Equal(new DateOnly(2026, 1, 15), payload.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 15), payload.ContinuousServiceDate);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_EmployeeNumber_Is_Missing()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_EmploymentTypeId_Is_Empty_Guid()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = Guid.Empty,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Persists_NoticePeriodOverride()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = 4
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Weeks", payload!.NoticePeriodUnitOverride);
        Assert.Equal(4, payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_Only_NoticePeriodUnitOverride_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = (int?)null
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_Only_NoticePeriodLengthOverride_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = (string?)null,
                noticePeriodLengthOverride = 4
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_Conflict_When_EmployeeNumber_Already_Used_By_Another_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employee1 = await CreateEmployeeAsync(client, companyId);
        var employee2 = await CreateEmployeeAsync(client, companyId);

        var setNumberResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee2.Id}/employment",
            new
            {
                companyId,
                id = employee2.Id,
                employeeNumber = "EMP-TAKEN",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });
        Assert.Equal(HttpStatusCode.OK, setNumberResponse.StatusCode);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee1.Id}/employment",
            new
            {
                companyId,
                id = employee1.Id,
                employeeNumber = "EMP-TAKEN",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_EmployeeNumber_Has_Invalid_Format()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP@001!",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Profile_Then_Put_Employment_With_Same_Location_Does_Not_Revert_Location()
    {
        // Locks in last-write-wins persistence at the API layer, matching the EmployeeEmploymentTab
        // fix: EmployeeEdit.razor's combined Save flow calls UpdateEmployeeProfile (which correctly
        // saves a new Location) and then immediately calls the Employment tab's SaveAsync, which
        // now synchronises its own Model.LocationId to the just-saved value before submitting
        // UpdateEmploymentDetailsRequest, instead of resubmitting a stale copy that silently
        // reverted the just-saved Location.
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<EmployeeRef>())!.Id;

        var newLocResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"NewLoc {Guid.NewGuid():N}", locationTypeId });
        newLocResp.EnsureSuccessStatusCode();
        var newLocationId = (await newLocResp.Content.ReadFromJsonAsync<EmployeeRef>())!.Id;

        // Step 1: UpdateEmployeeProfile saves the new Location (simulates EmployeeEdit.razor's
        // SaveCoreAsync calling UpdateEmployeeProfileAsync first).
        var profileResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            new
            {
                companyId,
                id = employee.Id,
                locationId = newLocationId,
                firstName = "Test",
                lastName = "Employee",
                workEmail = $"test.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-15"
            });
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

        // Step 2: UpdateEmploymentDetails is called immediately after with the SAME location value
        // — matching what the fixed EmployeeEmploymentTab.SyncSharedAssignmentFields now does —
        // rather than a stale/older LocationId that would silently revert step 1's change.
        var employmentResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                locationId = newLocationId,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });
        Assert.Equal(HttpStatusCode.OK, employmentResponse.StatusCode);

        var payload = await employmentResponse.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newLocationId, payload!.LocationId);

        // Re-fetch the profile to double-check the location wasn't reverted on the profile side either.
        var profilePayload = await profileResponse.Content.ReadFromJsonAsync<EmployeeProfilePayload>();
        Assert.NotNull(profilePayload);
        Assert.Equal(newLocationId, profilePayload!.LocationId);
    }

    [Fact]
    public async Task Put_Employment_Returns_NotFound_For_Unknown_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User4, SystemRoles.HrAdministrator, companyId);

        var unknownId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{unknownId}/employment",
            new
            {
                companyId,
                id = unknownId,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<EmployeeRef> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        // These tests set/correct an explicit employee number via PUT .../employment, which is only
        // permitted in Manual employee-number mode (the default is Automatic).
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Test", "Employee", $"test.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeRef>())!;
    }

    private sealed record EmployeeRef(Guid Id);

    private sealed record EmploymentPayload(
        Guid Id,
        Guid CompanyId,
        string? EmployeeNumber,
        Guid? EmploymentTypeId,
        Guid? LocationId,
        string Status,
        DateOnly StartDate,
        DateOnly? ContinuousServiceDate,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride,
        DateTimeOffset UpdatedAt);

    private sealed record EmployeeProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        Guid? LocationId,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        bool HasSystemAccess,
        DateTimeOffset UpdatedAt);
}
