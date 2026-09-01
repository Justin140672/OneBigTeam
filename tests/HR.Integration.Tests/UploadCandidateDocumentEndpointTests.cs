using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for POST /candidates/{c}/documents (multipart upload). See
/// UploadCandidateDocumentHandlerTests / UploadCandidateDocumentValidatorTests in
/// HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 201 + persisted row, unknown candidate 404,
/// cross-company 404, disallowed file type 422, empty file 422, missing title 422.
/// </summary>
[Collection("Integration")]
public class UploadCandidateDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000cc-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000cc-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UploadCandidateDocumentEndpointTests(ApiWebApplicationFactory factory)
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

    private static MultipartFormDataContent BuildUpload(
        string title = "CV",
        string fileName = "cv.pdf",
        string contentType = "application/pdf",
        int size = 2048)
    {
        var bytes = new byte[size];
        if (size >= 4) { bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46; }

        var content = new MultipartFormDataContent();
        if (title is not null)
            content.Add(new StringContent(title), "Title");

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "File", fileName);
        return content;
    }

    private static string Url(Guid companyId, Guid candidateId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents";

    [Fact]
    public async Task Post_Document_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(Url(Guid.NewGuid(), Guid.NewGuid()), BuildUpload());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsync(Url(companyId, candidateId), BuildUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Creates_And_Persists_Row()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(Url(companyId, candidateId), BuildUpload("Signed contract", "contract.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        Assert.Equal(candidateId, payload!.CandidateId);
        Assert.Equal("Signed contract", payload.Title);
        Assert.Equal("contract.pdf", payload.FileName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.CandidateDocuments.SingleAsync(d => d.Id == payload.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(candidateId, saved.CandidateId);
        Assert.Equal("Signed contract", saved.Title);
    }

    [Fact]
    public async Task Post_Document_Returns_NotFound_For_Unknown_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(Url(companyId, Guid.NewGuid()), BuildUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Returns_NotFound_For_Candidate_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsync(Url(companyB, candidateId), BuildUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Returns_UnprocessableEntity_For_Disallowed_File_Type()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(
            Url(companyId, candidateId), BuildUpload("Notes", "notes.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Returns_UnprocessableEntity_For_Empty_File()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(
            Url(companyId, candidateId), BuildUpload("Empty", "empty.pdf", "application/pdf", size: 0));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Document_Returns_UnprocessableEntity_When_Title_Missing()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(
            Url(companyId, candidateId), BuildUpload(title: "   "));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, Guid CandidateId, string Title, string FileName, long FileSize,
        string ContentType, DateTimeOffset CreatedAt);
}
