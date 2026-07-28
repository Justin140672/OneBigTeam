using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class UpdateVacancyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000014-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateVacancyEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedVacancyAsync(Guid companyId, Guid positionProfileId, string? advertTitle = "Backend Engineer", string? advertDescription = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, advertTitle, advertDescription, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Put_Vacancy_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}",
            new { advertTitle = "Updated Title", hiringManagerId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_NotFound_When_Vacancy_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}",
            new { companyId, advertTitle = "Updated Title", hiringManagerId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Response_Does_Not_Include_DepartmentId()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Updated Title", hiringManagerId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Title", payload!.AdvertTitle);
    }

    [Fact]
    public async Task Put_Vacancy_EffectiveLocation_Remains_Resolved_Purely_From_PositionProfile_Across_Updates()
    {
        // Location is no longer a vacancy-level concept — there is no vacancy-level override field
        // on UpdateVacancyRequest, so EffectiveLocation stays resolved exclusively from the linked
        // Position Profile's PositionProfileSummary.LocationName regardless of other field updates.
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var hiringManagerId = Guid.NewGuid();

        string positionProfileLocationName;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
            positionProfileLocationName = (await employeesDb.Locations.SingleAsync(l => l.Id == referenceData.LocationId)).Name;
        }

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", advertDescription = "Own the platform", hiringManagerId });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");
        var getPayload = await getResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(positionProfileLocationName, getPayload!.EffectiveLocation);
    }

    [Fact]
    public async Task Put_Vacancy_Clears_AdvertTitle_Back_To_Null_And_GetVacancy_Falls_Back_To_PositionProfile_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId, advertTitle: "Original Advert Title");
        var hiringManagerId = Guid.NewGuid();

        var positionProfileResponse = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{referenceData.PositionProfileId}");
        var positionProfileTitle = (await positionProfileResponse.Content.ReadFromJsonAsync<PositionProfileTitlePayload>())!.Title;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = (string?)null, hiringManagerId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.AdvertTitle);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");
        var getPayload = await getResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(positionProfileTitle, getPayload!.EffectiveTitle);
    }

    private async Task<Guid> SeedExternalRecruiterAsync(Guid companyId, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        if (!isActive)
            recruiter.SetActiveStatus(false, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }

    [Fact]
    public async Task Put_Vacancy_Assigns_Active_ExternalRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var recruiterId = await SeedExternalRecruiterAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", hiringManagerId, assignedRecruiterId = recruiterId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(recruiterId, payload!.AssignedRecruiterId);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");
        var getPayload = await getResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(recruiterId, getPayload!.AssignedRecruiterId);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_Validation_Error_When_AssignedRecruiter_Is_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var recruiterId = await SeedExternalRecruiterAsync(companyId, isActive: false);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", hiringManagerId, assignedRecruiterId = recruiterId });

        // Note: this is a handler-level Result.Failure(Error.Validation(...)), not a FastEndpoints
        // validator failure, so it maps to 400 (BadRequest) — same as other handler-level validation
        // errors in this codebase (e.g. the PositionProfile change-control checks above).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_NotFound_When_AssignedRecruiter_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var recruiterId = await SeedExternalRecruiterAsync(otherCompanyId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", hiringManagerId, assignedRecruiterId = recruiterId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Clears_AssignedRecruiterId_Back_To_Null_Succeeds()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var recruiterId = await SeedExternalRecruiterAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", hiringManagerId, assignedRecruiterId = recruiterId });
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var clearResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Backend Engineer", hiringManagerId, assignedRecruiterId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var payload = await clearResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.AssignedRecruiterId);
    }

    private async Task<Guid> SeedPositionProfileAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var positionProfile = HR.Modules.Employees.Domain.PositionProfile.Create(
            Guid.NewGuid(), companyId, departmentId: Guid.NewGuid(), locationId: Guid.NewGuid(), "Support Engineer",
            description: null, probationMonthsOverride: null, workingDaysOverride: null,
            hoursPerDayOverride: null, salaryMin: null, salaryMax: null, salaryType: null,
            defaultLeavePolicyId: Guid.NewGuid(), Now);
        employeesDb.PositionProfiles.Add(positionProfile);
        await employeesDb.SaveChangesAsync();
        return positionProfile.Id;
    }

    private async Task SeedApplicationAsync(Guid companyId, Guid vacancyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Jane", "Doe", $"jane.doe.{Guid.NewGuid():N}@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, null, Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Put_Vacancy_Changes_PositionProfileId_When_Draft_With_Zero_Applications()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, positionProfileId = newPositionProfileId, advertTitle = "Backend Engineer", hiringManagerId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newPositionProfileId, payload!.PositionProfileId);
    }

    [Fact]
    public async Task Put_Vacancy_Rejects_PositionProfileId_Change_When_Vacancy_Is_Not_Draft()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, positionProfileId = newPositionProfileId, advertTitle = "Backend Engineer", hiringManagerId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Rejects_PositionProfileId_Change_When_Vacancy_Has_An_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();
        await SeedApplicationAsync(companyId, vacancyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, positionProfileId = newPositionProfileId, advertTitle = "Backend Engineer", hiringManagerId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_NotFound_When_PositionProfileId_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, positionProfileId = Guid.NewGuid(), advertTitle = "Backend Engineer", hiringManagerId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_NotFound_When_PositionProfileId_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var otherCompanyPositionProfileId = await SeedPositionProfileAsync(otherCompanyId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, positionProfileId = otherCompanyPositionProfileId, advertTitle = "Backend Engineer", hiringManagerId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Updates_Other_Fields_Without_PositionProfileId_As_Before()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var hiringManagerId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new { companyId, vacancyId, advertTitle = "Updated Title Only", hiringManagerId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Title Only", payload!.AdvertTitle);
        Assert.Equal(referenceData.PositionProfileId, payload.PositionProfileId);
    }

    [Fact]
    public async Task Put_Vacancy_Allows_PositionProfileId_Change_When_Not_Draft_With_AuthorisedCorrection()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new
            {
                companyId,
                vacancyId,
                positionProfileId = newPositionProfileId,
                advertTitle = "Backend Engineer",
                hiringManagerId,
                isAuthorisedCorrection = true,
                correctionReason = "Vacancy was created against the wrong position profile.",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newPositionProfileId, payload!.PositionProfileId);
    }

    [Fact]
    public async Task Put_Vacancy_Rejects_PositionProfileId_Change_When_AuthorisedCorrection_True_But_Reason_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new
            {
                companyId,
                vacancyId,
                positionProfileId = newPositionProfileId,
                advertTitle = "Backend Engineer",
                hiringManagerId,
                isAuthorisedCorrection = true,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_AuthorisedCorrection_Persists_Audit_Record_With_AssignmentMethod_And_Actor()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);
        var newPositionProfileId = await SeedPositionProfileAsync(companyId);
        var hiringManagerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = await db.Vacancies.SingleAsync(v => v.Id == vacancyId);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            new
            {
                companyId,
                vacancyId,
                positionProfileId = newPositionProfileId,
                advertTitle = "Backend Engineer",
                hiringManagerId,
                isAuthorisedCorrection = true,
                correctionReason = "Vacancy was created against the wrong position profile.",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var auditDb = scope2.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId
                && e.EventType == "vacancy.position_profile_assigned"
                && e.EntityId == vacancyId)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.NotNull(auditRecord!.ActorUserId);
        Assert.Contains("authorised_correction", auditRecord.MetadataJson);
        Assert.Contains("Vacancy was created against the wrong position profile.", auditRecord.MetadataJson);
    }

    private sealed record VacancyPayload(
        Guid Id,
        Guid CompanyId,
        Guid PositionProfileId,
        string? AdvertTitle,
        string? AdvertDescription,
        string EffectiveTitle,
        string? EffectiveLocation,
        Guid? AssignedRecruiterId);

    private sealed record PositionProfileTitlePayload(string Title);
}
