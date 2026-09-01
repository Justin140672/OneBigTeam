using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for GET /recruitment/applications/search. See
/// SearchApplicationsHandlerTests / SearchApplicationsValidatorTests in HR.Modules.Recruitment.Tests
/// for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 paged result, free-text search filter,
/// vacancy filter, company isolation, page-size validation 422.
/// </summary>
[Collection("Integration")]
public class SearchApplicationsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000cd-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000cd-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public SearchApplicationsEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Base(Guid companyId) => $"/api/companies/{companyId}/recruitment/applications/search";

    [Fact]
    public async Task Get_Search_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Base(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Search_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(Base(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Search_Returns_Paged_Applications()
    {
        var companyId = Guid.NewGuid();
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Nina", "Patel");
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Omar", "Khan");
        using var client = await ClientAs(RecruiterUser, companyId);

        var payload = await client.GetFromJsonAsync<SearchPayload>($"{Base(companyId)}?pageNumber=1&pageSize=20");

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal(1, payload.PageNumber);
    }

    [Fact]
    public async Task Get_Search_Filters_By_Free_Text()
    {
        var companyId = Guid.NewGuid();
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Nina", "Patel");
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Omar", "Khan");
        using var client = await ClientAs(RecruiterUser, companyId);

        var payload = await client.GetFromJsonAsync<SearchPayload>($"{Base(companyId)}?search=Patel");

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Contains("Patel", item.CandidateName);
    }

    [Fact]
    public async Task Get_Search_Filters_By_Vacancy()
    {
        var companyId = Guid.NewGuid();
        var a = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Nina", "Patel");
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Omar", "Khan");
        using var client = await ClientAs(RecruiterUser, companyId);

        var payload = await client.GetFromJsonAsync<SearchPayload>($"{Base(companyId)}?vacancyId={a.VacancyId}");

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(a.VacancyId, item.VacancyId);
    }

    [Fact]
    public async Task Get_Search_Isolates_By_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now, "Nina", "Patel");
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var payload = await clientB.GetFromJsonAsync<SearchPayload>(Base(companyB));

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
    }

    [Fact]
    public async Task Get_Search_Returns_UnprocessableEntity_For_Oversized_Page()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"{Base(companyId)}?pageSize=500");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record SearchPayload(
        List<Item> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);

    private sealed record Item(
        Guid ApplicationId, Guid CandidateId, string CandidateName, string CandidateEmail,
        Guid VacancyId, string VacancyTitle, Guid CurrentStageId, DateTimeOffset AppliedAt);
}
