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
/// See ReactivateCandidateHandlerTests/ReactivateCandidateValidatorTests/CandidateTests in
/// HR.Modules.Recruitment.Tests for the equivalent unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class ReactivateCandidateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000013-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("ce000013-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ReactivateCandidateEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedCandidateAsync(Guid companyId, bool deactivated)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        if (deactivated)
            candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    [Fact]
    public async Task Post_Reactivate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}/reactivate",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reactivate_Returns_Forbidden_For_Plain_Employee_User()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/reactivate",
            new { companyId, candidateId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reactivate_Returns_Forbidden_When_Route_Company_Does_Not_Match_Caller_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyA, deactivated: true);
        using var client = await ClientAs(RecruiterUser, companyA);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/candidates/{candidateId}/reactivate",
            new { companyId = companyB, candidateId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reactivate_Reactivates_Candidate()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/reactivate",
            new { companyId, candidateId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReactivatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(candidateId, payload!.Id);
        Assert.True(payload.IsActive);
        Assert.NotNull(payload.ReactivatedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Candidates.SingleAsync(c => c.Id == candidateId);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Post_Reactivate_Returns_NotFound_For_Unknown_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);
        var candidateId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/reactivate",
            new { companyId, candidateId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reactivate_Returns_Conflict_When_Already_Active()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, deactivated: false);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/reactivate",
            new { companyId, candidateId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record ReactivatePayload(
        Guid Id, Guid CompanyId, bool IsActive, DateTimeOffset? ReactivatedAt, Guid? ReactivatedByUserId, DateTimeOffset UpdatedAt);
}
