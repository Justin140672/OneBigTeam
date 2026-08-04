using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies CompleteSharedCompanyDocumentReview end-to-end: the shared-document:manage policy,
/// that the reviewer identity is always resolved from the caller's own claims (never accepted
/// from the request body), tenant isolation, and validation of ReviewNotes.
/// </summary>
[Collection("Integration")]
public class CompleteSharedCompanyDocumentReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CompleteSharedCompanyDocumentReviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteReview_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/complete-review",
            new { ReviewNotes = "Reviewed." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Succeeds_For_HrAdministrator_And_Updates_Fields()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "Reviewed against the latest legislation." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CompleteReviewPayload>();
        Assert.Equal(doc.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(userId, payload.LastReviewedByEmployeeId);
        Assert.Equal("Reviewed against the latest legislation.", payload.LastReviewNotes);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), payload.LastReviewedAt);
        Assert.NotNull(payload.ReviewDate);
        Assert.True(payload.ReviewDate > DateOnly.FromDateTime(DateTime.UtcNow));

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetDetailPayload>();
        Assert.Equal(payload.LastReviewedAt,  detail!.LastReviewedAt);
        Assert.Equal(userId,                  detail.LastReviewedByEmployeeId);
        Assert.Equal("Reviewed against the latest legislation.", detail.LastReviewNotes);
        Assert.Equal(payload.ReviewDate,       detail.ReviewDate);

        var reviewHistoryEntry = Assert.Single(detail.ReviewHistory);
        Assert.Equal(payload.LastReviewedAt, reviewHistoryEntry.ReviewDate);
        Assert.Equal(userId, reviewHistoryEntry.ReviewedByEmployeeId);
        Assert.Equal("Reviewed against the latest legislation.", reviewHistoryEntry.ReviewNotes);
        Assert.Equal(new DateOnly(2020, 1, 1), reviewHistoryEntry.PreviousReviewDate);
    }

    [Fact]
    public async Task CompleteReview_AfterUploadingRenewedVersion_StillPersists_LastReviewedFields()
    {
        // Regression check for a reported E2E failure (CompleteReview_WithRenewedFile_...): the
        // Complete Review dialog, when a renewed file is attached, chains two real HTTP calls —
        // upload-version first, then complete-review — mirroring that exact sequence here via the
        // real API/DB (not fakes) to determine whether the "Last reviewed by" fields genuinely
        // fail to persist server-side, or whether the E2E failure is purely a UI-side timing issue.
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Renewal Regression Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        using var versionForm = new MultipartFormDataContent();
        versionForm.Add(new StringContent("Renewed via document review. Reviewed and reissued."), "VersionNote");
        versionForm.Add(new StringContent("false"), "RequiresReacknowledgement");
        var versionFileContent = new ByteArrayContent(PdfBytes());
        versionFileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        versionForm.Add(versionFileContent, "File", "renewed.pdf");

        var uploadVersionResponse = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions", versionForm);
        Assert.Equal(HttpStatusCode.Created, uploadVersionResponse.StatusCode);

        var completeReviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "Reviewed and reissued." });
        Assert.Equal(HttpStatusCode.OK, completeReviewResponse.StatusCode);
        var completePayload = await completeReviewResponse.Content.ReadFromJsonAsync<CompleteReviewPayload>();
        Assert.Equal(userId, completePayload!.LastReviewedByEmployeeId);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetDetailPayload>();

        Assert.NotNull(detail!.LastReviewedAt);
        Assert.Equal(userId, detail.LastReviewedByEmployeeId);
        Assert.Equal(2, detail.VersionHistory.Count);
    }

    [Fact]
    public async Task CompleteReview_Closes_Open_Review_Task_Created_By_DetectDocumentsDueForReviewJob()
    {
        // Proves the fix for "Prevent Duplicate Review Tasks": completing a review must close
        // the open Review task DetectDocumentsDueForReviewJob created for this document
        // (sourceEntityId = document.Id, TaskSource.Document, TaskActionType.Review) — otherwise
        // the job's openReviewTaskIds check keeps skipping the document forever once its next
        // ReviewDate comes due.
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title: $"Review due: {doc!.Title}",
            source: TaskSource.Document,
            actionType: TaskActionType.Review,
            sourceEntityId: doc.Id,
            status: TaskItemStatus.Open);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "Reviewed against the latest legislation." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = await tasksDb.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId);

        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(userId, task.CompletedBy);
    }

    [Fact]
    public async Task CompleteReview_Persists_Audit_Record()
    {
        // shared_company_document.review_completed's EmployeeId is always null (see
        // SharedCompanyDocumentReviewCompletedAuditEvent), so — just like
        // AuditHistoryIntegrationTests.UpdateCompanySettings_Persists_Audit_Record — it can never
        // appear via the employee-scoped GetEmployeeAuditHistory endpoint. Read AuditDbContext
        // directly via the test host's DI container instead, proving the audit event raised by the
        // handler actually lands in the audit table end-to-end.
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "Reviewed against the latest legislation." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "shared_company_document.review_completed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SharedCompanyDocument", auditRecord!.EntityType);
        Assert.Equal(doc.Id, auditRecord.EntityId);
        Assert.Equal(userId, auditRecord.ActorUserId);
        Assert.Contains("Reviewed against the latest legislation.", auditRecord.AfterJson);
    }

    [Fact]
    public async Task CompleteReview_Twice_Results_In_Two_ReviewHistory_Entries()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "First review." });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "Second review." });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetDetailPayload>();

        // Both reviews complete on the same real-world day, so ReviewDate ties between the two
        // rows and the newest-first ordering isn't guaranteed between them — just assert both
        // are present rather than asserting a specific order.
        Assert.Equal(2, detail!.ReviewHistory.Count);
        Assert.Contains(detail.ReviewHistory, h => h.ReviewNotes == "First review.");
        Assert.Contains(detail.ReviewHistory, h => h.ReviewNotes == "Second review.");
    }

    [Fact]
    public async Task CompleteReview_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId        = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        var userId           = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var uploadClient = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(uploadClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(uploadClient, companyId, categoryId);

        // Same user, but the tenant header on this client claims a different company than the
        // one in the route — must be rejected before the document lookup even runs.
        using var mismatchedClient = await ClientAs(differentCompany, userId);
        var response = await mismatchedClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "Reviewed." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        // Manager holds no company-wide role that satisfies shared-document:manage, so the
        // policy itself should reject the call before the tenant-claim check even matters.
        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "Reviewed." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/complete-review",
            new { ReviewNotes = "Reviewed." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "Reviewed." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Returns_Validation_Error_When_ReviewNotes_Missing()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReview_Returns_Validation_Error_When_ReviewNotes_Is_Whitespace_Only()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/complete-review",
            new { ReviewNotes = "   " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Renewal-via-review journey ──────────────────────────────────────────────
    // These prove the "Keep Previous Versions" acceptance criteria hold for the *combined*
    // flow the UI actually drives: upload a new version via the existing, unmodified
    // UploadSharedCompanyDocumentVersion endpoint, then complete the review via this
    // endpoint. Each endpoint already has full isolated coverage elsewhere; this file only
    // proves the two-call sequence composes correctly.

    [Fact]
    public async Task RenewalViaReview_Retains_Both_Versions_And_Updates_Review_Fields()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        // Step 1: renew the file — the same call the UI makes before completing the review.
        var versionResponse = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions",
            BuildVersionUpload("Updated for 2026 legislation.", requiresReacknowledgement: false));
        Assert.Equal(HttpStatusCode.Created, versionResponse.StatusCode);
        var versionPayload = await versionResponse.Content.ReadFromJsonAsync<VersionUploadPayload>();
        Assert.Equal(2, versionPayload!.VersionNumber);

        // Step 2: complete the review — the same call the UI makes immediately afterwards.
        var reviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "Renewed with updated 2026 legislation." });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetDetailPayload>();

        // Both the old and new file remain in the version history, distinctly numbered.
        Assert.Equal(2, detail!.VersionHistory.Count);
        var v1 = Assert.Single(detail.VersionHistory, v => v.VersionNumber == 1);
        var v2 = Assert.Single(detail.VersionHistory, v => v.VersionNumber == 2);
        Assert.Equal("Superseded", v1.PublicationStatus);
        Assert.Equal(detail.Status, v2.PublicationStatus);

        // The review fields reflect the completed review, exactly as in the non-renewal case.
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), detail.LastReviewedAt);
        Assert.Equal(userId, detail.LastReviewedByEmployeeId);
        Assert.Equal("Renewed with updated 2026 legislation.", detail.LastReviewNotes);
        Assert.NotNull(detail.ReviewDate);
        Assert.True(detail.ReviewDate > DateOnly.FromDateTime(DateTime.UtcNow));

        var reviewHistoryEntry = Assert.Single(detail.ReviewHistory);
        Assert.Equal(detail.LastReviewedAt, reviewHistoryEntry.ReviewDate);
        Assert.Equal(userId, reviewHistoryEntry.ReviewedByEmployeeId);
        Assert.Equal("Renewed with updated 2026 legislation.", reviewHistoryEntry.ReviewNotes);
        Assert.Equal(new DateOnly(2020, 1, 1), reviewHistoryEntry.PreviousReviewDate);
    }

    [Fact]
    public async Task RenewalViaReview_Twice_Retains_All_Three_Versions()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Yearly", reviewDate: new DateOnly(2020, 1, 1));

        // First renewal cycle: v1 -> v2, then review.
        var firstVersionResponse = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions",
            BuildVersionUpload("First renewal.", requiresReacknowledgement: false));
        Assert.Equal(HttpStatusCode.Created, firstVersionResponse.StatusCode);
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "First review." });

        // Second renewal cycle: v2 -> v3, then review again — proves this isn't a one-shot
        // special case.
        var secondVersionResponse = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/versions",
            BuildVersionUpload("Second renewal.", requiresReacknowledgement: false));
        Assert.Equal(HttpStatusCode.Created, secondVersionResponse.StatusCode);
        var secondVersionPayload = await secondVersionResponse.Content.ReadFromJsonAsync<VersionUploadPayload>();
        Assert.Equal(3, secondVersionPayload!.VersionNumber);

        var secondReviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/complete-review",
            new { ReviewNotes = "Second review." });
        Assert.Equal(HttpStatusCode.OK, secondReviewResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetDetailPayload>();

        Assert.Equal(3, detail!.VersionHistory.Count);
        Assert.Equal([1, 2, 3], detail.VersionHistory.Select(v => v.VersionNumber).OrderBy(n => n));
        Assert.Equal("Superseded", Assert.Single(detail.VersionHistory, v => v.VersionNumber == 1).PublicationStatus);
        Assert.Equal("Superseded", Assert.Single(detail.VersionHistory, v => v.VersionNumber == 2).PublicationStatus);
        Assert.Equal(detail.Status, Assert.Single(detail.VersionHistory, v => v.VersionNumber == 3).PublicationStatus);

        Assert.Equal(2, detail.ReviewHistory.Count);
        Assert.Contains(detail.ReviewHistory, h => h.ReviewNotes == "First review.");
        Assert.Contains(detail.ReviewHistory, h => h.ReviewNotes == "Second review.");
    }

    private static MultipartFormDataContent BuildVersionUpload(
        string versionNote = "Updated section 3", bool requiresReacknowledgement = false)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(versionNote), "VersionNote");
        form.Add(new StringContent(requiresReacknowledgement.ToString()), "RequiresReacknowledgement");

        var fileContent = new ByteArrayContent(PdfBytes());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy-v2.pdf");
        return form;
    }

    private sealed record VersionUploadPayload(Guid Id, Guid CompanyId, int VersionNumber, string FileName, string VersionNote);

    private sealed record CompleteReviewPayload(
        Guid Id, Guid CompanyId, DateOnly? ReviewDate, DateOnly LastReviewedAt,
        Guid LastReviewedByEmployeeId, string? LastReviewNotes);

    private sealed record GetDetailPayload(
        string Status, DateOnly? ReviewDate, DateOnly? LastReviewedAt, Guid? LastReviewedByEmployeeId,
        string? LastReviewedByName, string? LastReviewNotes,
        IReadOnlyList<ReviewHistoryItemPayload> ReviewHistory,
        IReadOnlyList<VersionHistoryItemPayload> VersionHistory);

    private sealed record ReviewHistoryItemPayload(
        DateOnly ReviewDate, Guid ReviewedByEmployeeId, string ReviewedByName,
        string? ReviewNotes, DateOnly? PreviousReviewDate);

    private sealed record VersionHistoryItemPayload(
        int VersionNumber, string FileName, long FileSize, string UploadedByName,
        DateTimeOffset UploadedAt, string? VersionNote, bool RequiresAcknowledgement,
        DateOnly? EffectiveDate, string PublicationStatus);

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<(DocumentPayload? Payload, HttpResponseMessage Response)> UploadAsync(
        HttpClient client, Guid companyId, Guid categoryId, string title = "Test Document",
        string? reviewFrequency = null, DateOnly? reviewDate = null)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents",
            BuildUpload(title, categoryId, reviewFrequency, reviewDate));
        DocumentPayload? payload = null;
        if (response.IsSuccessStatusCode)
            payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();
        return (payload, response);
    }

    private static MultipartFormDataContent BuildUpload(
        string title = "Test Document", Guid? categoryId = null,
        string? reviewFrequency = null, DateOnly? reviewDate = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent((categoryId ?? Guid.NewGuid()).ToString()), "CategoryId");
        if (reviewFrequency is not null)
            form.Add(new StringContent(reviewFrequency), "ReviewFrequency");
        if (reviewDate is not null)
            form.Add(new StringContent(reviewDate.Value.ToString("yyyy-MM-dd")), "ReviewDate");

        var fileContent = new ByteArrayContent(PdfBytes());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy.pdf");
        return form;
    }

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] PdfBytes()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Role-agnostic sync only — every caller of this helper already granted the specific
        // role(s) it wants to test beforehand via AssignRoleAsync. Hardcoding a role here (this
        // used to always grant SystemRoles.HrAdministrator) additionally granted it to every
        // caller regardless of intent, which used to be harmless only because tenant resolution
        // didn't actually key off UserProfile.CompanyId yet — now that it does, an unconditional
        // extra role grant here changes real authorization outcomes.
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
}
