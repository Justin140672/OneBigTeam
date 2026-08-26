using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See ApproveVacancyHandlerTests/VacancyTests in HR.Modules.Recruitment.Tests for the equivalent
/// unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class ApproveVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000031-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ApproveVacancyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<Guid> SeedVacancyAsync(Guid companyId, VacancyStatus status = VacancyStatus.Draft)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        if (status is VacancyStatus.Open)
            vacancy.Open(Now, date);
        if (status is VacancyStatus.Closed)
        {
            vacancy.Open(Now, date);
            vacancy.Close(Now, date);
        }
        if (status is VacancyStatus.Cancelled)
            vacancy.Cancel(Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Post_Approve_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/approve", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Approve_Approves_Draft_Vacancy()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovePayload>();
        Assert.NotNull(payload);
        Assert.Equal(vacancyId, payload!.Id);
        Assert.NotNull(payload.ApprovedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
        Assert.NotNull(saved.ApprovedAt);
    }

    [Fact]
    public async Task Post_Approve_Returns_NotFound_For_Unknown_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}/approve", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Approve_Returns_BadRequest_For_Closed_Vacancy()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId, VacancyStatus.Closed);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Approve_Returns_BadRequest_For_Cancelled_Vacancy()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId, VacancyStatus.Cancelled);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ApprovePayload(Guid Id, Guid CompanyId, DateTimeOffset? ApprovedAt, Guid? ApprovedByUserId);
}
