using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CompleteProbationReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid User5 = new("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid User6 = new("aaaaaaaa-0000-0000-0000-000000000006");
    private static readonly Guid User7 = new("aaaaaaaa-0000-0000-0000-000000000007");

    public CompleteProbationReviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User5, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User6, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User7, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_Complete_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/probation-records/{Guid.NewGuid()}/reviews/{Guid.NewGuid()}/complete",
            new { completedByEmployeeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Complete_Completes_ManagerCheckIn_Without_Outcome()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "ManagerCheckIn");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = completedBy,
                notes = "Good progress."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Completed", payload!.Status);
        Assert.Equal(completedBy, payload.CompletedByEmployeeId);
        Assert.Equal("Good progress.", payload.Notes);
        Assert.NotNull(payload.CompletedAt);
    }

    [Fact]
    public async Task Post_Complete_FinalDecision_With_Pass_Transitions_Record_To_Passed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "FinalDecision");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = completedBy,
                notes = "Excellent work.",
                outcome = "Pass",
                decisionDate = "2026-09-01"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.Equal("Completed", review!.Status);

        var record = await GetRecord(client, companyId, recordId);
        Assert.Equal("Passed", record.Status);
        Assert.Equal(completedBy, record.DecisionMakerEmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 1), record.DecisionDate);
        Assert.Equal("Excellent work.", record.OutcomeNotes);
    }

    [Fact]
    public async Task Post_Complete_FinalDecision_With_Fail_Transitions_Record_To_Failed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "FinalDecision");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = completedBy,
                notes = "Did not meet targets.",
                outcome = "Fail",
                decisionDate = "2026-09-01"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var record = await GetRecord(client, companyId, recordId);
        Assert.Equal("Failed", record.Status);
        Assert.Equal(completedBy, record.DecisionMakerEmployeeId);
    }

    [Fact]
    public async Task Post_Complete_ExtensionConfirmation_With_Extend_Transitions_Record_To_Extended()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "ExtensionConfirmation");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = completedBy,
                outcome = "Extend",
                decisionDate = "2026-09-01",
                newExpectedEndDate = "2026-12-01",
                extensionReason = "Needs more time to meet targets."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var record = await GetRecord(client, companyId, recordId);
        Assert.Equal("Extended", record.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), record.ExpectedEndDate);
        Assert.Equal("Needs more time to meet targets.", record.ExtensionReason);
        Assert.Equal(completedBy, record.DecisionMakerEmployeeId);
    }

    [Fact]
    public async Task Post_Complete_Returns_NotFound_For_Unknown_Review()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, _) = await CreateRecordAndReview(client, companyId, "ManagerCheckIn");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{Guid.NewGuid()}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId = Guid.NewGuid(),
                completedByEmployeeId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Complete_Returns_BadRequest_When_FinalDecision_Has_No_Outcome()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User6.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "FinalDecision");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Complete_Returns_BadRequest_When_Review_Already_Completed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User7.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (recordId, reviewId) = await CreateRecordAndReview(client, companyId, "ManagerCheckIn");

        var firstComplete = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = Guid.NewGuid()
            });
        firstComplete.EnsureSuccessStatusCode();

        var secondComplete = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.BadRequest, secondComplete.StatusCode);
    }

    private async Task<(Guid recordId, Guid reviewId)> CreateRecordAndReview(
        HttpClient client,
        Guid companyId,
        string reviewType)
    {
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

        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType,
            dueDate = "2026-07-01"
        });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<ReviewPayload>();

        return (record.Id, review!.Id);
    }

    private async Task<RecordDetailPayload> GetRecord(HttpClient client, Guid companyId, Guid recordId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/probation-records/{recordId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecordDetailPayload>())!;
    }

    private sealed record RecordPayload(Guid Id, Guid CompanyId);

    private sealed record RecordDetailPayload(
        Guid Id,
        Guid CompanyId,
        string Status,
        DateOnly ExpectedEndDate,
        string? ExtensionReason,
        DateOnly? DecisionDate,
        Guid? DecisionMakerEmployeeId,
        string? OutcomeNotes);

    private sealed record ReviewPayload(
        Guid Id,
        Guid CompanyId,
        Guid ProbationRecordId,
        string ReviewType,
        DateOnly DueDate,
        string Status,
        DateTimeOffset? CompletedAt,
        Guid? CompletedByEmployeeId,
        string? Notes);
}
