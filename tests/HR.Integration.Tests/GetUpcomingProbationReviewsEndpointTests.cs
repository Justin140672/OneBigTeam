using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetUpcomingProbationReviewsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("eeeeeeee-0000-0000-0000-000000000031");
    private static readonly Guid User2 = new("eeeeeeee-0000-0000-0000-000000000032");
    private static readonly Guid User3 = new("eeeeeeee-0000-0000-0000-000000000033");

    public GetUpcomingProbationReviewsEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_UpcomingReviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_UpcomingReviews_Returns_Empty_When_No_Pending_Reviews()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        // Create a record but no reviews.
        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId        = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate         = "2026-06-01",
            expectedEndDate   = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_UpcomingReviews_Returns_Pending_Reviews_With_EmployeeId()
    {
        using var client = _factory.CreateClient();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate         = "2026-06-01",
            expectedEndDate   = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<RecordPayload>();

        // Create a review due within the next 30 days.
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd");
        await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType        = "ManagerCheckIn",
            dueDate
        });

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(employeeId,  item.EmployeeId);
        Assert.Equal(record.Id,   item.ProbationRecordId);
        Assert.Equal("ManagerCheckIn", item.ReviewType);
    }

    [Fact]
    public async Task Get_UpcomingReviews_Excludes_Completed_Reviews()
    {
        using var client = _factory.CreateClient();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate         = "2026-06-01",
            expectedEndDate   = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<RecordPayload>();

        // Create and complete a review.
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd");
        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType        = "ManagerCheckIn",
            dueDate
        });
        var review = await reviewResponse.Content.ReadFromJsonAsync<ReviewPayload>();

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{record.Id}/reviews/{review!.Id}/complete",
            new { completedByEmployeeId = User3, notes = "Done." });

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record RecordPayload(Guid Id, Guid CompanyId);
    private sealed record ReviewPayload(Guid Id);
    private sealed record UpcomingPayload(IReadOnlyList<UpcomingItem> Items);
    private sealed record UpcomingItem(
        Guid ReviewId,
        Guid ProbationRecordId,
        Guid EmployeeId,
        string ReviewType,
        DateOnly DueDate);
}
