using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See DeactivateCandidateHandlerTests/DeactivateCandidateValidatorTests/CandidateTests in
/// HR.Modules.Recruitment.Tests for the equivalent unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class DeactivateCandidateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000012-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("ce000012-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public DeactivateCandidateEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedCandidateAsync(Guid companyId, bool deactivated = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        if (deactivated)
            candidate.Deactivate(Guid.NewGuid(), "Already inactive", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedCandidateWithActiveApplicationAsync(Guid companyId, Guid candidateId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        var cvReviewStageId = stages.Single(s => s.Name == "CV Review").Id;
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateId, cvReviewStageId, null, Now);
        db.RecruitmentStages.AddRange(stages);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}/deactivate",
            new { reason = "No longer available" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_Forbidden_For_Plain_Employee_User()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_Forbidden_When_Route_Company_Does_Not_Match_Caller_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyA);
        using var client = await ClientAs(RecruiterUser, companyA);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/candidates/{candidateId}/deactivate",
            new { companyId = companyB, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Deactivates_Candidate()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeactivatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(candidateId, payload!.Id);
        Assert.False(payload.IsActive);
        Assert.Equal("No longer available", payload.DeactivationReason);
        Assert.NotNull(payload.DeactivatedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Candidates.SingleAsync(c => c.Id == candidateId);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_NotFound_For_Unknown_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);
        var candidateId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_Conflict_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = string.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Deactivate_Returns_UnprocessableEntity_When_Candidate_Has_Active_Application()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId);
        await SeedCandidateWithActiveApplicationAsync(companyId, candidateId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { companyId, candidateId, reason = "No longer available" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record DeactivatePayload(
        Guid Id, Guid CompanyId, bool IsActive, DateTimeOffset? DeactivatedAt, Guid? DeactivatedByUserId,
        string? DeactivationReason, DateTimeOffset UpdatedAt);
}
