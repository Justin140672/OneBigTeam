using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateProbationReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("eeeeeeee-0000-0000-0000-000000000003");

    public CreateProbationReviewEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_ProbationReviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/probation-reviews", new
        {
            probationRecordId = Guid.NewGuid(),
            reviewType = "ManagerCheckIn",
            dueDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ProbationReviews_Creates_Review()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
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
        var record = await recordResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType = "ManagerCheckIn",
            dueDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<ProbationReviewPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(record.Id, payload.ProbationRecordId);
        Assert.Equal("ManagerCheckIn", payload.ReviewType);
        Assert.Equal("Pending", payload.Status);
    }

    [Fact]
    public async Task Post_ProbationReviews_Returns_NotFound_For_Unknown_ProbationRecord()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = Guid.NewGuid(),
            reviewType = "HrReview",
            dueDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ProbationReviews_Returns_UnprocessableEntity_For_Invalid_ReviewType()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = Guid.NewGuid(),
            reviewType = "NotAType",
            dueDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ProbationReviews_Returns_Conflict_When_Same_ReviewType_Already_Exists()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = new Guid("eeeeeeee-0000-0000-0000-000000000004");
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, HR.Modules.Identity.Domain.SystemRoles.HrAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId        = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate         = "2026-06-01",
            expectedEndDate   = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType        = "ManagerCheckIn",
            dueDate           = "2026-07-01"
        });
        first.EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record.Id,
            reviewType        = "ManagerCheckIn",
            dueDate           = "2026-07-15"
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Post_ProbationReviews_Allows_Different_ReviewTypes_For_Same_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = new Guid("eeeeeeee-0000-0000-0000-000000000005");
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, HR.Modules.Identity.Domain.SystemRoles.HrAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var recordResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId        = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate         = "2026-06-01",
            expectedEndDate   = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType        = "ManagerCheckIn",
            dueDate           = "2026-07-01"
        });

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record.Id,
            reviewType        = "HrReview",
            dueDate           = "2026-08-01"
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    private sealed record ProbationRecordPayload(Guid Id, Guid CompanyId);

    private sealed record ProbationReviewPayload(
        Guid Id,
        Guid CompanyId,
        Guid ProbationRecordId,
        string ReviewType,
        DateOnly DueDate,
        string Status,
        DateTimeOffset CreatedAt);
}
