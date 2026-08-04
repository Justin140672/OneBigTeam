using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// LEGACY / dead-in-practice: <see cref="Vacancy.PositionProfileId"/> is now a non-nullable
/// <see cref="Guid"/>, so there is no way to construct a "vacancy needing position profile review"
/// any more — every vacancy always has a PositionProfileId. The GetVacanciesNeedingPositionProfileReview
/// endpoint was rewritten to always short-circuit and return an empty result; these tests assert
/// exactly that, regardless of what vacancy data exists.
/// </summary>
[Collection("Integration")]
public class GetVacanciesNeedingPositionProfileReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00000f-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetVacanciesNeedingPositionProfileReviewEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_PositionProfileMatchesReview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies/position-profile-matches/review");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionProfileMatchesReview_Returns_Empty_List_When_Nothing_Needs_Review()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_PositionProfileMatchesReview_Always_Returns_Empty_List_Even_When_Vacancies_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ReviewListPayload(List<ReviewItemPayload> Items);

    private sealed record ReviewItemPayload(
        Guid VacancyId,
        string? AdvertTitle,
        string Outcome,
        List<Guid> CandidatePositionProfileIds);
}
