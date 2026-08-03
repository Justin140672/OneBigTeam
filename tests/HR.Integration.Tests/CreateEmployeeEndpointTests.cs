using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateEmployeeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Pre-seeded user IDs that have the HrAdministrator role.
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

        // Seed HrAdministrator (to create employees) and CompanyAdministrator + Employee (to
        // update/read company settings via PUT/GET .../settings, which the employee-number
        // scenarios below need) for each test user so the permission checks pass.
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

    // UpdateCompanySettings (used by SetEmployeeNumberModeAsync below) requires a real
    // companies.companies row to exist — unlike CreateEmployee, which never checks the Company
    // table directly. Scenarios that call SetEmployeeNumberModeAsync must seed a real company via
    // this helper rather than using an arbitrary Guid as companyId.
    private static async Task<Guid> CreateCompanyAsync(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = name ?? $"Employee Number Test Co {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    // Was calling PUT /api/companies/{id}/settings (UpdateCompanySettingsHandler), which only
    // persists TimeZone/Locale and silently ignores every other field in the request body
    // (including employeeNumberMode) — it still returned 200 OK, so CreateEmployee's
    // "Automatic mode" checks kept reading back the default Manual mode and failing with
    // "Employee number is required.". The actual employee-number/HR settings live behind
    // PUT /api/companies/{id}/hr-settings (UpdateHrSettingsHandler/UpdateHrSettingsRequest).
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
    public async Task Post_Employees_Creates_Employee_With_Draft_Status()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Department, Location, Position Profile, Employment Type and Employee Number are all
        // mandatory on employee creation — seed the minimum real reference data required for any
        // employee to be created at all (see EmployeeReferenceDataSeeder for why).
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
        Assert.Equal("Draft", payload.Status);
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

        // Base reference data used for the manager (whose own department/position-profile isn't
        // under test here); a distinct department + position profile are created afterwards to
        // verify the employee-under-test picks up its own explicitly-assigned values.
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
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = manager!.Id,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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

        // Location/PositionProfile/EmploymentType are real (seeded) so the NotFound is
        // attributable specifically to the unknown DepartmentId, not some other missing lookup.
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = Guid.NewGuid(),
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = Guid.NewGuid(),
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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
        // Manual is the default mode, but set it explicitly for clarity.
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);

        // The response contract doesn't currently surface EmployeeNumber, so assert against the
        // company settings' advanced counter — proof a number was actually claimed.
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
                firstName = "Alice",
                lastName = "Smith",
                workEmail = $"alice.{Guid.NewGuid():N}@example.com",
                startDate = "2026-07-01",
                dateOfBirth = "1990-05-20",
                nationality = "British",
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

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        const int concurrentCreations = 10;

        var tasks = Enumerable.Range(0, concurrentCreations).Select(_ =>
        {
            // A fresh HttpClient per concurrent request, sharing the same auth/tenant headers,
            // avoids any accidental serialization introduced by reusing one HttpClient instance.
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
                firstName = "Alice",
                lastName = "Smith",
                workEmail = $"alice.{Guid.NewGuid():N}@example.com",
                startDate = "2026-07-01",
                dateOfBirth = "1990-05-20",
                nationality = "British",
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
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
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
            firstName = "Bob",
            lastName = "Jones",
            workEmail = $"bob.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Male"
        });
        Assert.Equal(HttpStatusCode.Created, responseB1.StatusCode);

        var settingsAResponse = await client.GetAsync($"/api/companies/{companyB}/hr-settings");
        // Still scoped to companyB via the tenant header set above.
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

    private sealed record DepartmentPayload(Guid Id);
    private sealed record PositionProfilePayload(Guid Id);
    private sealed record SettingsPayload(int NextEmployeeNumber);

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
