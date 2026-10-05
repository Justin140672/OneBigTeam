using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class VacancyEmploymentTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001a-0000-0000-0000-000000000078");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public VacancyEmploymentTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    private async Task<Guid> SeedLegacyVacancyAsync(Guid companyId, Guid positionProfileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Legacy Role", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    private async Task DeactivateEmploymentTypeAsync(Guid employmentTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var type = await db.EmploymentTypes.SingleAsync(t => t.Id == employmentTypeId);
        type.Deactivate(Now);
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> CreateVacancyAsync(
        HttpClient client, Guid companyId, Guid positionProfileId, Guid? employmentTypeId) =>
        client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId,
            employmentTypeId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid(),
        });

    [Fact]
    public async Task Create_Requires_An_EmploymentType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = reference.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("employmentTypeId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Rejects_An_EmploymentType_That_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var response = await CreateVacancyAsync(client, companyId, reference.PositionProfileId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Rejects_An_EmploymentType_From_Another_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var otherReference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, Guid.NewGuid());

        var response = await CreateVacancyAsync(client, companyId, reference.PositionProfileId, otherReference.EmploymentTypeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Rejects_An_Inactive_EmploymentType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        await DeactivateEmploymentTypeAsync(reference.EmploymentTypeId);

        var response = await CreateVacancyAsync(client, companyId, reference.PositionProfileId, reference.EmploymentTypeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Persists_The_EmploymentType_And_Get_Returns_It()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var createResponse = await CreateVacancyAsync(client, companyId, reference.PositionProfileId, reference.EmploymentTypeId);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.Equal(reference.EmploymentTypeId, created.RootElement.GetProperty("employmentTypeId").GetGuid());
        var id = created.RootElement.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var fetched = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal(reference.EmploymentTypeId, fetched.RootElement.GetProperty("employmentTypeId").GetGuid());
    }

    [Fact]
    public async Task Get_Legacy_Vacancy_Returns_A_Null_EmploymentType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedLegacyVacancyAsync(companyId, reference.PositionProfileId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("employmentTypeId").ValueKind);
    }

    [Fact]
    public async Task Update_Sets_The_EmploymentType_On_A_Legacy_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedLegacyVacancyAsync(companyId, reference.PositionProfileId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}", new
        {
            companyId,
            vacancyId,
            advertTitle = "Legacy Role",
            hiringManagerId = Guid.NewGuid(),
            employmentTypeId = reference.EmploymentTypeId,
            expectedVersion = 1,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>()
            .Vacancies.AsNoTracking().SingleAsync(v => v.Id == vacancyId);
        Assert.Equal(reference.EmploymentTypeId, saved.EmploymentTypeId);
    }

    [Fact]
    public async Task Update_Rejects_An_Inactive_Or_CrossCompany_EmploymentType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var otherReference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, Guid.NewGuid());
        var inactiveReference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        await DeactivateEmploymentTypeAsync(inactiveReference.EmploymentTypeId);
        var vacancyId = await SeedLegacyVacancyAsync(companyId, reference.PositionProfileId);

        foreach (var rejectedTypeId in new[] { otherReference.EmploymentTypeId, inactiveReference.EmploymentTypeId, Guid.NewGuid() })
        {
            var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}", new
            {
                companyId,
                vacancyId,
                advertTitle = "Legacy Role",
                hiringManagerId = Guid.NewGuid(),
                employmentTypeId = rejectedTypeId,
                expectedVersion = 1,
            });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Publish_Of_A_Legacy_Vacancy_Without_EmploymentType_Returns_BadRequest_And_Stays_Draft()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedLegacyVacancyAsync(companyId, reference.PositionProfileId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/publish", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>()
            .Vacancies.AsNoTracking().SingleAsync(v => v.Id == vacancyId);
        Assert.Equal(VacancyStatus.Draft, saved.Status);
    }

    [Fact]
    public async Task Publish_Succeeds_Once_The_EmploymentType_Is_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var createResponse = await CreateVacancyAsync(client, companyId, reference.PositionProfileId, reference.EmploymentTypeId);
        var vacancyId = (await createResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/publish", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);
}
