using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateEmployeeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid User5 = new("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid User6 = new("aaaaaaaa-0000-0000-0000-000000000006");
    private static readonly Guid User7 = new("aaaaaaaa-0000-0000-0000-000000000007");
    private static readonly Guid User8 = new("aaaaaaaa-0000-0000-0000-000000000008");
    private static readonly Guid User9 = new("aaaaaaaa-0000-0000-0000-000000000009");
    private static readonly Guid User10 = new("aaaaaaaa-0000-0000-0000-000000000010");
    private static readonly Guid User11 = new("aaaaaaaa-0000-0000-0000-000000000011");
    private static readonly Guid User12 = new("aaaaaaaa-0000-0000-0000-000000000012");

    public CreateEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var userId in new[] { User1, User2, User3, User4, User5, User6, User7, User8, User9, User10, User11, User12 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.CompanyAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    private async Task<Guid> CreateCompanyAsync(HttpClient client, string? name = null)
    {
        _ = client;
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, name ?? $"Employee Number Test Co {Guid.NewGuid():N}");
    }

    private static async Task SetEmployeeNumberModeAsync(
        HttpClient client, Guid companyId, string mode, string? prefix = null, int nextEmployeeNumber = 1, int minimumLength = 1)
    {
        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            id = companyId,
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMode = mode,
            employeeNumberPrefix = prefix,
            nextEmployeeNumber,
            employeeNumberMinimumLength = minimumLength
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid companyId, string name = "Head Office")
    {
        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = "Office"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();

        var locationResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name,
            locationTypeId = locationType!.Id
        });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdPayload>();
        return location!.Id;
    }

    private sealed record IdPayload(Guid Id);

    [Fact]
    public async Task Post_Employees_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/employees", new
        {
            firstName = "Alice",
            lastName = "Smith",
            workEmail = "alice@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Active_Status()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Alice", payload.FirstName);
        Assert.Equal("Smith", payload.LastName);
        Assert.Equal("Active", payload.Status);
        Assert.Equal(refData.DepartmentId, payload.DepartmentId);
        Assert.Equal(refData.LocationId, payload.LocationId);
        Assert.Equal(refData.PositionProfileId, payload.PositionProfileId);
        Assert.Null(payload.ManagerId);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Department_PositionProfile_And_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var managerResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Jane", "Manager", $"jane.manager.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 1), dateOfBirth: new DateOnly(1985, 3, 10)));
        managerResponse.EnsureSuccessStatusCode();
        var manager = await managerResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        var deptResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"Engineering {Guid.NewGuid():N}"
        });
        deptResponse.EnsureSuccessStatusCode();
        var dept = await deptResponse.Content.ReadFromJsonAsync<DepartmentPayload>();

        var leavePolicyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"RefLeavePolicy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var leavePolicy = await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>();

        var ppResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = dept!.Id,
            locationId = refData.LocationId,
            title = $"Developer {Guid.NewGuid():N}",
            defaultLeavePolicyId = leavePolicy!.Id
        });
        ppResponse.EnsureSuccessStatusCode();
        var pp = await ppResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = dept!.Id,
            locationId = refData.LocationId,
            positionProfileId = pp!.Id,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = manager!.Id,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(dept.Id, payload!.DepartmentId);
        Assert.Equal(pp.Id, payload.PositionProfileId);
        Assert.Equal(manager.Id, payload.ManagerId);
    }

    [Fact]
    public async Task Post_Employees_Returns_Conflict_For_Duplicate_WorkEmail()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var email = $"duplicate.{Guid.NewGuid():N}@example.com";
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(companyId, refData, "Alice", "Smith", email));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(companyId, refData, "Alice", "Smith", email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Department()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User4, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Location()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User6.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User6, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(locationId, payload!.LocationId);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Location()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User7.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User7, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = Guid.NewGuid(),
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User5, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = Guid.NewGuid(),
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_ValidationError_In_Manual_Mode_When_EmployeeNumber_Omitted()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User8.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Succeeds_In_Manual_Mode_When_EmployeeNumber_Supplied()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User9.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com",
                employeeNumber: "EMP-100"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Generates_EmployeeNumber_In_Automatic_Mode_When_Omitted()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User10.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 125, minimumLength: 5);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);

        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(126, settings!.NextEmployeeNumber);
    }

    [Fact]
    public async Task Post_Employees_Advances_NextEmployeeNumber_On_Each_Automatic_Creation()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User11.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
            {
                companyId,
                departmentId = refData.DepartmentId,
                locationId = refData.LocationId,
                positionProfileId = refData.PositionProfileId,
                employmentTypeId = refData.EmploymentTypeId,
                salary = 50000m,
                salaryFrequency = "Annual",
                currency = "GBP",
                firstName = "Alice",
                lastName = "Smith",
                workEmail = $"alice.{Guid.NewGuid():N}@example.com",
                startDate = "2026-07-01",
                dateOfBirth = "1990-05-20",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Female"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(4, settings!.NextEmployeeNumber);
    }

    [Fact]
    public async Task Post_Employees_Concurrent_Automatic_Creation_Produces_Distinct_EmployeeNumbers()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User12.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User12, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        const int concurrentCreations = 10;

        var tasks = Enumerable.Range(0, concurrentCreations).Select(_ =>
        {
            var concurrentClient = _factory.CreateClient();
            concurrentClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User12.ToString());
            concurrentClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

            return concurrentClient.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
            {
                companyId,
                departmentId = refData.DepartmentId,
                locationId = refData.LocationId,
                positionProfileId = refData.PositionProfileId,
                employmentTypeId = refData.EmploymentTypeId,
                salary = 50000m,
                salaryFrequency = "Annual",
                currency = "GBP",
                firstName = "Alice",
                lastName = "Smith",
                workEmail = $"alice.{Guid.NewGuid():N}@example.com",
                startDate = "2026-07-01",
                dateOfBirth = "1990-05-20",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Female"
            });
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(1 + concurrentCreations, settings!.NextEmployeeNumber);
    }

    [Fact]
    public async Task Post_Employees_Two_Automatic_Companies_Each_Advance_Independently()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyA = await CreateCompanyAsync(client);
        var companyB = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);

        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        var refDataA = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyA);
        await SetEmployeeNumberModeAsync(client, companyA, "Automatic", prefix: "A-", nextEmployeeNumber: 1, minimumLength: 3);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyB.ToString());
        var refDataB = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyB);
        await SetEmployeeNumberModeAsync(client, companyB, "Automatic", prefix: "B-", nextEmployeeNumber: 1, minimumLength: 3);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        var responseA1 = await client.PostAsJsonAsync($"/api/companies/{companyA}/employees", new
        {
            companyId = companyA,
            departmentId = refDataA.DepartmentId,
            locationId = refDataA.LocationId,
            positionProfileId = refDataA.PositionProfileId,
            employmentTypeId = refDataA.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Female"
        });
        Assert.Equal(HttpStatusCode.Created, responseA1.StatusCode);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyB.ToString());
        var responseB1 = await client.PostAsJsonAsync($"/api/companies/{companyB}/employees", new
        {
            companyId = companyB,
            departmentId = refDataB.DepartmentId,
            locationId = refDataB.LocationId,
            positionProfileId = refDataB.PositionProfileId,
            employmentTypeId = refDataB.EmploymentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            firstName = "Bob",
            lastName = "Jones",
            workEmail = $"bob.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Male"
        });
        Assert.Equal(HttpStatusCode.Created, responseB1.StatusCode);

        var settingsAResponse = await client.GetAsync($"/api/companies/{companyB}/hr-settings");
        settingsAResponse.EnsureSuccessStatusCode();
        var settingsB = await settingsAResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settingsB);
        Assert.Equal(2, settingsB!.NextEmployeeNumber);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        var settingsAOnlyResponse = await client.GetAsync($"/api/companies/{companyA}/hr-settings");
        settingsAOnlyResponse.EnsureSuccessStatusCode();
        var settingsA = await settingsAOnlyResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settingsA);
        Assert.Equal(2, settingsA!.NextEmployeeNumber);
    }

    [Fact]
    public async Task Post_Employees_Returns_Conflict_For_Case_Insensitive_Duplicate_EmployeeNumber_In_Same_Company()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                employeeNumber: "EMP-001"));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com",
                employeeNumber: "emp-001"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Allows_Same_EmployeeNumber_In_Different_Companies()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());

        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyA);
        var refDataA = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyA);
        var responseA = await client.PostAsJsonAsync(
            $"/api/companies/{companyA}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyA, refDataA, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                employeeNumber: "EMP-777"));
        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyB.ToString());
        var refDataB = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyB);
        var responseB = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyB, refDataB, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com",
                employeeNumber: "EMP-777"));
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    // -- Starting compensation ----------------------------------------------------------------

    private async Task<(HttpClient Client, Guid CompanyId, EmployeeReferenceDataSeeder.ReferenceData RefData)> SetupHrClientAsync(Guid userId)
    {
        var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        return (client, companyId, refData);
    }

    private static Dictionary<string, object?> ToBody(object request)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }

    private async Task<int> CountCompensationsAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        return await db.Compensations.CountAsync(c => c.CompanyId == companyId);
    }

    private async Task<int> CountEmployeesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        return await db.Employees.CountAsync(e => e.CompanyId == companyId);
    }

    [Fact]
    public async Task Post_Employees_Creates_Initial_NewHire_Compensation_Effective_From_StartDate()
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var startDate = new DateOnly(2020, 3, 15);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                startDate: startDate, salary: 61500.5m, salaryFrequency: "Annual", currency: "GBP"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var employee = (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;

        var currentResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/compensation/current");
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        var current = (await currentResponse.Content.ReadFromJsonAsync<CurrentCompensationPayload>())!;
        Assert.Equal(employee.Id, current.EmployeeId);
        Assert.Equal(61500.5m, current.Salary);
        Assert.Equal("GBP", current.Currency);
        Assert.Equal("Annual", current.SalaryType);
        Assert.Equal(startDate, current.EffectiveFrom);
        Assert.Null(current.EffectiveTo);
        Assert.Equal("NewHire", current.Reason);

        var historyResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/compensation/history");
        historyResponse.EnsureSuccessStatusCode();
        var history = (await historyResponse.Content.ReadFromJsonAsync<CompensationHistoryPayload>())!;
        var record = Assert.Single(history.Items);
        Assert.Equal(current.Id, record.Id);
    }

    [Theory]
    [InlineData("Hourly", 18.75)]
    [InlineData("Daily", 220)]
    [InlineData("annual", 50000)]
    public async Task Post_Employees_Persists_Requested_SalaryFrequency(string frequency, double salary)
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2020, 1, 1), salary: (decimal)salary, salaryFrequency: frequency));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var employee = (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;

        var current = await client.GetFromJsonAsync<CurrentCompensationPayload>(
            $"/api/companies/{companyId}/employees/{employee.Id}/compensation/current");
        Assert.Equal(char.ToUpperInvariant(frequency[0]) + frequency[1..].ToLowerInvariant(), current!.SalaryType);
        Assert.Equal((decimal)salary, current.Salary);
    }

    [Fact]
    public async Task Post_Employees_Normalises_Currency_To_Upper_Case()
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2020, 1, 1), currency: "eur"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var employee = (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;

        var current = await client.GetFromJsonAsync<CurrentCompensationPayload>(
            $"/api/companies/{companyId}/employees/{employee.Id}/compensation/current");
        Assert.Equal("EUR", current!.Currency);
    }

    [Theory]
    [InlineData("salary")]
    [InlineData("salaryFrequency")]
    [InlineData("currency")]
    public async Task Post_Employees_Rejects_Missing_Compensation_Field(string field)
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body.Remove(field);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected a validation failure but got {response.StatusCode}.");
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
        Assert.Equal(0, await CountCompensationsAsync(companyId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public async Task Post_Employees_Rejects_Zero_Or_Negative_Salary(double salary)
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
            salary: (decimal)salary));

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected a validation failure but got {response.StatusCode}.");
        Assert.Contains("salary", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
        Assert.Equal(0, await CountCompensationsAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_Accepts_Smallest_Positive_Salary()
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
                salary: 0.01m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("99")]
    public async Task Post_Employees_Rejects_Invalid_SalaryFrequency(string frequency)
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
            salaryFrequency: frequency));

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected a validation failure but got {response.StatusCode}.");
        Assert.Equal(0, await CountEmployeesAsync(companyId));
        Assert.Equal(0, await CountCompensationsAsync(companyId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GB")]
    [InlineData("GBPX")]
    public async Task Post_Employees_Rejects_Invalid_Currency(string currency)
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
            currency: currency));

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected a validation failure but got {response.StatusCode}.");
        Assert.Equal(0, await CountEmployeesAsync(companyId));
        Assert.Equal(0, await CountCompensationsAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_Failed_Creation_Leaves_No_Extra_Compensation_Record()
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var email = $"duplicate.{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(companyId, refData, "Alice", "Smith", email));
        first.EnsureSuccessStatusCode();
        var firstEmployee = (await first.Content.ReadFromJsonAsync<EmployeePayload>())!;
        Assert.Equal(1, await CountCompensationsAsync(companyId));

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", email, salary: 99999m));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountEmployeesAsync(companyId));
        Assert.Equal(1, await CountCompensationsAsync(companyId));

        var history = (await client.GetFromJsonAsync<CompensationHistoryPayload>(
            $"/api/companies/{companyId}/employees/{firstEmployee.Id}/compensation/history"))!;
        var record = Assert.Single(history.Items);
        Assert.Equal(50000m, record.Salary);
    }

    [Fact]
    public async Task Post_Employees_Failed_Creation_With_Unknown_Department_Leaves_No_Compensation_Record()
    {
        var (client, companyId, refData) = await SetupHrClientAsync(User12);
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body["departmentId"] = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
        Assert.Equal(0, await CountCompensationsAsync(companyId));
    }

    // -- Idempotency-Key (ticket 3, P1 follow-up) --------------------------------------------

    private static HttpRequestMessage BuildIdempotentPostRequest(string url, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Post_Employees_With_Same_IdempotencyKey_And_Same_Body_Returns_Same_Employee_And_Creates_Only_One_Row()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com");

        var firstResponse = await client.SendAsync(BuildIdempotentPostRequest($"/api/companies/{companyId}/employees", body, idempotencyKey));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        var secondResponse = await client.SendAsync(BuildIdempotentPostRequest($"/api/companies/{companyId}/employees", body, idempotencyKey));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.Equal(firstPayload!.Id, secondPayload!.Id);
        Assert.Equal(firstPayload.CompanyId, secondPayload.CompanyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var employees = await db.Employees.Where(e => e.CompanyId == companyId).ToListAsync();
        Assert.Single(employees);
        Assert.Equal(1, await CountCompensationsAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_With_Same_IdempotencyKey_And_Different_Body_Returns_Conflict()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com");
        var differentBody = EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, refData, "Bob", "Jones", $"bob.jones.{Guid.NewGuid():N}@example.com");

        var firstResponse = await client.SendAsync(BuildIdempotentPostRequest($"/api/companies/{companyId}/employees", body, idempotencyKey));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.SendAsync(BuildIdempotentPostRequest($"/api/companies/{companyId}/employees", differentBody, idempotencyKey));
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var employees = await db.Employees.Where(e => e.CompanyId == companyId).ToListAsync();
        Assert.Single(employees);
    }


    private sealed record DepartmentPayload(Guid Id);
    private sealed record PositionProfilePayload(Guid Id);
    private sealed record SettingsPayload(int NextEmployeeNumber);

    private sealed record CurrentCompensationPayload(
        Guid Id,
        Guid EmployeeId,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string SalaryType,
        decimal Salary,
        string Currency,
        string Reason);

    private sealed record CompensationHistoryItemPayload(Guid Id, decimal Salary, string Reason);
    private sealed record CompensationHistoryPayload(List<CompensationHistoryItemPayload> Items);

    private sealed record EmployeePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        Guid? LocationId,
        Guid? PositionProfileId,
        Guid? ManagerId,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        DateTimeOffset CreatedAt);
}
