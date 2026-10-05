using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class HireCandidateVacancyDerivedDefaultsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001a-0000-0000-0000-000000000077");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public HireCandidateVacancyDerivedDefaultsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private sealed record Scenario(
        Guid CompanyId,
        HttpClient Client,
        EmployeeReferenceDataSeeder.ReferenceData Reference,
        Guid VacancyId,
        Guid CandidateId,
        Guid ApplicationId,
        Guid HiringManagerId);

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, EmployeeReferenceDataSeeder.ReferenceData reference, string firstName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, "Manager", $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@acme.example",
            new DateOnly(2020, 1, 1), hasSystemAccess: true, new DateOnly(1985, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}"[..20], reference.EmploymentTypeId, reference.DepartmentId, reference.LocationId,
            reference.PositionProfileId, Now);
        employee.Activate(Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Scenario> SeedAsync(
        Guid? companyId = null, Guid? hiringManagerId = null, bool legacyVacancyWithoutEmploymentType = false)
    {
        var company = companyId ?? Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, company.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, company);

        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, company);
        var managerId = hiringManagerId ?? await SeedEmployeeAsync(company, reference, "Hiring");

        var vacancyResponse = await client.PostAsJsonAsync($"/api/companies/{company}/vacancies", new
        {
            companyId = company,
            positionProfileId = reference.PositionProfileId,
            employmentTypeId = reference.EmploymentTypeId,
            advertTitle = "Engineer",
            hiringManagerId = managerId,
        });
        Assert.Equal(HttpStatusCode.Created, vacancyResponse.StatusCode);
        var vacancy = (await vacancyResponse.Content.ReadFromJsonAsync<IdPayload>())!;

        if (legacyVacancyWithoutEmploymentType)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE recruitment.vacancies SET employment_type_id = NULL WHERE id = {0}", vacancy.Id);
        }

        var candidate = (await (await client.PostAsJsonAsync($"/api/companies/{company}/candidates", new
        {
            companyId = company,
            firstName = "Robin",
            lastName = "Fisher",
            email = $"robin.fisher.{Guid.NewGuid():N}@example.com",
        })).Content.ReadFromJsonAsync<IdPayload>())!;

        var application = (await (await client.PostAsJsonAsync(
            $"/api/companies/{company}/vacancies/{vacancy.Id}/applications", new
            {
                companyId = company,
                vacancyId = vacancy.Id,
                candidateId = candidate.Id,
            })).Content.ReadFromJsonAsync<IdPayload>())!;

        return new Scenario(company, client, reference, vacancy.Id, candidate.Id, application.Id, managerId);
    }

    private static Dictionary<string, object?> HireBody(Scenario s) => new()
    {
        ["companyId"] = s.CompanyId,
        ["vacancyId"] = s.VacancyId,
        ["applicationId"] = s.ApplicationId,
        ["startDate"] = new DateOnly(2026, 9, 1).ToString("yyyy-MM-dd"),
        ["dateOfBirth"] = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
        ["nationality"] = "British",
        ["gender"] = "Prefer not to say",
        ["employeeNumber"] = $"EMP-{Guid.NewGuid():N}",
        ["addressLine1"] = "1 Test Street",
        ["city"] = "London",
        ["postCode"] = "SW1A 1AA",
    };

    private static Task<HttpResponseMessage> HireAsync(Scenario s, Dictionary<string, object?> body) =>
        s.Client.PostAsJsonAsync(
            $"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}/hire", body);

    private async Task<Employee> LoadHiredEmployeeAsync(HttpResponseMessage response)
    {
        var hire = (await response.Content.ReadFromJsonAsync<HirePayload>())!;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.Employees.AsNoTracking().SingleAsync(e => e.Id == hire.EmployeeId);
    }

    private async Task<int> CountEmployeesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.Employees.CountAsync(e => e.CompanyId == companyId);
    }

    [Fact]
    public async Task Hire_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/hire", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hire_Uses_The_Vacancy_EmploymentType()
    {
        var s = await SeedAsync();

        var response = await HireAsync(s, HireBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await LoadHiredEmployeeAsync(response);
        Assert.Equal(s.Reference.EmploymentTypeId, employee.EmploymentTypeId);
    }

    [Fact]
    public async Task Hire_Ignores_A_Client_Supplied_EmploymentType()
    {
        var s = await SeedAsync();
        var otherType = await EmployeeReferenceDataSeeder.SeedAsync(_factory, s.CompanyId);
        var body = HireBody(s);
        body["employmentTypeId"] = otherType.EmploymentTypeId;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await LoadHiredEmployeeAsync(response);
        Assert.Equal(s.Reference.EmploymentTypeId, employee.EmploymentTypeId);
        Assert.NotEqual(otherType.EmploymentTypeId, employee.EmploymentTypeId);
    }

    [Fact]
    public async Task Hire_Of_A_Legacy_Vacancy_Without_EmploymentType_Returns_BadRequest_And_Creates_No_Employee()
    {
        var s = await SeedAsync(legacyVacancyWithoutEmploymentType: true);
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body["employmentTypeId"] = s.Reference.EmploymentTypeId;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Defaults_The_Manager_To_The_Vacancy_HiringManager()
    {
        var s = await SeedAsync();

        var response = await HireAsync(s, HireBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(s.HiringManagerId, (await LoadHiredEmployeeAsync(response)).ManagerId);
    }

    [Fact]
    public async Task Hire_Ignores_ManagerId_When_OverrideManager_Is_Not_Set()
    {
        var s = await SeedAsync();
        var otherManagerId = await SeedEmployeeAsync(s.CompanyId, s.Reference, "Other");
        var body = HireBody(s);
        body["managerId"] = otherManagerId;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(s.HiringManagerId, (await LoadHiredEmployeeAsync(response)).ManagerId);
    }

    [Fact]
    public async Task Hire_Honours_An_Explicit_Alternative_Manager()
    {
        var s = await SeedAsync();
        var otherManagerId = await SeedEmployeeAsync(s.CompanyId, s.Reference, "Other");
        var body = HireBody(s);
        body["overrideManager"] = true;
        body["managerId"] = otherManagerId;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(otherManagerId, (await LoadHiredEmployeeAsync(response)).ManagerId);
    }

    [Fact]
    public async Task Hire_Honours_An_Explicit_No_Manager()
    {
        var s = await SeedAsync();
        var body = HireBody(s);
        body["overrideManager"] = true;
        body["managerId"] = null;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await LoadHiredEmployeeAsync(response)).ManagerId);
    }

    [Fact]
    public async Task Hire_Rejects_An_Unknown_Override_Manager_And_Creates_No_Employee()
    {
        var s = await SeedAsync();
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body["overrideManager"] = true;
        body["managerId"] = Guid.NewGuid();

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Rejects_An_Override_Manager_From_Another_Company()
    {
        var s = await SeedAsync();
        var otherCompanyId = Guid.NewGuid();
        var otherReference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, otherCompanyId);
        var foreignManagerId = await SeedEmployeeAsync(otherCompanyId, otherReference, "Foreign");
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body["overrideManager"] = true;
        body["managerId"] = foreignManagerId;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Rejects_A_Default_HiringManager_That_Is_Not_An_Employee_Of_The_Company()
    {
        var s = await SeedAsync(hiringManagerId: Guid.NewGuid());
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);

        var response = await HireAsync(s, HireBody(s));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Requires_Nationality_And_Never_Defaults_It()
    {
        var s = await SeedAsync();
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body.Remove("nationality");

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Theory]
    [InlineData("addressLine1", null)]
    [InlineData("addressLine1", "")]
    [InlineData("addressLine1", "   ")]
    [InlineData("city", null)]
    [InlineData("city", "")]
    [InlineData("city", "   ")]
    [InlineData("postCode", null)]
    [InlineData("postCode", "")]
    [InlineData("postCode", "   ")]
    public async Task Hire_Rejects_Missing_Or_Whitespace_Required_Address_Fields(string field, string? value)
    {
        var s = await SeedAsync();
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body[field] = value;

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Rejects_A_Postcode_That_Does_Not_Match_The_Company_Rule()
    {
        var s = await SeedAsync();
        var employeesBefore = await CountEmployeesAsync(s.CompanyId);
        var body = HireBody(s);
        body["postCode"] = "not a postcode";

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(s.CompanyId));
    }

    [Fact]
    public async Task Hire_Persists_The_Normalized_Address()
    {
        var s = await SeedAsync();
        var body = HireBody(s);
        body["addressLine1"] = "  1 Test Street  ";
        body["addressLine2"] = "   ";
        body["city"] = " London ";
        body["county"] = " Greater London ";
        body["postCode"] = " SW1A 1AA ";

        var response = await HireAsync(s, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await LoadHiredEmployeeAsync(response);
        Assert.Equal("1 Test Street", employee.AddressLine1);
        Assert.Null(employee.AddressLine2);
        Assert.Equal("London", employee.City);
        Assert.Equal("Greater London", employee.County);
        Assert.Equal("SW1A 1AA", employee.PostCode);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record HirePayload(Guid Id, Guid EmployeeId);
}
