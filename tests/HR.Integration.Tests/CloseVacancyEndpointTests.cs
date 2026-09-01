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
/// Postgres integration coverage for POST /vacancies/{id}/close. See CloseVacancyHandlerTests in
/// HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, tenant-mismatch 403, happy 200 + persisted Closed status,
/// unknown vacancy 404, cross-company 404, already-closed 400, already-cancelled 400.
/// </summary>
[Collection("Integration")]
public class CloseVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c1-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c1-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CloseVacancyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
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

    private async Task<Guid> SeedVacancyAsync(Guid companyId, VacancyStatus? forceStatus = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        if (forceStatus == VacancyStatus.Closed)
            vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        else if (forceStatus == VacancyStatus.Cancelled)
            vacancy.Cancel(Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Post_Close_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/close", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/close", new { companyId, vacancyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyA);
        using var client = await ClientAs(RecruiterUser, companyA);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyId}/close", new { companyId = companyB, vacancyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Closes_Vacancy_And_Persists_Status()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);
        var closedAt = new DateOnly(2026, 9, 15);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/close",
            new { companyId, vacancyId, closedAt });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ClosePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Closed", payload!.Status);
        Assert.Equal(closedAt, payload.ClosedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
        Assert.Equal(VacancyStatus.Closed, saved.Status);
        Assert.Equal(closedAt, saved.ClosedAt);
    }

    [Fact]
    public async Task Post_Close_Returns_NotFound_For_Unknown_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);
        var vacancyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/close", new { companyId, vacancyId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Returns_NotFound_For_Vacancy_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyA);
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyId}/close", new { companyId = companyB, vacancyId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Returns_BadRequest_When_Already_Closed()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId, VacancyStatus.Closed);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/close", new { companyId, vacancyId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Close_Returns_BadRequest_When_Already_Cancelled()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId, VacancyStatus.Cancelled);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/close", new { companyId, vacancyId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ClosePayload(Guid Id, Guid CompanyId, string Status, DateOnly? OpenedAt, DateOnly? ClosedAt);
}
