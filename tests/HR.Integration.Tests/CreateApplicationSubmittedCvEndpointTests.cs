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

/// <summary>
/// Internal recruitment Ticket 1: Postgres integration coverage for the optional CvDocumentId on
/// POST /api/companies/{c}/vacancies/{v}/applications. See CreateApplicationHandlerTests /
/// CreateApplicationValidatorTests in HR.Modules.Recruitment.Tests for unit-level equivalents.
/// </summary>
[Collection("Integration")]
public class CreateApplicationSubmittedCvEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00cf31-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CreateApplicationSubmittedCvEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    private static string CreateUrl(Guid companyId, Guid vacancyId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications";

    private Task<Guid> SeedCvAsync(Guid companyId, Guid candidateId, string fileName = "cv.pdf",
        CandidateDocumentKind kind = CandidateDocumentKind.Cv, DateTimeOffset? at = null) =>
        RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, at ?? Now, fileName, fileName, kind);

    private async Task<bool> AnyApplicationAsync(Guid vacancyId, Guid candidateId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.AnyAsync(a => a.VacancyId == vacancyId && a.CandidateId == candidateId);
    }

    [Fact]
    public async Task Post_With_Valid_CvDocumentId_Returns_Created_And_Get_Returns_The_Submitted_Cv()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, candidateId, "emma-cv.pdf");
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId),
            new { companyId, vacancyId, candidateId, cvDocumentId = cvId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.NotNull(created);
        Assert.Equal(cvId, created!.CvDocumentId);

        var get = await client.GetAsync($"{CreateUrl(companyId, vacancyId)}/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var application = await get.Content.ReadFromJsonAsync<GetApplicationPayload>();
        Assert.Equal(cvId, application!.CvDocumentId);
        Assert.Equal("emma-cv.pdf", application.CvFileName);

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "application.cv_reference_changed" && e.EntityId == created.Id)
            .SingleOrDefaultAsync();
        Assert.NotNull(auditRecord);
        Assert.Equal(RecruiterUser, auditRecord!.ActorUserId);
        Assert.Contains(cvId.ToString(), auditRecord.AfterJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_Without_CvDocumentId_Creates_Application_With_No_Submitted_Cv()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        await SeedCvAsync(companyId, candidateId);
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId), new { companyId, vacancyId, candidateId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.Null(created!.CvDocumentId);
    }

    [Fact]
    public async Task Post_With_Empty_Guid_CvDocumentId_Is_Rejected_By_Validation()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId),
            new { companyId, vacancyId, candidateId, cvDocumentId = Guid.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(await AnyApplicationAsync(vacancyId, candidateId));
    }

    [Fact]
    public async Task Post_With_Cv_Of_Another_Candidate_Returns_BadRequest_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var otherCandidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now, "Liam", "Turner");
        var otherCandidatesCvId = await SeedCvAsync(companyId, otherCandidateId);
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId),
            new { companyId, vacancyId, candidateId, cvDocumentId = otherCandidatesCvId });

        await AssertValidationProblemAsync(response);
        Assert.False(await AnyApplicationAsync(vacancyId, candidateId));
    }

    [Fact]
    public async Task Post_With_Cv_Of_Another_Company_Returns_BadRequest_And_Creates_Nothing()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyA, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        var companyBCandidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyB, Now);
        var foreignCvId = await SeedCvAsync(companyB, companyBCandidateId);
        using var client = await ClientAs(companyA);

        var response = await client.PostAsJsonAsync(CreateUrl(companyA, vacancyId),
            new { companyId = companyA, vacancyId, candidateId, cvDocumentId = foreignCvId });

        var problem = await AssertValidationProblemAsync(response);
        Assert.Equal(Application.CvDocumentNotFoundMessage, problem.Error);
        Assert.False(await AnyApplicationAsync(vacancyId, candidateId));
    }

    [Fact]
    public async Task Post_With_Non_Cv_Document_Returns_BadRequest_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var coverLetterId = await SeedCvAsync(companyId, candidateId, "cover.pdf", CandidateDocumentKind.Other);
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId),
            new { companyId, vacancyId, candidateId, cvDocumentId = coverLetterId });

        var problem = await AssertValidationProblemAsync(response);
        Assert.Contains("not a CV", problem.Error);
        Assert.False(await AnyApplicationAsync(vacancyId, candidateId));
    }

    [Fact]
    public async Task Post_With_Unknown_Document_Returns_BadRequest_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(companyId);

        var response = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyId),
            new { companyId, vacancyId, candidateId, cvDocumentId = Guid.NewGuid() });

        await AssertValidationProblemAsync(response);
        Assert.False(await AnyApplicationAsync(vacancyId, candidateId));
    }

    [Fact]
    public async Task Two_Applications_For_The_Same_Candidate_On_Different_Vacancies_Keep_Their_Own_Cvs()
    {
        var companyId = Guid.NewGuid();
        var vacancyA = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now, "Backend Engineer");
        var vacancyB = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now, "Product Designer");
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var engineeringCvId = await SeedCvAsync(companyId, candidateId, "cv-engineering.pdf");
        var designCvId = await SeedCvAsync(companyId, candidateId, "cv-design.pdf", at: Now.AddMinutes(1));
        using var client = await ClientAs(companyId);

        var responseA = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyA),
            new { companyId, vacancyId = vacancyA, candidateId, cvDocumentId = engineeringCvId });
        var responseB = await client.PostAsJsonAsync(CreateUrl(companyId, vacancyB),
            new { companyId, vacancyId = vacancyB, candidateId, cvDocumentId = designCvId });

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        var createdA = await responseA.Content.ReadFromJsonAsync<CreatedPayload>();
        var createdB = await responseB.Content.ReadFromJsonAsync<CreatedPayload>();

        var appA = await (await client.GetAsync($"{CreateUrl(companyId, vacancyA)}/{createdA!.Id}")).Content.ReadFromJsonAsync<GetApplicationPayload>();
        var appB = await (await client.GetAsync($"{CreateUrl(companyId, vacancyB)}/{createdB!.Id}")).Content.ReadFromJsonAsync<GetApplicationPayload>();

        Assert.Equal(engineeringCvId, appA!.CvDocumentId);
        Assert.Equal("cv-engineering.pdf", appA.CvFileName);
        Assert.Equal(designCvId, appB!.CvDocumentId);
        Assert.Equal("cv-design.pdf", appB.CvFileName);
    }

    private static async Task<ProblemPayload> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal("validation", problem!.Code);
        return problem;
    }

    private sealed record CreatedPayload(Guid Id, Guid CompanyId, Guid VacancyId, Guid CandidateId, Guid? CvDocumentId);

    private sealed record GetApplicationPayload(Guid Id, Guid? CvDocumentId, string? CvFileName);

    private sealed record ProblemPayload(string Error, string Code);
}
