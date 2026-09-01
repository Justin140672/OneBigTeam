using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See PurgeEligibleCandidatesHandlerTests/CandidateTests in HR.Modules.Recruitment.Tests for the
/// equivalent unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class PurgeEligibleCandidatesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdminUser = new("ce000033-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterOnlyUser = new("ce000033-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public PurgeEligibleCandidatesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterOnlyUser, SystemRoles.Recruiter);
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

    private async Task<Guid> SeedEligibleCandidateAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var oldEnough = Now.AddDays(-731);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, oldEnough);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private async Task<Guid> SeedRecentCandidateAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", $"olivia.{Guid.NewGuid():N}@example.com", null, null, Now.AddDays(-10));
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    [Fact]
    public async Task Post_PurgeEligible_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/purge-eligible", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PurgeEligible_Returns_Forbidden_For_Recruiter_Only_Role()
    {
        // Proves the stronger "role:company-administrator" gate — Recruiter (recruitment:manage)
        // alone must not be able to run this permanent-redaction action.
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterOnlyUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates/purge-eligible", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_PurgeEligible_Purges_Eligible_Candidate_As_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedEligibleCandidateAsync(companyId);
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates/purge-eligible", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PurgeEligiblePayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.PurgedCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Candidates.SingleAsync(c => c.Id == candidateId);
        Assert.NotNull(saved.PurgedAt);
        Assert.Equal("[purged]", saved.FirstName);
    }

    [Fact]
    public async Task Post_PurgeEligible_Leaves_NonEligible_Candidates_Untouched()
    {
        var companyId = Guid.NewGuid();
        var eligibleId = await SeedEligibleCandidateAsync(companyId);
        var recentId = await SeedRecentCandidateAsync(companyId);
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates/purge-eligible", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PurgeEligiblePayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.PurgedCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var eligible = await db.Candidates.SingleAsync(c => c.Id == eligibleId);
        var recent = await db.Candidates.SingleAsync(c => c.Id == recentId);
        Assert.NotNull(eligible.PurgedAt);
        Assert.Null(recent.PurgedAt);
        Assert.Equal("Olivia", recent.FirstName);
    }

    [Fact]
    public async Task Post_PurgeEligible_Returns_Conflict_And_Purges_Nothing_When_Company_Is_Under_Legal_Hold()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedEligibleCandidateAsync(companyId);

        // ClientAs -> SyncCompanyAsync -> EnsureActiveSubscriptionAsync creates the Company and its
        // CustomerSubscription, so the legal hold is applied to that existing row afterwards (adding
        // a second subscription would violate the customer_subscriptions PK / companies FK).
        using var client = await ClientAs(CompanyAdminUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var subscription = await companiesDb.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
            subscription.PlaceLegalHold(Guid.NewGuid(), "Litigation hold for purge test", DateTimeOffset.UtcNow);
            await companiesDb.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates/purge-eligible", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Candidates.SingleAsync(c => c.Id == candidateId);
        Assert.Null(saved.PurgedAt);
        Assert.Equal("Emma", saved.FirstName);
    }

    private sealed record PurgeEligiblePayload(int PurgedCount);
}
