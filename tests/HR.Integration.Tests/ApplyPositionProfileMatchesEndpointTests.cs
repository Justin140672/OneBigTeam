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
/// LEGACY / dead-in-practice: <see cref="Vacancy.PositionProfileId"/> is now a non-nullable
/// <see cref="Guid"/>, so there is no way to construct a vacancy that "needs" an auto-matched
/// position profile any more — every vacancy always has one. The ApplyPositionProfileMatches endpoint
/// was rewritten to always short-circuit and return an empty result; these tests assert exactly that
/// and confirm it never touches existing rows.
/// </summary>
[Collection("Integration")]
public class ApplyPositionProfileMatchesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000010-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ApplyPositionProfileMatchesEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Post_ApplyPositionProfileMatches_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/vacancies/position-profile-matches/apply", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ApplyPositionProfileMatches_Always_Returns_Empty_Results_When_Nothing_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/apply", new { companyId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplyResultsPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Results);
    }

    [Fact]
    public async Task Post_ApplyPositionProfileMatches_Always_Returns_Empty_Results_And_Never_Touches_Existing_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            vacancyId = vacancy.Id;
            await recruitmentDb.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/apply", new { companyId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplyResultsPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Results);

        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var saved = await recruitmentDb.Vacancies.SingleAsync(v => v.Id == vacancyId);
            Assert.Equal(referenceData.PositionProfileId, saved.PositionProfileId);
        }
    }

    private sealed record ApplyResultsPayload(List<ApplyResultItemPayload> Results);

    private sealed record ApplyResultItemPayload(
        Guid VacancyId,
        string? AdvertTitle,
        string Outcome,
        Guid? AssignedPositionProfileId,
        List<Guid> CandidatePositionProfileIds);
}
