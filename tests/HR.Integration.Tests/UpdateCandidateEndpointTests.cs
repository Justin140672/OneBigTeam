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
/// Postgres integration coverage for PUT /candidates/{id}. See UpdateCandidateHandlerTests /
/// UpdateCandidateValidatorTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, tenant-mismatch 403, happy 200 + persisted details,
/// unknown candidate 404, cross-company 404, duplicate-email 409, unchanged-email 200, validation 422.
/// </summary>
[Collection("Integration")]
public class UpdateCandidateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c2-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c2-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateCandidateEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedCandidateAsync(Guid companyId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", email, null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private static object Body(Guid companyId, Guid candidateId, string first = "Emily", string last = "Clarke-Jones",
        string email = "emily.updated@example.com") =>
        new { companyId, candidateId, firstName = first, lastName = last, email, phone = "+44 7700 900123" };

    [Fact]
    public async Task Put_Candidate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}", Body(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyA, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyA);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyB}/candidates/{candidateId}", Body(companyB, candidateId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Updates_And_Persists_Details()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyId);
        var email = $"emily.{Guid.NewGuid():N}@example.com";

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, email: email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Emily", payload!.FirstName);
        Assert.Equal(email, payload.Email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Candidates.SingleAsync(c => c.Id == candidateId);
        Assert.Equal("Emily", saved.FirstName);
        Assert.Equal(email, saved.Email);
    }

    [Fact]
    public async Task Put_Candidate_Returns_NotFound_For_Unknown_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);
        var candidateId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_NotFound_For_Candidate_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyA, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyB}/candidates/{candidateId}", Body(companyB, candidateId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_Conflict_When_Email_Belongs_To_Another_Candidate_In_Company()
    {
        var companyId = Guid.NewGuid();
        var takenEmail = $"taken.{Guid.NewGuid():N}@example.com";
        await SeedCandidateAsync(companyId, takenEmail);
        var candidateId = await SeedCandidateAsync(companyId, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, email: takenEmail));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Allows_Keeping_Same_Email()
    {
        var companyId = Guid.NewGuid();
        var email = $"emma.{Guid.NewGuid():N}@example.com";
        var candidateId = await SeedCandidateAsync(companyId, email);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, first: "Emmaline", email: email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_UnprocessableEntity_For_Invalid_Email()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            new { companyId, candidateId, firstName = "Emily", lastName = "Clarke", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_UnprocessableEntity_For_Empty_FirstName()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(companyId, $"emma.{Guid.NewGuid():N}@example.com");
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            new { companyId, candidateId, firstName = "   ", lastName = "Clarke", email = "emily@example.com" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CandidatePayload(
        Guid Id, Guid CompanyId, string FirstName, string LastName, string Email, string? Phone, string? ResumeUrl);
}
