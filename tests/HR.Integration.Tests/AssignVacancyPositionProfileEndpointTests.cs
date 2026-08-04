using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AssignVacancyPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000011-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public AssignVacancyPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    private async Task<Guid> SeedVacancyAsync(Guid companyId, Guid? initialPositionProfileId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, initialPositionProfileId ?? Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/position-profile",
            new { positionProfileId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Assigns_And_Persists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = referenceData.PositionProfileId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(vacancyId, payload!.Id);
        Assert.Equal(referenceData.PositionProfileId, payload.PositionProfileId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
        Assert.Equal(referenceData.PositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_NotFound_When_Vacancy_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = referenceData.PositionProfileId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        // Position profile exists, but for a different company than the one making the request.
        var otherCompanyReferenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, otherCompanyId);
        var vacancyId = await SeedVacancyAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = otherCompanyReferenceData.PositionProfileId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_UnprocessableEntity_When_PositionProfileId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = Guid.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Returns_UnprocessableEntity_When_PositionProfileId_Is_Omitted()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssignVacancyPositionProfile_Can_ReAssign_Vacancy_That_Already_Had_A_PositionProfileId()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var originalReferenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var newReferenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, originalReferenceData.PositionProfileId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/position-profile",
            new { companyId, vacancyId, positionProfileId = newReferenceData.PositionProfileId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newReferenceData.PositionProfileId, payload!.PositionProfileId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
        Assert.Equal(newReferenceData.PositionProfileId, saved.PositionProfileId);
    }

    private sealed record VacancyPayload(
        Guid Id,
        Guid CompanyId,
        Guid PositionProfileId,
        string? AdvertTitle,
        string Status,
        DateTimeOffset UpdatedAt);
}
