using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for DELETE /candidates/{c}/documents/{d}. See
/// DeleteCandidateDocumentHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 204 + row removed, unknown document 404,
/// cross-company 404 (a company B caller cannot delete company A's document).
/// </summary>
[Collection("Integration")]
public class DeleteCandidateDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000ca-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000ca-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public DeleteCandidateDocumentEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId, Guid candidateId, Guid documentId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents/{documentId}";

    [Fact]
    public async Task Delete_Document_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync(Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Document_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, candidateId, documentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Document_Removes_Row_And_Returns_NoContent()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, candidateId, documentId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.False(await db.CandidateDocuments.AnyAsync(d => d.Id == documentId));
    }

    [Fact]
    public async Task Delete_Document_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, candidateId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Internal recruitment Ticket 1: a CV submitted with an application cannot be deleted --------

    [Fact]
    public async Task Delete_Referenced_Cv_Returns_Conflict_And_Document_Still_Exists()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, seeded.CandidateId, Now, kind: HR.Modules.Recruitment.Domain.CandidateDocumentKind.Cv);
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, cvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.CandidateId, cvId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.Equal("conflict", problem!.Code);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.True(await db.CandidateDocuments.AnyAsync(d => d.Id == cvId));
        Assert.False(await db.CandidateDocumentDeletionOperations.AnyAsync(o => o.CandidateId == seeded.CandidateId));
        Assert.Equal(cvId, (await db.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Delete_Cv_Succeeds_After_The_Reference_Is_Removed_Via_The_Set_Cv_Endpoint()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var cvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, seeded.CandidateId, Now, kind: HR.Modules.Recruitment.Domain.CandidateDocumentKind.Cv);
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, cvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var blocked = await client.DeleteAsync(Url(companyId, seeded.CandidateId, cvId));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var removeReference = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/cv",
            new { cvDocumentId = (Guid?)null, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, removeReference.StatusCode);

        var response = await client.DeleteAsync(Url(companyId, seeded.CandidateId, cvId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.False(await db.CandidateDocuments.AnyAsync(d => d.Id == cvId));
        Assert.Null((await db.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Delete_Unreferenced_Cv_Succeeds_While_Another_Cv_Of_The_Candidate_Is_Referenced()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var referencedCvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, seeded.CandidateId, Now, fileName: "submitted.pdf", kind: HR.Modules.Recruitment.Domain.CandidateDocumentKind.Cv);
        var otherCvId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, seeded.CandidateId, Now, fileName: "old.pdf", kind: HR.Modules.Recruitment.Domain.CandidateDocumentKind.Cv);
        await RecruitmentTestSeeder.AttachApplicationCvAsync(_factory, seeded.ApplicationId, referencedCvId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.CandidateId, otherCvId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.False(await db.CandidateDocuments.AnyAsync(d => d.Id == otherCvId));
        Assert.True(await db.CandidateDocuments.AnyAsync(d => d.Id == referencedCvId));
    }

    private sealed record ProblemPayload(string Error, string Code);

    [Fact]
    public async Task Delete_Document_Returns_NotFound_For_Document_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyA, candidateId, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.DeleteAsync(Url(companyB, candidateId, documentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.True(await db.CandidateDocuments.AnyAsync(d => d.Id == documentId));
    }
}
