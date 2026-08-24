using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// PROB-02: reporting-hierarchy / HR-administrator resource-level authorization for the two
/// Probation endpoints guarded by <c>HR.Modules.Probation.Services.ProbationResourceAuthorizer</c>
/// — GetUpcomingProbationReviews and GetProbationReview. The "probation:review" policy those
/// endpoints carry only proves Manager/HrAdministrator role membership; it never proves the caller
/// has a reporting relationship to the specific employee(s) whose probation review data is being
/// requested, so these tests exercise that resource-ownership check end-to-end over real HTTP.
/// Mirrors HR.Integration.Tests.SicknessResourceAuthorizationTests' pattern for SICK-02's
/// equivalent authorizer.
/// </summary>
[Collection("Integration")]
public class ProbationResourceAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ProbationResourceAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetUpcomingProbationReviews
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingProbationReviews_Visible_To_Direct_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetUpcomingProbationReviews_Visible_To_Indirect_Manager_Via_Skip_Level_Hierarchy()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var seniorManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(seniorManager, companyId, SystemRoles.Manager);
        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, manager, seniorManager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var seniorManagerClient = await ClientFor(companyId, seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetUpcomingProbationReviews_Hidden_From_Peer_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var peerManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(peerManager, companyId, SystemRoles.Manager);

        await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetUpcomingProbationReviews_Hidden_From_Unrelated_Employees_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var unrelatedManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(unrelatedManager, companyId, SystemRoles.Manager);
        var unrelatedReport = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, unrelatedReport, unrelatedManager);

        await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetUpcomingProbationReviews_HrAdministrator_Sees_Company_Wide()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateUpcomingReviewAsync(hrClient, companyId, report);

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpcomingPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetUpcomingProbationReviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetProbationReview (single-resource read; unauthorized -> 404, never 403)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProbationReview_Visible_To_Direct_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
    }

    [Fact]
    public async Task GetProbationReview_Visible_To_Indirect_Manager_Via_Skip_Level_Hierarchy()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var seniorManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(seniorManager, companyId, SystemRoles.Manager);
        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, manager, seniorManager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var seniorManagerClient = await ClientFor(companyId, seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
    }

    [Fact]
    public async Task GetProbationReview_Returns_NotFound_Not_Forbidden_For_Peer_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var peerManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(peerManager, companyId, SystemRoles.Manager);

        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/{reviewId}");

        // PROB-02: a manager unrelated to the review's employee must receive the same 404 as a
        // genuinely nonexistent review id — never 403 — so review ids cannot be enumerated to
        // fish for the existence of unrelated reviews.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProbationReview_Returns_NotFound_Not_Forbidden_For_Unrelated_Employees_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var unrelatedManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(unrelatedManager, companyId, SystemRoles.Manager);
        var unrelatedReport = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, unrelatedReport, unrelatedManager);

        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/probation-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProbationReview_HrAdministrator_Sees_Any_Review()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/probation-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
    }

    [Fact]
    public async Task GetProbationReview_Returns_NotFound_For_CrossCompany_Guessed_Id()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateUpcomingReviewAsync(hrClient, companyId, report);

        using var otherHrClient = await HrAdminClientAsync(otherCompanyId);
        var response = await otherHrClient.GetAsync($"/api/companies/{otherCompanyId}/probation-reviews/{reviewId}");

        // Even an HR Administrator of a different company cannot fetch Company A's review by
        // guessing/knowing its id — the review lookup itself is scoped by CompanyId.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProbationReview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/probation-reviews/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> HrAdminClientAsync(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    /// <summary>
    /// An employee's id doubles as the identity user id for the linked account (see
    /// GetMyEmployeeHandler's `e.Id == userId` lookup), so this id is used both as the probation
    /// resource's EmployeeId and as the TestAuthHandler.UserHeader value when acting "as" that
    /// employee/manager. Employee role is always assigned; Manager/HrAdministrator must be
    /// assigned explicitly by callers via AssignRoleAsync.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync(
        HttpClient hrClient, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData reference)
    {
        var response = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId,
                reference,
                "Test",
                $"Employee-{Guid.NewGuid():N}",
                $"probauth.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();

        await TestRoleSeeder.AssignRoleAsync(_factory, payload!.Id, SystemRoles.Employee, companyId);

        return payload.Id;
    }

    private async Task AssignRoleAsync(Guid userId, Guid companyId, Guid roleId) =>
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a probation record and a Pending review due within the next 30 days, so it
    /// surfaces from both GetUpcomingProbationReviews and a direct GetProbationReview lookup.
    /// </summary>
    private async Task<Guid> CreateUpcomingReviewAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var recordResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<RecordPayload>();

        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd");
        var reviewResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType = "ManagerCheckIn",
            dueDate
        });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<ReviewCreatedPayload>();

        return review!.Id;
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record RecordPayload(Guid Id);
    private sealed record ReviewCreatedPayload(Guid Id);

    private sealed record UpcomingPayload(IReadOnlyList<UpcomingItem> Items);
    private sealed record UpcomingItem(Guid ReviewId, Guid ProbationRecordId, Guid EmployeeId, string ReviewType, DateOnly DueDate, Guid? TaskId);

    private sealed record ReviewPayload(
        Guid Id,
        Guid CompanyId,
        Guid ProbationRecordId,
        Guid EmployeeId,
        string ReviewType,
        DateOnly DueDate,
        string Status,
        DateTimeOffset? CompletedAt,
        string? Notes,
        DateOnly RecordStartDate,
        DateOnly RecordExpectedEndDate,
        string RecordStatus);
}
