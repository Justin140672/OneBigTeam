using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the ticket #78 Source/SourceExternalRecruiterId/SourceExternalRecruiterAgencyName fields
/// now surfaced on GetApplication's response.
/// </summary>
public class GetApplicationSourceEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd000012-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetApplicationSourceEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Get_Application_Returns_Source_And_Recruiter_AgencyName()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        Guid vacancyId, applicationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now, ApplicationSource.ExternalRecruiter, recruiter.Id);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.ExternalRecruiters.Add(recruiter);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
            vacancyId = vacancy.Id;
            applicationId = application.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ExternalRecruiter", payload!.Source);
        Assert.NotNull(payload.SourceExternalRecruiterId);
        Assert.Equal("Acme Recruiting", payload.SourceExternalRecruiterAgencyName);
    }

    [Fact]
    public async Task Get_Application_Returns_Null_Source_Fields_When_Source_Was_Never_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        Guid vacancyId, applicationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
            vacancyId = vacancy.Id;
            applicationId = application.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.Source);
        Assert.Null(payload.SourceExternalRecruiterId);
        Assert.Null(payload.SourceExternalRecruiterAgencyName);
    }

    private sealed record ApplicationPayload(
        Guid Id,
        string? Source,
        Guid? SourceExternalRecruiterId,
        string? SourceExternalRecruiterAgencyName);
}
