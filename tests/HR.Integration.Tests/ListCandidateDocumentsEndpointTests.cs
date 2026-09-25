using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for GET /candidates/{c}/documents. See
/// ListCandidateDocumentsHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 ordered by CreatedAt desc, company isolation,
/// and (internal recruitment Ticket 2) IsCurrentCv + ReferencingApplicationCount.
/// </summary>
[Collection("Integration")]
public class ListCandidateDocumentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c9-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c9-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListCandidateDocumentsEndpointTests(ApiWebApplicationFactory factory)
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

    private static string DocumentsUrl(Guid companyId, Guid candidateId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents";

    private static string CvUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv";

    [Fact]
    public async Task Get_Documents_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_Returns_Documents_For_Candidate()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, Now, "CV", "cv.pdf");
        await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, Now.AddMinutes(1), "Cover Letter", "cover.pdf");
        using var client = await ClientAs(RecruiterUser, companyId);

        var payload = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/candidates/{candidateId}/documents");

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Cover Letter", payload.Items[0].Title); // newest first
    }

    [Fact]
    public async Task Get_Documents_Isolates_By_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyA, candidateId, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var payload = await clientB.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyB}/candidates/{candidateId}/documents");

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    // ---- Internal recruitment Ticket 2: IsCurrentCv + ReferencingApplicationCount ------------------

    [Fact]
    public async Task Get_Documents_Flags_Only_Newest_Cv_As_Current_And_Retains_Replaced_Cv_After_Upload()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var originalCvId = await UploadAsync(client, companyId, candidateId, "emma-cv-v1.pdf", kind: "Cv");

        var beforeReplacement = await ListAsync(client, companyId, candidateId);
        var onlyCv = Assert.Single(beforeReplacement.Items);
        Assert.Equal(originalCvId, onlyCv.Id);
        Assert.Equal("Cv", onlyCv.Kind);
        Assert.True(onlyCv.IsCurrentCv);

        var replacementCvId = await UploadAsync(client, companyId, candidateId, "emma-cv-v2.pdf", kind: "Cv");
        // A newer non-CV document must not take the current-CV flag.
        var coverLetterId = await UploadAsync(client, companyId, candidateId, "emma-cover.pdf", kind: null);

        var payload = await ListAsync(client, companyId, candidateId);

        Assert.Equal(3, payload.Items.Count);
        Assert.Equal(new[] { coverLetterId, replacementCvId, originalCvId }, payload.Items.Select(i => i.Id)); // newest first

        var coverLetter = payload.Items[0];
        Assert.Equal("Other", coverLetter.Kind);
        Assert.False(coverLetter.IsCurrentCv);

        var replacement = payload.Items[1];
        Assert.Equal("emma-cv-v2.pdf", replacement.FileName);
        Assert.True(replacement.IsCurrentCv);

        // The original CV is retained (not overwritten) and is no longer current.
        var original = payload.Items[2];
        Assert.Equal("emma-cv-v1.pdf", original.FileName);
        Assert.Equal("Cv", original.Kind);
        Assert.False(original.IsCurrentCv);

        Assert.All(payload.Items, i => Assert.Equal(0, i.ReferencingApplicationCount));
    }

    [Fact]
    public async Task Get_Documents_Reports_ReferencingApplicationCount_As_Application_Cvs_Are_Set_And_Replaced()
    {
        var companyId = Guid.NewGuid();
        var first = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var (secondVacancyId, secondApplicationId) =
            await SeedSecondApplicationForCandidateAsync(companyId, first.CandidateId, first.CvReviewStageId);
        var oldCvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, first.CandidateId, Now, "Old CV", "cv-v1.pdf", CandidateDocumentKind.Cv);
        var newCvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, first.CandidateId, Now.AddMinutes(1), "New CV", "cv-v2.pdf", CandidateDocumentKind.Cv);
        using var client = await ClientAs(RecruiterUser, companyId);

        // Both applications were submitted with the old CV.
        await PutCvAsync(client, companyId, first.VacancyId, first.ApplicationId, oldCvId, expectedVersion: 1);
        await PutCvAsync(client, companyId, secondVacancyId, secondApplicationId, oldCvId, expectedVersion: 1);

        var bothOnOld = await ListAsync(client, companyId, first.CandidateId);
        Assert.Equal(2, bothOnOld.Items.Single(i => i.Id == oldCvId).ReferencingApplicationCount);
        Assert.Equal(0, bothOnOld.Items.Single(i => i.Id == newCvId).ReferencingApplicationCount);

        // Replace only the first application's CV with the new document.
        await PutCvAsync(client, companyId, first.VacancyId, first.ApplicationId, newCvId, expectedVersion: 2);

        var payload = await ListAsync(client, companyId, first.CandidateId);

        Assert.Equal(2, payload.Items.Count);
        var oldCv = payload.Items.Single(i => i.Id == oldCvId);
        var newCv = payload.Items.Single(i => i.Id == newCvId);
        // The second application still references the old CV, so it is retained and counted.
        Assert.Equal(1, oldCv.ReferencingApplicationCount);
        Assert.False(oldCv.IsCurrentCv);
        Assert.Equal(1, newCv.ReferencingApplicationCount);
        Assert.True(newCv.IsCurrentCv);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static async Task<ListPayload> ListAsync(HttpClient client, Guid companyId, Guid candidateId)
    {
        var response = await client.GetAsync(DocumentsUrl(companyId, candidateId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListPayload>())!;
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, Guid companyId, Guid candidateId, string fileName, string? kind)
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46; // %PDF

        using var content = new MultipartFormDataContent
        {
            { new StringContent(fileName), "Title" },
        };
        if (kind is not null)
            content.Add(new StringContent(kind), "Kind");

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "File", fileName);

        var response = await client.PostAsync(DocumentsUrl(companyId, candidateId), content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(uploaded);
        return uploaded!.Id;
    }

    private static async Task PutCvAsync(
        HttpClient client, Guid companyId, Guid vacancyId, Guid applicationId, Guid cvDocumentId, int expectedVersion)
    {
        var response = await client.PutAsJsonAsync(
            CvUrl(companyId, vacancyId, applicationId), new { cvDocumentId, expectedVersion });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A second application for the SAME candidate (on a new vacancy) in the same company.
    /// RecruitmentTestSeeder.SeedApplicationAsync always creates a fresh candidate, so this reuses
    /// the candidate and the stage it seeded instead.
    /// </summary>
    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedSecondApplicationForCandidateAsync(
        Guid companyId, Guid candidateId, Guid stageId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Platform Engineer", null, Guid.NewGuid(), Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateId, stageId, null, Now);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id);
    }

    private sealed record ListPayload(List<Item> Items);

    private sealed record Item(
        Guid Id, string Title, string Kind, string FileName, long FileSize, string ContentType, DateTimeOffset CreatedAt,
        bool IsCurrentCv, int ReferencingApplicationCount);

    private sealed record UploadPayload(Guid Id, string Kind, string FileName);
}
