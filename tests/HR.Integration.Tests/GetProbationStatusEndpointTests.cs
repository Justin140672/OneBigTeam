using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetProbationStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000021");
    private static readonly Guid User2 = new("cccccccc-0000-0000-0000-000000000022");
    private static readonly Guid User3 = new("cccccccc-0000-0000-0000-000000000023");
    private static readonly Guid User4 = new("cccccccc-0000-0000-0000-000000000024");

    public GetProbationStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_ProbationStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/probation-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationStatus_Returns_HasRecord_False_When_Employee_Has_No_Record()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User1, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasRecord);
        Assert.Null(payload.Status);
    }

    [Fact]
    public async Task Get_ProbationStatus_Returns_Active_After_Record_Created()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User2, companyId);

        var employeeId = await CreateProbationRecordAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasRecord);
        Assert.Equal("Active", payload.Status);
    }

    [Fact]
    public async Task Get_ProbationStatus_Returns_Passed_After_FinalDecision_Review_Completed_With_Pass()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User3, companyId);

        var (employeeId, recordId) = await CreateProbationRecordWithIdAsync(client, companyId);
        var reviewId = await CreateReviewAsync(client, companyId, recordId, "FinalDecision");
        await CompleteReviewAsync(client, companyId, recordId, reviewId, "Pass");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasRecord);
        Assert.Equal("Passed", payload.Status);
    }

    [Fact]
    public async Task Get_ProbationStatus_Returns_Failed_After_FinalDecision_Review_Completed_With_Fail()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User4, companyId);

        var (employeeId, recordId) = await CreateProbationRecordWithIdAsync(client, companyId);
        var reviewId = await CreateReviewAsync(client, companyId, recordId, "FinalDecision");
        await CompleteReviewAsync(client, companyId, recordId, reviewId, "Fail");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasRecord);
        Assert.Equal("Failed", payload.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid user, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, user, companyId);
        return client;
    }

    private async Task<Guid> CreateProbationRecordAsync(HttpClient client, Guid companyId)
    {
        var employeeId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
        });
        response.EnsureSuccessStatusCode();
        return employeeId;
    }

    private async Task<(Guid EmployeeId, Guid RecordId)> CreateProbationRecordWithIdAsync(HttpClient client, Guid companyId)
    {
        var employeeId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
        });
        response.EnsureSuccessStatusCode();
        var record = (await response.Content.ReadFromJsonAsync<RecordPayload>())!;
        return (employeeId, record.Id);
    }

    private async Task<Guid> CreateReviewAsync(HttpClient client, Guid companyId, Guid recordId, string reviewType)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = recordId,
            reviewType,
            dueDate = "2026-07-01",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReviewPayload>())!.Id;
    }

    private async Task CompleteReviewAsync(HttpClient client, Guid companyId, Guid recordId, Guid reviewId, string outcome)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                completedByEmployeeId = Guid.NewGuid(),
                notes = "Integration test.",
                outcome,
                decisionDate = "2026-09-01",
            });
        response.EnsureSuccessStatusCode();
    }

    private sealed record RecordPayload(Guid Id, Guid CompanyId);

    private sealed record ReviewPayload(Guid Id);

    private sealed record StatusPayload(bool HasRecord, string? Status);
}
