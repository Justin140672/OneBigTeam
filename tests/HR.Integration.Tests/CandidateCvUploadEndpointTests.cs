using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 1 end-to-end: uploading a document with the <c>Kind=Cv</c> form field marks it as the
/// candidate's CV, and GET application then surfaces that CV summary alongside the CV review notes.
/// </summary>
[Collection("Integration")]
public class CandidateCvUploadEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00cf20-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CandidateCvUploadEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
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

    private static MultipartFormDataContent BuildCvUpload(string? kind = "Cv")
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;

        var content = new MultipartFormDataContent
        {
            { new StringContent("Emma Clarke CV"), "Title" },
        };
        if (kind is not null)
            content.Add(new StringContent(kind), "Kind");

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "File", "emma-cv.pdf");
        return content;
    }

    private static string UploadUrl(Guid companyId, Guid candidateId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents";

    private static string ApplicationUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}";

    [Fact]
    public async Task Upload_With_Kind_Cv_Is_Surfaced_By_Get_Application_Along_With_Review_Notes()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var uploadResponse = await client.PostAsync(UploadUrl(companyId, seeded.CandidateId), BuildCvUpload("Cv"));
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(uploaded);
        Assert.Equal("Cv", uploaded!.Kind);

        // Record CV review notes so GET application returns both halves of Ticket 1.
        var notesResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/cv-review-notes",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "Great CV" });
        Assert.Equal(HttpStatusCode.OK, notesResponse.StatusCode);

        var getResponse = await client.GetAsync(ApplicationUrl(companyId, seeded.VacancyId, seeded.ApplicationId));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var application = await getResponse.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(application);
        Assert.Equal("Great CV", application!.CvReviewNotes);
        Assert.Equal(uploaded.Id, application.CvDocumentId);
        Assert.Equal("emma-cv.pdf", application.CvFileName);
        Assert.Equal("application/pdf", application.CvContentType);
        Assert.NotNull(application.CvUploadedAt);
    }

    [Fact]
    public async Task Upload_Without_Kind_Is_Not_Treated_As_Cv()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var uploadResponse = await client.PostAsync(UploadUrl(companyId, seeded.CandidateId), BuildCvUpload(kind: null));
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.Equal("Other", uploaded!.Kind);

        var getResponse = await client.GetAsync(ApplicationUrl(companyId, seeded.VacancyId, seeded.ApplicationId));
        var application = await getResponse.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.Null(application!.CvDocumentId);
    }

    [Fact]
    public async Task Upload_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(UploadUrl(Guid.NewGuid(), Guid.NewGuid()), BuildCvUpload());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_NotFound_For_Candidate_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsync(UploadUrl(companyB, seeded.CandidateId), BuildCvUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, Guid CandidateId, string Title, string Kind, string FileName,
        long FileSize, string ContentType, DateTimeOffset CreatedAt);

    private sealed record ApplicationPayload(
        Guid Id, string? CvReviewNotes, DateTimeOffset? CvReviewedAt, Guid? CvReviewedByUserId,
        Guid? CvDocumentId, string? CvFileName, string? CvContentType, long? CvFileSize,
        DateTimeOffset? CvUploadedAt);
}
