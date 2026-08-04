using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetExternalRecruiterUsageEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd000011-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetExternalRecruiterUsageEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
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

    private async Task<Guid> SeedRecruiterAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }

    [Fact]
    public async Task Get_Usage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}/usage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Usage_Returns_NotFound_For_Unknown_Recruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/usage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Usage_Returns_Not_InUse_When_No_Vacancies_Assigned()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UsagePayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.InUse);
        Assert.Equal(0, payload.ActiveVacancyCount);
    }

    [Fact]
    public async Task Get_Usage_Returns_InUse_When_Vacancy_Assigned_To_Recruiter_Is_Active()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiterId);
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UsagePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.InUse);
        Assert.Equal(1, payload.ActiveVacancyCount);
        Assert.Single(payload.VacancyLabels);
    }

    private sealed record UsagePayload(
        Guid ExternalRecruiterId,
        bool InUse,
        int ActiveVacancyCount,
        IReadOnlyList<string> VacancyLabels);
}
