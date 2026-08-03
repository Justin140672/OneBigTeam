using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the ticket #78 Source/SourceExternalRecruiterId fields added to CreateApplication.
/// See CreateApplicationHandlerTests/CreateApplicationValidatorTests in HR.Modules.Recruitment.Tests
/// for the equivalent unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class CreateApplicationSourceEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd000011-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CreateApplicationSourceEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(Guid VacancyId, Guid CandidateId)> SeedVacancyAndCandidateAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return (vacancy.Id, candidate.Id);
    }

    private async Task<Guid> SeedRecruiterAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }

    [Fact]
    public async Task Post_Applications_Succeeds_With_Valid_ExternalRecruiter_Source()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (vacancyId, candidateId) = await SeedVacancyAndCandidateAsync(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new
            {
                companyId,
                vacancyId,
                candidateId,
                source = "ExternalRecruiter",
                sourceExternalRecruiterId = recruiterId,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ExternalRecruiter", payload!.Source);
        Assert.Equal(recruiterId, payload.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task Post_Applications_Returns_UnprocessableEntity_When_Source_ExternalRecruiter_But_RecruiterId_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (vacancyId, candidateId) = await SeedVacancyAndCandidateAsync(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new
            {
                companyId,
                vacancyId,
                candidateId,
                source = "ExternalRecruiter",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Applications_Returns_NotFound_When_SourceExternalRecruiterId_Unknown()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (vacancyId, candidateId) = await SeedVacancyAndCandidateAsync(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new
            {
                companyId,
                vacancyId,
                candidateId,
                source = "ExternalRecruiter",
                sourceExternalRecruiterId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Applications_Succeeds_With_Direct_Source_And_No_Recruiter_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (vacancyId, candidateId) = await SeedVacancyAndCandidateAsync(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new
            {
                companyId,
                vacancyId,
                candidateId,
                source = "Direct",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Direct", payload!.Source);
        Assert.Null(payload.SourceExternalRecruiterId);
    }

    private sealed record ApplicationPayload(Guid Id, string? Source, Guid? SourceExternalRecruiterId);
}
