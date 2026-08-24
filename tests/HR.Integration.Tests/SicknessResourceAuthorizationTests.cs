using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// SICK-02: reporting-hierarchy / HR-administrator resource-level authorization for the three
/// Sickness endpoints guarded by <c>HR.Modules.Sickness.Services.SicknessResourceAuthorizer</c> —
/// GetMissingFitNotes, GetOverdueReturnToWorkReviews, and GetReturnToWorkReview. The
/// "sickness:review" policy those endpoints carry only proves Manager/HrAdministrator role
/// membership; it never proves the caller has a reporting relationship to the specific
/// employee(s) whose data is being requested, so these tests exercise that resource-ownership
/// check end-to-end over real HTTP. Mirrors LeaveResourceAuthorizationTests' pattern for
/// LEAVE-01/02's equivalent authorizer.
/// </summary>
[Collection("Integration")]
public class SicknessResourceAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SicknessResourceAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetMissingFitNotes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMissingFitNotes_Visible_To_Direct_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateStaleOpenSicknessRecordAsync(hrClient, companyId, report);
        await RunFitNoteRequestJobAsync();

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MissingFitNotesPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetMissingFitNotes_Visible_To_Indirect_Manager_Via_Skip_Level_Hierarchy()
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

        await CreateStaleOpenSicknessRecordAsync(hrClient, companyId, report);
        await RunFitNoteRequestJobAsync();

        using var seniorManagerClient = await ClientFor(companyId, seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MissingFitNotesPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetMissingFitNotes_Hidden_From_Peer_Manager()
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

        await CreateStaleOpenSicknessRecordAsync(hrClient, companyId, report);
        await RunFitNoteRequestJobAsync();

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MissingFitNotesPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetMissingFitNotes_Hidden_From_Unrelated_Employees_Manager()
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

        await CreateStaleOpenSicknessRecordAsync(hrClient, companyId, report);
        await RunFitNoteRequestJobAsync();

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MissingFitNotesPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetMissingFitNotes_HrAdministrator_Sees_Company_Wide()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateStaleOpenSicknessRecordAsync(hrClient, companyId, report);
        await RunFitNoteRequestJobAsync();

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MissingFitNotesPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetMissingFitNotes_PlainEmployee_Gets_Forbidden()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);

        using var employeeClient = await ClientFor(companyId, employee);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetOverdueReturnToWorkReviews
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_Visible_To_Direct_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateOverdueReturnToWorkReviewAsync(hrClient, companyId, report);

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverdueReviewsPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_Visible_To_Indirect_Manager_Via_Skip_Level_Hierarchy()
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

        await CreateOverdueReturnToWorkReviewAsync(hrClient, companyId, report);

        using var seniorManagerClient = await ClientFor(companyId, seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverdueReviewsPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_Hidden_From_Peer_Manager()
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

        await CreateOverdueReturnToWorkReviewAsync(hrClient, companyId, report);

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverdueReviewsPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_Hidden_From_Unrelated_Employees_Manager()
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

        await CreateOverdueReturnToWorkReviewAsync(hrClient, companyId, report);

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverdueReviewsPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_HrAdministrator_Sees_Company_Wide()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await CreateOverdueReturnToWorkReviewAsync(hrClient, companyId, report);

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverdueReviewsPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task GetOverdueReturnToWorkReviews_PlainEmployee_Gets_Forbidden()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);

        using var employeeClient = await ClientFor(companyId, employee);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetReturnToWorkReview (single-resource read; unauthorized -> 404, never 403)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReturnToWorkReview_Visible_To_Direct_Manager_Without_Notes()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
        // SICK-02: managers get a trimmed view that omits Notes, even though they're authorized
        // to view the review itself.
        Assert.Null(payload.Notes);
    }

    [Fact]
    public async Task GetReturnToWorkReview_Visible_To_Indirect_Manager_Via_Skip_Level_Hierarchy()
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

        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        using var seniorManagerClient = await ClientFor(companyId, seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
    }

    [Fact]
    public async Task GetReturnToWorkReview_Returns_NotFound_Not_Forbidden_For_Peer_Manager()
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

        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        // SICK-02: a manager unrelated to the review's employee must receive the same 404 as a
        // genuinely nonexistent review id — never 403 — so review ids cannot be enumerated.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnToWorkReview_Returns_NotFound_Not_Forbidden_For_Unrelated_Employees_Manager()
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

        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnToWorkReview_HrAdministrator_Sees_Any_Review_Including_Notes()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(report, payload!.EmployeeId);
        Assert.NotNull(payload.Notes);
    }

    [Fact]
    public async Task GetReturnToWorkReview_Returns_NotFound_For_CrossCompany_Guessed_Id()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, report);

        using var otherHrClient = await HrAdminClientAsync(otherCompanyId);
        var response = await otherHrClient.GetAsync($"/api/companies/{otherCompanyId}/return-to-work-reviews/{reviewId}");

        // Even an HR Administrator of a different company cannot fetch Company A's review by
        // guessing/knowing its id — the review lookup itself is scoped by CompanyId.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnToWorkReview_PlainEmployee_Gets_Forbidden()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewWithNotesAsync(hrClient, companyId, employee);

        using var employeeClient = await ClientFor(companyId, employee);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    /// GetMyEmployeeHandler's `e.Id == userId` lookup), so this id is used both as the sickness
    /// resource's EmployeeId and as the TestAuthHandler.UserHeader value when acting "as" that
    /// employee/manager. Employee role is always assigned by CreateEmployee's own downstream
    /// side effects are NOT relied upon here — callers must assign Manager/HrAdministrator via
    /// AssignRoleAsync explicitly as needed.
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
                $"sickauth.{Guid.NewGuid():N}@example.com"));
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

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    /// <summary>
    /// Creates an open (unclosed) sickness record starting well over the default
    /// FitNoteRequiredAfterDays threshold (7 calendar days) in the past, so
    /// FitNoteRequestJob will create a Pending SicknessEvidenceRequest for it on its next run.
    /// </summary>
    private async Task<Guid> CreateStaleOpenSicknessRecordAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var categoryId = await CreateCategoryAsync(hrClient, companyId);
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);

        var response = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = startDate.ToString("yyyy-MM-dd"),
                startDayPart = "FullDay"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SicknessRecordPayload>();
        return payload!.Id;
    }

    /// <summary>
    /// Creates and closes a sickness record whose return-to-work review due date is well in the
    /// past, then runs ReturnToWorkReminderJob so the review transitions Pending -> Overdue —
    /// mirroring FitNoteRequestCreatesTaskTests' job-driven state promotion pattern.
    /// ReturnToWorkRequiredAfterDays defaults to 1, so any closed record with >=1 total day
    /// produces a review with no HR-settings setup required.
    /// </summary>
    private async Task CreateOverdueReturnToWorkReviewAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var categoryId = await CreateCategoryAsync(hrClient, companyId);
        var startDate = new DateOnly(2026, 6, 1);
        var endDate = new DateOnly(2026, 6, 3);

        var createResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = startDate.ToString("yyyy-MM-dd"),
                startDayPart = "FullDay"
            });
        createResponse.EnsureSuccessStatusCode();
        var recordId = (await createResponse.Content.ReadFromJsonAsync<SicknessRecordPayload>())!.Id;

        var closeResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = endDate.ToString("yyyy-MM-dd"),
                endDayPart = "FullDay"
            });
        closeResponse.EnsureSuccessStatusCode();

        await RunReturnToWorkReminderJobAsync();
    }

    /// <summary>
    /// Creates and closes a sickness record, then directly writes Notes onto the resulting
    /// (Pending) return-to-work review via the DbContext — the domain has no public API for
    /// setting review notes outside CompleteReturnToWorkReview, which this test deliberately
    /// avoids to keep the review in a state reachable by a plain GET.
    /// </summary>
    private async Task<Guid> CreateReturnToWorkReviewWithNotesAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var categoryId = await CreateCategoryAsync(hrClient, companyId);
        var startDate = new DateOnly(2026, 6, 1);
        var endDate = new DateOnly(2026, 6, 3);

        var createResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = startDate.ToString("yyyy-MM-dd"),
                startDayPart = "FullDay"
            });
        createResponse.EnsureSuccessStatusCode();
        var recordId = (await createResponse.Content.ReadFromJsonAsync<SicknessRecordPayload>())!.Id;

        var closeResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = endDate.ToString("yyyy-MM-dd"),
                endDayPart = "FullDay"
            });
        closeResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Sickness.Persistence.SicknessDbContext>();
        var review = await db.ReturnToWorkReviews.SingleAsync(r =>
            r.CompanyId == companyId && r.EmployeeId == employeeId);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE sickness.return_to_work_reviews SET notes = {0} WHERE id = {1}",
            "Confidential medical detail", review.Id);

        return review.Id;
    }

    private async Task RunFitNoteRequestJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<FitNoteRequestJob>();
        await job.ExecuteAsync();
    }

    private async Task RunReturnToWorkReminderJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReturnToWorkReminderJob>();
        await job.ExecuteAsync();
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record CategoryPayload(Guid Id);
    private sealed record SicknessRecordPayload(Guid Id);

    private sealed record MissingFitNotesPayload(IReadOnlyList<MissingFitNoteItemPayload> Items);
    private sealed record MissingFitNoteItemPayload(Guid RequestId, Guid EmployeeId, Guid SicknessRecordId, string DueDate, string Status);

    private sealed record OverdueReviewsPayload(IReadOnlyList<OverdueReviewItemPayload> Items);
    private sealed record OverdueReviewItemPayload(Guid ReviewId, Guid EmployeeId, Guid SicknessRecordId, string DueDate, Guid? TaskId);

    private sealed record ReviewPayload(
        Guid Id,
        Guid CompanyId,
        Guid SicknessRecordId,
        Guid EmployeeId,
        string DueDate,
        string Status,
        DateTimeOffset? CompletedAt,
        string? Notes);
}
