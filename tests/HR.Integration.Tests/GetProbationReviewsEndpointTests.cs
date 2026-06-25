using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetProbationReviewsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-0000-0000-0000-000000000003");

    public GetProbationReviewsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_ProbationReviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/probation-records/{Guid.NewGuid()}/reviews");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReviews_Returns_NotFound_For_Unknown_ProbationRecord()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}/reviews");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReviews_Returns_Empty_List_When_No_Reviews_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<RecordPayload>();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{record!.Id}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReviewsPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_ProbationReviews_Returns_Reviews_For_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<RecordPayload>();

        await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType = "ManagerCheckIn",
            dueDate = "2026-07-01"
        });

        await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record.Id,
            reviewType = "HrReview",
            dueDate = "2026-08-01"
        });

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{record.Id}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReviewsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Equal("Pending", item.Status));
    }

    private sealed record RecordPayload(Guid Id, Guid CompanyId);

    private sealed record ReviewsPayload(IReadOnlyList<ReviewItem> Items);

    private sealed record ReviewItem(
        Guid Id,
        Guid ProbationRecordId,
        string ReviewType,
        DateOnly DueDate,
        string Status,
        DateTimeOffset? CompletedAt,
        Guid? CompletedByEmployeeId,
        string? Notes);
}
