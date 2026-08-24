using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// SICK-04: end-to-end coverage of GET /api/companies/{companyId}/attendance-alerts — the
/// HR-administrator-vs-manager reduced view, reporting-hierarchy scoping (mirrors
/// SicknessResourceAuthorizationTests' pattern for the other "sickness:review" endpoints),
/// anonymous 401, and cross-company isolation. Alerts are seeded by creating four closed sickness
/// records (>= FrequentAbsenceCountThreshold, default 4) for an employee within the default
/// 365-day rolling window and then running AttendanceAlertEvaluationJob directly.
/// </summary>
[Collection("Integration")]
public class ListAttendanceAlertsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ListAttendanceAlertsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_AttendanceAlerts_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/attendance-alerts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AttendanceAlerts_HrAdministrator_Sees_FullDetail()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        await SeedFrequentAbsencesAsync(hrClient, companyId, employee);
        await CompleteAllReturnToWorkReviewsAsync(employee);
        await RunAttendanceAlertEvaluationJobAsync();

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/attendance-alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AttendanceAlertsPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items, i => i.EmployeeId == employee);
        Assert.Equal("FrequentAbsences", item.Rule);
        Assert.Equal(4, item.OccurrenceCount);
        Assert.NotNull(item.EvidencePeriodStart);
        Assert.NotNull(item.EvidencePeriodEnd);
        Assert.NotNull(item.Description);
    }

    [Fact]
    public async Task Get_AttendanceAlerts_Manager_Sees_ReducedView_WithoutDatesOrDescription()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        await SeedFrequentAbsencesAsync(hrClient, companyId, report);
        await CompleteAllReturnToWorkReviewsAsync(report);
        await RunAttendanceAlertEvaluationJobAsync();

        using var managerClient = await ClientFor(companyId, manager);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/attendance-alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AttendanceAlertsPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items, i => i.EmployeeId == report);
        Assert.Equal("FrequentAbsences", item.Rule);
        Assert.Equal(4, item.OccurrenceCount);
        // SICK-04 manager-reduced-view decision: dates and description are withheld from managers.
        Assert.Null(item.EvidencePeriodStart);
        Assert.Null(item.EvidencePeriodEnd);
        Assert.Null(item.Description);
    }

    [Fact]
    public async Task Get_AttendanceAlerts_Hidden_From_Unrelated_Manager()
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

        await SeedFrequentAbsencesAsync(hrClient, companyId, report);
        await CompleteAllReturnToWorkReviewsAsync(report);
        await RunAttendanceAlertEvaluationJobAsync();

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedManagerClient.GetAsync($"/api/companies/{companyId}/attendance-alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AttendanceAlertsPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task Get_AttendanceAlerts_PlainEmployee_Gets_Forbidden()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);

        using var employeeClient = await ClientFor(companyId, employee);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/attendance-alerts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AttendanceAlerts_Never_Leaks_AlertsFromOtherCompany()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        await SeedFrequentAbsencesAsync(hrClient, companyId, employee);
        await CompleteAllReturnToWorkReviewsAsync(employee);
        await RunAttendanceAlertEvaluationJobAsync();

        using var otherHrClient = await HrAdminClientAsync(otherCompanyId);
        var response = await otherHrClient.GetAsync($"/api/companies/{otherCompanyId}/attendance-alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AttendanceAlertsPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == employee);
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
                $"attendancealert.{Guid.NewGuid():N}@example.com"));
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
    /// Creates four closed sickness records for the employee, each starting on a distinct month
    /// within the last year, so the default FrequentAbsences rule (threshold 4, 365-day window)
    /// fires deterministically once AttendanceAlertEvaluationJob runs, irrespective of "today"'s
    /// actual calendar date at test-run time.
    /// </summary>
    private async Task SeedFrequentAbsencesAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var categoryId = await CreateCategoryAsync(hrClient, companyId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var offsets = new[] { 30, 60, 90, 120 };
        foreach (var offsetDays in offsets)
        {
            var startDate = today.AddDays(-offsetDays);
            var endDate = startDate.AddDays(1);

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
        }
    }

    private async Task RunAttendanceAlertEvaluationJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<AttendanceAlertEvaluationJob>();
        await job.ExecuteAsync();
    }

    /// <summary>
    /// Closing a sickness record raises a return-to-work review (SICK-03, default
    /// ReturnToWorkRequiredAfterDays = 1), which — left Pending with a long-past due date —
    /// would itself trip MissingReturnToWorkReview and pollute these FrequentAbsences-focused
    /// assertions. Completing every review for the employee isolates the scenario to the rule
    /// under test, mirroring how AttendanceAlertEvaluationJobTests attaches completed reviews for
    /// the same reason.
    /// </summary>
    private async Task CompleteAllReturnToWorkReviewsAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        var now = DateTimeOffset.UtcNow;

        var reviews = await db.ReturnToWorkReviews
            .Where(r => r.EmployeeId == employeeId && r.Status != ReturnToWorkReviewStatus.Completed)
            .ToListAsync();

        foreach (var review in reviews)
        {
            review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, adjustmentsRequired: false, adjustmentDetails: null, notes: null, now);
        }

        // CloseSicknessRecordHandler only raises a review when its own (working-day) TotalDays
        // meets ReturnToWorkRequiredAfterDays — a short spell landing on a weekend may close
        // without ever getting a review row at all. MissingReturnToWorkReview's defensive
        // catch-all measures duration in calendar days instead (see
        // AttendanceAlertEvaluationService), so such a record would still trip it. Backfill a
        // completed review for any closed record that doesn't have one, so this test's seeded
        // data is guaranteed to exercise only FrequentAbsences regardless of which weekday the
        // seeded absences happen to fall on.
        var closedRecordIds = await db.SicknessRecords
            .Where(r => r.EmployeeId == employeeId && r.Status == SicknessStatus.Closed)
            .Select(r => r.Id)
            .ToListAsync();

        var reviewedRecordIds = await db.ReturnToWorkReviews
            .Where(r => r.EmployeeId == employeeId)
            .Select(r => r.SicknessRecordId)
            .ToListAsync();

        var unreviewedRecordIds = closedRecordIds.Except(reviewedRecordIds).ToList();
        foreach (var recordId in unreviewedRecordIds)
        {
            var backfilled = ReturnToWorkReview.Create(Guid.NewGuid(), (await db.SicknessRecords.SingleAsync(r => r.Id == recordId)).CompanyId, recordId, employeeId, DateOnly.FromDateTime(now.UtcDateTime), now);
            backfilled.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, adjustmentsRequired: false, adjustmentDetails: null, notes: null, now);
            db.ReturnToWorkReviews.Add(backfilled);
        }

        if (reviews.Count > 0 || unreviewedRecordIds.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record CategoryPayload(Guid Id);
    private sealed record SicknessRecordPayload(Guid Id);

    private sealed record AttendanceAlertsPayload(IReadOnlyList<AttendanceAlertItemPayload> Items);

    private sealed record AttendanceAlertItemPayload(
        Guid AlertId,
        Guid EmployeeId,
        string Rule,
        int OccurrenceCount,
        string? EvidencePeriodStart,
        string? EvidencePeriodEnd,
        string? Description,
        DateTimeOffset CreatedAt);
}
