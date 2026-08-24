using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateProbationRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("dddddddd-0000-0000-0000-000000000004");
    private static readonly Guid User5 = new("dddddddd-0000-0000-0000-000000000005");

    public UpdateProbationRecordEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User5, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}", new
        {
            managerEmployeeId = Guid.NewGuid(),
            expectedEndDate = "2026-09-01"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ProbationRecord_Updates_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = newManagerId,
                expectedEndDate = "2026-09-01",
                notes = "Updated via PUT."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdatedProbationRecordPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newManagerId, payload!.ManagerEmployeeId);
        Assert.Equal("Active", payload.Status);
        Assert.Equal("Updated via PUT.", payload.Notes);
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                managerEmployeeId = Guid.NewGuid(),
                expectedEndDate = "2026-09-01"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_Conflict_For_Passed_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User4, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = created!.Id,
            reviewType = "FinalDecision",
            dueDate = "2026-09-01"
        });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<ReviewItem>();

        var completeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews/{review!.Id}/complete",
            new
            {
                companyId,
                probationRecordId = created.Id,
                reviewId = review.Id,
                outcome = "Pass",
                decisionDate = "2026-09-01"
            });
        completeResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = managerId,
                expectedEndDate = "2026-12-01",
                notes = "Attempted edit after decision."
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_ProbationRecord_Changing_ExpectedEndDate_Recalculates_Pending_Reviews()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = created!.Id,
            reviewType = "FinalDecision",
            dueDate = "2026-09-01"
        });

        var beforeReviewsResponse = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews");
        var beforeReviews = await beforeReviewsResponse.Content.ReadFromJsonAsync<ReviewsPayload>();
        var originalFinalDecisionId = Assert.Single(beforeReviews!.Items).Id;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = managerId,
                expectedEndDate = "2026-12-01"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var afterReviewsResponse = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews");
        var afterReviews = await afterReviewsResponse.Content.ReadFromJsonAsync<ReviewsPayload>();

        var originalReview = afterReviews!.Items.Single(r => r.Id == originalFinalDecisionId);
        Assert.Equal("Cancelled", originalReview.Status);

        var newFinalDecision = afterReviews.Items.Single(r =>
            r.Id != originalFinalDecisionId && r.ReviewType == "FinalDecision");
        Assert.Equal("Pending", newFinalDecision.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), newFinalDecision.DueDate);
    }

    [Fact]
    public async Task Put_ProbationRecord_Unchanged_ExpectedEndDate_Does_Not_Recalculate_Reviews()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = created!.Id,
            reviewType = "FinalDecision",
            dueDate = "2026-09-01"
        });

        var beforeReviewsResponse = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews");
        var beforeReviews = await beforeReviewsResponse.Content.ReadFromJsonAsync<ReviewsPayload>();
        var originalFinalDecisionId = Assert.Single(beforeReviews!.Items).Id;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = managerId,
                expectedEndDate = "2026-09-01", // unchanged
                notes = "No date change."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var afterReviewsResponse = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews");
        var afterReviews = await afterReviewsResponse.Content.ReadFromJsonAsync<ReviewsPayload>();

        var onlyReview = Assert.Single(afterReviews!.Items);
        Assert.Equal(originalFinalDecisionId, onlyReview.Id);
        Assert.Equal("Pending", onlyReview.Status);
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_BadRequest_For_Missing_ExpectedEndDate()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User5, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

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

    private sealed record ProbationRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid ManagerEmployeeId,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        string Status,
        string? Notes,
        DateTimeOffset CreatedAt);

    private sealed record UpdatedProbationRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid ManagerEmployeeId,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        string Status,
        string? Notes,
        string? ExtensionReason,
        Guid? DecisionMakerEmployeeId,
        DateOnly? DecisionDate,
        string? OutcomeNotes,
        DateTimeOffset UpdatedAt);
}
