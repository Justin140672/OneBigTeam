using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// SICK-03: the canonical way a return-to-work review is completed. Mirrors
/// SicknessResourceAuthorizationTests' seeding conventions for the "sickness:review"
/// policy/authorizer pair.
/// </summary>
[Collection("Integration")]
public class CompleteReturnToWorkReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CompleteReturnToWorkReviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/return-to-work-reviews/{Guid.NewGuid()}/complete",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_FitOutcome_Returns_Ok_With_Outcome()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, employee);

        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "Fit",
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = "Employee confirmed fit to return."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewCompletionPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Completed", payload!.Status);
        Assert.Equal("Fit", payload.Outcome);
        Assert.False(payload.AdjustmentsRequired);
        Assert.Null(payload.AdjustmentDetails);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_NotFitOutcome_Reopens_SicknessRecord()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var (recordId, reviewId) = await CreateReturnToWorkReviewWithRecordAsync(hrClient, companyId, employee);

        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "NotFit",
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewCompletionPayload>();
        Assert.NotNull(payload);
        Assert.Equal("NotFit", payload!.Outcome);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Sickness.Persistence.SicknessDbContext>();
        var record = await db.SicknessRecords.AsNoTracking().SingleAsync(r => r.Id == recordId);
        Assert.Equal("Active", record.Status.ToString());
        Assert.Null(record.EndDate);
        Assert.Null(record.TotalDays);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_FitWithAdjustments_WithDetails_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, employee);

        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "FitWithAdjustments",
            adjustmentsRequired = true,
            adjustmentDetails = "Phased return, reduced hours for two weeks.",
            managerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewCompletionPayload>();
        Assert.NotNull(payload);
        Assert.Equal("FitWithAdjustments", payload!.Outcome);
        Assert.True(payload.AdjustmentsRequired);
        Assert.Equal("Phased return, reduced hours for two weeks.", payload.AdjustmentDetails);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_Returns_NotFound_For_NonExistent_Review()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);

        var reviewId = Guid.NewGuid();
        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "Fit",
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_Returns_NotFound_For_Review_Outside_Reporting_Hierarchy()
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

        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, report);

        using var unrelatedManagerClient = await ClientFor(companyId, unrelatedManager);
        var response = await CompleteAsync(unrelatedManagerClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "Fit",
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = (string?)null
        });

        // Same "unrelated review looks like no such review" pattern as GetReturnToWorkReview.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_Returns_UnprocessableEntity_When_AdjustmentsRequired_Without_Details()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, employee);

        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "FitWithAdjustments",
            adjustmentsRequired = true,
            adjustmentDetails = (string?)null,
            managerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_Returns_UnprocessableEntity_For_Invalid_Outcome()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, employee);

        var response = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = 99,
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_CompleteReturnToWorkReview_CalledTwice_Returns_Ok_Both_Times_With_Same_Outcome()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);
        var employee = await CreateEmployeeAsync(hrClient, companyId, reference);
        var reviewId = await CreateReturnToWorkReviewAsync(hrClient, companyId, employee);

        var body = new
        {
            companyId,
            reviewId,
            outcome = "FitWithAdjustments",
            adjustmentsRequired = true,
            adjustmentDetails = "Reduced hours for one week.",
            managerNotes = "First completion."
        };

        var firstResponse = await CompleteAsync(hrClient, companyId, reviewId, body);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<ReviewCompletionPayload>();

        // Second call with different (would-be) values — should be ignored, not overwrite.
        var secondResponse = await CompleteAsync(hrClient, companyId, reviewId, new
        {
            companyId,
            reviewId,
            outcome = "NotFit",
            adjustmentsRequired = false,
            adjustmentDetails = (string?)null,
            managerNotes = "Second completion attempt."
        });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<ReviewCompletionPayload>();

        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.Equal(firstPayload!.Outcome, secondPayload!.Outcome);
        Assert.Equal(firstPayload.AdjustmentsRequired, secondPayload.AdjustmentsRequired);
        Assert.Equal(firstPayload.AdjustmentDetails, secondPayload.AdjustmentDetails);
        Assert.Equal("FitWithAdjustments", secondPayload.Outcome);
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

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
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
                $"rtwreview.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();

        await TestRoleSeeder.AssignRoleAsync(_factory, payload!.Id, SystemRoles.Employee, companyId);

        return payload.Id;
    }

    private async Task AssignRoleAsync(Guid userId, Guid companyId, Guid roleId) =>
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);

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
    /// Creates and closes a sickness record whose closure raises a return-to-work review
    /// (ReturnToWorkRequiredAfterDays defaults to 1, so any closed record with >=1 total day
    /// qualifies), returning only the review id.
    /// </summary>
    private async Task<Guid> CreateReturnToWorkReviewAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var (_, reviewId) = await CreateReturnToWorkReviewWithRecordAsync(hrClient, companyId, employeeId);
        return reviewId;
    }

    private async Task<(Guid RecordId, Guid ReviewId)> CreateReturnToWorkReviewWithRecordAsync(
        HttpClient hrClient, Guid companyId, Guid employeeId)
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
        var review = await db.ReturnToWorkReviews.AsNoTracking().SingleAsync(r =>
            r.CompanyId == companyId && r.EmployeeId == employeeId);

        return (recordId, review.Id);
    }

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid companyId, Guid reviewId, object body) =>
        await client.PostAsJsonAsync($"/api/companies/{companyId}/return-to-work-reviews/{reviewId}/complete", body);

    private sealed record EmployeePayload(Guid Id);
    private sealed record CategoryPayload(Guid Id);
    private sealed record SicknessRecordPayload(Guid Id);

    private sealed record ReviewCompletionPayload(
        Guid Id,
        Guid CompanyId,
        Guid SicknessRecordId,
        Guid EmployeeId,
        string Status,
        string Outcome,
        bool AdjustmentsRequired,
        string? AdjustmentDetails,
        Guid ReviewedBy,
        DateTimeOffset CompletedAt);
}
