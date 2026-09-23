using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9 regression: the account-creation work-email policy must NOT leak into Recruitment —
/// candidates are external applicants and legitimately use personal/public email addresses, and
/// creating a candidate never creates a login account.
/// </summary>
[Collection("Integration")]
public class CreateCandidatePublicEmailEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("b9e10009-0000-0000-0000-000000000004");

    public CreateCandidatePublicEmailEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> RecruiterClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    [Fact]
    public async Task Post_Candidate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Emma",
            lastName = "Clarke",
            email = "emma.clarke@gmail.com",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("hotmail.co.uk")]
    [InlineData("yahoo.com")]
    public async Task Post_Candidate_With_Public_Email_Domain_Is_Created(string domain)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var email = $"emma.clarke.{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Emma",
            lastName = "Clarke",
            email,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(email, payload!.Email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.True(await db.Candidates.AnyAsync(c => c.Id == payload.Id && c.CompanyId == companyId));
    }

    private sealed record CandidatePayload(Guid Id, string Email);
}
