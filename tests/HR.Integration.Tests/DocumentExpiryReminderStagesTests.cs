using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// DOC-03: covers the three independent upcoming-expiry reminder stages (90/30/7 days) and the
/// reminder-state reset performed by EmployeeDocument.UpdateExpiryDate, on top of the pre-existing
/// coverage in DocumentExpiryTasksEndToEndTests.
/// </summary>
[Collection("Integration")]
public class DocumentExpiryReminderStagesTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ExpiryStagesAdmin = Guid.Parse("11100004-0000-0000-0000-000000000001");
    private static readonly DateOnly Today          = DateOnly.FromDateTime(DateTime.UtcNow);

    public DocumentExpiryReminderStagesTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryStagesAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryStagesAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Document_Seven_Days_From_Expiry_Fires_All_Three_Reminder_Stages_On_First_Evaluation()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var docTypeId = await CreateDocTypeAsync(client, companyId);
        await UploadDocAsync(client, companyId, docTypeId, employeeId, Today.AddDays(7));

        var notifResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        notifResp.EnsureSuccessStatusCode();
        var payload = await notifResp.Content.ReadFromJsonAsync<NotifPayload>();

        Assert.Equal(1, payload!.Reminder90Count);
        Assert.Equal(1, payload.Reminder30Count);
        Assert.Equal(1, payload.Reminder7Count);
        Assert.Equal(3, payload.ExpiringSoonCount);

        var tasksResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        var tasks = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        var documentTasks = tasks!.Items.Where(t => t.Source == "Document").ToList();
        Assert.Equal(3, documentTasks.Count);
    }

    [Fact]
    public async Task Updating_Expiry_Date_Resets_Reminder_State_And_Reevaluates_Against_New_Date()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var docTypeId = await CreateDocTypeAsync(client, companyId);
        // Start out already within the 90-day window so the first run fires the 90-day stage.
        var employeeDocumentId = await UploadDocAsync(client, companyId, docTypeId, employeeId, Today.AddDays(60));

        var firstResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        firstResp.EnsureSuccessStatusCode();
        var firstPayload = await firstResp.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(1, firstPayload!.Reminder90Count);

        // No accessible API to edit an EmployeeDocument's expiry date exists yet (DOC-03 adds
        // UpdateExpiryDate to the domain for forward-safety only — see EmployeeDocument.cs), so we
        // exercise it directly against the DbContext, mirroring how other integration tests reach
        // into the database for setup that has no corresponding endpoint (see
        // AdjustLeaveBalanceEndpointTests.CreateLeaveTypeAsync for the same pattern).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var entity = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == employeeDocumentId);

            // Push the expiry date far enough out that none of the three stages are due yet.
            entity.UpdateExpiryDate(Today.AddDays(120), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Re-running immediately after the reset must not re-fire the 90-day stage (or any other)
        // for the new, much later expiry date.
        var secondResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        secondResp.EnsureSuccessStatusCode();
        var secondPayload = await secondResp.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(0, secondPayload!.Reminder90Count);
        Assert.Equal(0, secondPayload.Reminder30Count);
        Assert.Equal(0, secondPayload.Reminder7Count);
        Assert.Equal(0, secondPayload.ExpiringSoonCount);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var entity = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == employeeDocumentId);
            Assert.Null(entity.ExpiryReminder90SentAt);
            Assert.Null(entity.ExpiryReminder30SentAt);
            Assert.Null(entity.ExpiryReminder7SentAt);
            Assert.Null(entity.ExpiringSoonNotifiedAt);
            Assert.Null(entity.ExpiredNotifiedAt);
            Assert.Equal(Today.AddDays(120), entity.ExpiryDate);
        }

        // Now push the expiry date back into the 90-day window and confirm it correctly fires
        // again against the new date, proving the reset genuinely restarted the schedule.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var entity = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == employeeDocumentId);
            entity.UpdateExpiryDate(Today.AddDays(90), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var thirdResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        thirdResp.EnsureSuccessStatusCode();
        var thirdPayload = await thirdResp.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(1, thirdPayload!.Reminder90Count);
    }

    [Fact]
    public async Task Post_ExpiryNotifications_Returns_Unauthorized_For_Anonymous_Request()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ExpiryStagesAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ExpiryStagesAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateDocTypeAsync(HttpClient client, Guid companyId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "Contract", allowEmployeeUpload = false });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<DocTypePayload>();
        return payload!.Id;
    }

    private async Task<Guid> UploadDocAsync(
        HttpClient client, Guid companyId, Guid docTypeId, Guid employeeId, DateOnly expiryDate)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46;

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Expiry Stages Test Doc"),          "Title");
        content.Add(new StringContent(docTypeId.ToString()),              "DocumentTypeId");
        content.Add(new StringContent(expiryDate.ToString("yyyy-MM-dd")), "ExpiryDate");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        var resp = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/documents", content);
        resp.EnsureSuccessStatusCode();

        // Look up the resulting EmployeeDocument id directly, since the upload response does not
        // itself return the EmployeeDocument's own id (the join id between Document and Employee).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var employeeDocument = await db.EmployeeDocuments
            .Where(ed => ed.CompanyId == companyId && ed.EmployeeId == employeeId)
            .OrderByDescending(ed => ed.CreatedAt)
            .FirstAsync();
        return employeeDocument.Id;
    }

    private sealed record NotifPayload(
        int ExpiringSoonCount,
        int ExpiredCount,
        int Reminder90Count,
        int Reminder30Count,
        int Reminder7Count);
    private sealed record DocTypePayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Title, string Source, string Priority, Guid? AssignedEmployeeId);
}
