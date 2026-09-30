using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CandidateDeactivationEffectsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000014-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CandidateDeactivationEffectsEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedCandidateAsync(Guid companyId, bool deactivated)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, Now);
        if (deactivated)
            candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    [Fact]
    public async Task Get_Candidates_Excludes_Inactive_Candidate_By_Default()
    {
        var companyId = Guid.NewGuid();
        var activeCandidateId = await SeedCandidateAsync(companyId, deactivated: false);
        var inactiveCandidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == activeCandidateId);
        Assert.DoesNotContain(payload.Items, i => i.Id == inactiveCandidateId);
    }

    [Fact]
    public async Task Get_Candidates_Includes_Inactive_Candidate_When_IncludeInactive_Is_True()
    {
        var companyId = Guid.NewGuid();
        var activeCandidateId = await SeedCandidateAsync(companyId, deactivated: false);
        var inactiveCandidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates?includeInactive=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == activeCandidateId);
        Assert.Contains(payload.Items, i => i.Id == inactiveCandidateId && !i.IsActive);
    }

    [Fact]
    public async Task Post_Applications_Returns_BadRequest_For_Inactive_Candidate()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, deactivated: true);
        using var client = await AuthenticatedClient(companyId);

        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new { companyId, vacancyId, candidateId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ListPayload(List<CandidateListItemPayload> Items);
    private sealed record CandidateListItemPayload(Guid Id, string FirstName, string LastName, string Email, bool IsActive);
}
