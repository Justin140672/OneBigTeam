using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for GET /candidates/{c}/documents. See
/// ListCandidateDocumentsHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 ordered by CreatedAt desc, company isolation.
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

    private sealed record ListPayload(List<Item> Items);

    private sealed record Item(
        Guid Id, string Title, string FileName, long FileSize, string ContentType, DateTimeOffset CreatedAt);
}
