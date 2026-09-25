using System.Net;
using System.Net.Http.Headers;
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
/// Internal recruitment Ticket 1: Postgres integration coverage for
/// PUT /api/companies/{c}/vacancies/{v}/applications/{a}/cv — attach, replace or remove the CV
/// recorded as submitted with an application. Unit-level equivalents live in
/// SetApplicationCvHandlerTests / SetApplicationCvValidatorTests / ApplicationCvDocumentConstraintTests
/// in HR.Modules.Recruitment.Tests.
/// </summary>
[Collection("Integration")]
public class SetApplicationCvEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00cf30-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc00cf30-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public SetApplicationCvEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
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

    private static string CvUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv";

    private static string ApplicationUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}";

    private Task<Guid> SeedCvAsync(Guid companyId, Guid candidateId, string fileName = "cv.pdf", DateTimeOffset? at = null,
        CandidateDocumentKind kind = CandidateDocumentKind.Cv) =>
        RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, at ?? Now, fileName, fileName, kind);

    private async Task<GetApplicationPayload> GetApplicationAsync(HttpClient client, Guid companyId, Guid vacancyId, Guid applicationId)
    {
        var response = await client.GetAsync(ApplicationUrl(companyId, vacancyId, applicationId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GetApplicationPayload>())!;
    }

    private async Task<Application> LoadApplicationAsync(Guid applicationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
    }

    // ---- Auth ------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            CvUrl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), new { cvDocumentId = Guid.NewGuid(), expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Forbidden_For_Plain_Employee_And_Changes_Nothing()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cvId, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    // ---- Attach / replace / remove ---------------------------------------------------------------

    [Fact]
    public async Task Put_Attaches_Cv_And_Get_Application_Returns_It_As_The_Submitted_Cv()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId, "emma-cv.pdf");
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cvId, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SetCvPayload>();
        Assert.NotNull(payload);
        Assert.Equal(seeded.ApplicationId, payload!.Id);
        Assert.Equal(seeded.VacancyId, payload.VacancyId);
        Assert.Equal(seeded.CandidateId, payload.CandidateId);
        Assert.Equal(cvId, payload.CvDocumentId);
        Assert.Equal(2, payload.Version);

        var application = await GetApplicationAsync(client, companyId, seeded.VacancyId, seeded.ApplicationId);
        Assert.Equal(cvId, application.CvDocumentId);
        Assert.Equal("emma-cv.pdf", application.CvFileName);
        Assert.Equal("application/pdf", application.CvContentType);
        Assert.Equal(2, application.Version);
    }

    [Fact]
    public async Task Put_Replaces_Existing_Cv_Reference()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var originalCvId = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v1.pdf");
        var replacementCvId = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v2.pdf", Now.AddMinutes(1));
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, originalCvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = replacementCvId, expectedVersion = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SetCvPayload>();
        Assert.Equal(replacementCvId, payload!.CvDocumentId);
        Assert.Equal(3, payload.Version);

        var application = await GetApplicationAsync(client, companyId, seeded.VacancyId, seeded.ApplicationId);
        Assert.Equal(replacementCvId, application.CvDocumentId);
        Assert.Equal("cv-v2.pdf", application.CvFileName);
    }

    [Fact]
    public async Task Put_With_Null_CvDocumentId_Removes_The_Reference_But_Keeps_The_Document()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId);
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, cvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = (Guid?)null, expectedVersion = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SetCvPayload>();
        Assert.Null(payload!.CvDocumentId);
        Assert.Equal(3, payload.Version);

        var application = await GetApplicationAsync(client, companyId, seeded.VacancyId, seeded.ApplicationId);
        Assert.Null(application.CvDocumentId);
        Assert.Null(application.CvFileName);
        // The document still exists and is still the candidate's current CV.
        Assert.Equal(cvId, application.CurrentCandidateCvDocumentId);
    }

    [Fact]
    public async Task Put_Same_Cv_Again_Is_A_NoOp_That_Does_Not_Advance_Version()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId);
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, cvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cvId, expectedVersion = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SetCvPayload>();
        Assert.Equal(cvId, payload!.CvDocumentId);
        Assert.Equal(2, payload.Version);
        Assert.Equal(2, (await LoadApplicationAsync(seeded.ApplicationId)).Version);
    }

    [Fact]
    public async Task Uploading_A_Newer_Cv_After_Attaching_Does_Not_Change_The_Submitted_Cv()
    {
        // Acceptance: the application keeps the exact CV it was submitted with.
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var submittedCvId = await SeedCvAsync(companyId, seeded.CandidateId, "submitted-cv.pdf", Now.AddMinutes(-10));
        using var client = await ClientAs(RecruiterUser, companyId);

        var attach = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = submittedCvId, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);

        var upload = await client.PostAsync(
            $"/api/companies/{companyId}/candidates/{seeded.CandidateId}/documents", BuildCvUpload("newer-cv.pdf"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(uploaded);
        Assert.NotEqual(submittedCvId, uploaded!.Id);

        var application = await GetApplicationAsync(client, companyId, seeded.VacancyId, seeded.ApplicationId);
        Assert.Equal(submittedCvId, application.CvDocumentId);
        Assert.Equal("submitted-cv.pdf", application.CvFileName);
        Assert.Equal(uploaded.Id, application.CurrentCandidateCvDocumentId);
        Assert.Equal("newer-cv.pdf", application.CurrentCandidateCvFileName);

        Assert.Equal(submittedCvId, (await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    // ---- Audit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Put_Persists_Audit_Record_With_Previous_And_New_Cv_Ids_And_Actor()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var originalCvId = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v1.pdf");
        var replacementCvId = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v2.pdf", Now.AddMinutes(1));
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, originalCvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = replacementCvId, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "application.cv_reference_changed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("Application", auditRecord!.EntityType);
        Assert.Equal(seeded.ApplicationId, auditRecord.EntityId);
        Assert.Equal(RecruiterUser, auditRecord.ActorUserId);
        Assert.Contains(originalCvId.ToString(), auditRecord.BeforeJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(replacementCvId.ToString(), auditRecord.AfterJson, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Not found -------------------------------------------------------------------------------

    [Fact]
    public async Task Put_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, Guid.NewGuid()), new { cvDocumentId = cvId, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        var cvId = await SeedCvAsync(companyA, seeded.CandidateId);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PutAsJsonAsync(
            CvUrl(companyB, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cvId, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    // ---- Validation ------------------------------------------------------------------------------

    [Fact]
    public async Task Put_Without_ExpectedVersion_Is_Rejected_By_Validation()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await SeedCvAsync(companyId, seeded.CandidateId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cvId });

        // FastEndpoints validation failures are configured as 422 in HR.Api/Program.cs.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Put_With_Empty_Guid_CvDocumentId_Is_Rejected_By_Validation()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = Guid.Empty, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_BadRequest_For_Document_Of_Another_Candidate()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var otherCandidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now, "Liam", "Turner");
        var otherCandidatesCvId = await SeedCvAsync(companyId, otherCandidateId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = otherCandidatesCvId, expectedVersion = 1 });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Put_Returns_BadRequest_For_Document_Of_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        var companyBCandidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyB, Now);
        var foreignCvId = await SeedCvAsync(companyB, companyBCandidateId);
        using var client = await ClientAs(RecruiterUser, companyA);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyA, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = foreignCvId, expectedVersion = 1 });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        // Another tenant's document is reported exactly like a missing one.
        Assert.Equal(Application.CvDocumentNotFoundMessage, problem.Error);
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Put_Returns_BadRequest_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = Guid.NewGuid(), expectedVersion = 1 });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(Application.CvDocumentNotFoundMessage, problem.Error);
    }

    [Fact]
    public async Task Put_Returns_BadRequest_For_Non_Cv_Document()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var coverLetterId = await SeedCvAsync(companyId, seeded.CandidateId, "cover.pdf", kind: CandidateDocumentKind.Other);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = coverLetterId, expectedVersion = 1 });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("not a CV", problem.Error);
        Assert.Null((await LoadApplicationAsync(seeded.ApplicationId)).CvDocumentId);
    }

    // ---- Concurrency -----------------------------------------------------------------------------

    [Fact]
    public async Task Put_Returns_Conflict_For_Stale_ExpectedVersion()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cv1 = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v1.pdf");
        var cv2 = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v2.pdf", Now.AddMinutes(1));
        using var client = await ClientAs(RecruiterUser, companyId);

        var first = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cv1, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await client.PutAsJsonAsync(
            CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cv2, expectedVersion = 1 });

        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrency");
        var saved = await LoadApplicationAsync(seeded.ApplicationId);
        Assert.Equal(cv1, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Concurrent_Puts_From_The_Same_Version_Exactly_One_Wins()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cv1 = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v1.pdf");
        var cv2 = await SeedCvAsync(companyId, seeded.CandidateId, "cv-v2.pdf", Now.AddMinutes(1));
        using var clientA = await ClientAs(RecruiterUser, companyId);
        using var clientB = await ClientAs(RecruiterUser, companyId);

        HttpResponseMessage[] responses;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            responses = await Task.WhenAll(
                clientA.PutAsJsonAsync(CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cv1, expectedVersion = 1 }),
                clientB.PutAsJsonAsync(CvUrl(companyId, seeded.VacancyId, seeded.ApplicationId), new { cvDocumentId = cv2, expectedVersion = 1 }));
        }

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected status: {r.StatusCode}"));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict, "concurrency");

        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<SetCvPayload>();
        var saved = await LoadApplicationAsync(seeded.ApplicationId);
        Assert.Equal(winner!.CvDocumentId, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static async Task<ProblemPayload> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem!.Code);
        return problem;
    }

    private static MultipartFormDataContent BuildCvUpload(string fileName)
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;

        var content = new MultipartFormDataContent
        {
            { new StringContent("Newer CV"), "Title" },
            { new StringContent("Cv"), "Kind" },
        };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "File", fileName);
        return content;
    }

    private sealed record SetCvPayload(
        Guid Id, Guid VacancyId, Guid CandidateId, Guid? CvDocumentId, int Version, DateTimeOffset UpdatedAt);

    private sealed record GetApplicationPayload(
        Guid Id, Guid? CvDocumentId, string? CvFileName, string? CvContentType, long? CvFileSize, DateTimeOffset? CvUploadedAt,
        int Version, Guid? CurrentCandidateCvDocumentId, string? CurrentCandidateCvFileName);

    private sealed record UploadPayload(Guid Id, string Kind, string FileName);

    private sealed record ProblemPayload(string Error, string Code);
}
