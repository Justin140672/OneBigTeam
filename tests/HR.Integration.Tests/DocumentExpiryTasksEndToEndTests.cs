using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that ProcessDocumentExpiryNotifications creates tasks that are
/// subsequently visible when querying an employee's task list.
/// </summary>
[Collection("Integration")]
public class DocumentExpiryTasksEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ExpiryE2EAdmin = Guid.Parse("11100003-0000-0000-0000-000000000001");
    private static readonly DateOnly Today       = DateOnly.FromDateTime(DateTime.UtcNow);

    public DocumentExpiryTasksEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryE2EAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryE2EAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Expiring_Document_Notification_Creates_High_Priority_Task_Visible_In_Employee_Tasks()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var docTypeId = await CreateDocTypeAsync(client, companyId);
        await UploadDocAsync(client, companyId, docTypeId, employeeId, Today.AddDays(10));

        var notifResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        notifResp.EnsureSuccessStatusCode();
        var notifPayload = await notifResp.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(1, notifPayload!.ExpiringSoonCount);

        var tasksResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        Assert.Equal(HttpStatusCode.OK, tasksResp.StatusCode);

        var tasks = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        var task  = Assert.Single(tasks!.Items.Where(t => t.Source == "Document"));
        Assert.Equal("High",     task.Priority);
        Assert.Equal(employeeId, task.AssignedEmployeeId);
        Assert.Contains("expiring soon", task.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_Document_Notification_Creates_Critical_Priority_Task_Visible_In_Employee_Tasks()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var docTypeId = await CreateDocTypeAsync(client, companyId);
        await UploadDocAsync(client, companyId, docTypeId, employeeId, Today.AddDays(-5));

        var notifResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        notifResp.EnsureSuccessStatusCode();
        var notifPayload = await notifResp.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(1, notifPayload!.ExpiredCount);

        var tasksResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        var tasks     = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        var task      = Assert.Single(tasks!.Items.Where(t => t.Source == "Document"));
        Assert.Equal("Critical",  task.Priority);
        Assert.Contains("expired", task.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expiry_Task_Notification_Is_Delivered_To_Employee()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var docTypeId = await CreateDocTypeAsync(client, companyId);
        await UploadDocAsync(client, companyId, docTypeId, employeeId, Today.AddDays(7));

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });

        using var employeeClient = await EmployeeClient(companyId, employeeId);
        var notifResp = await employeeClient.GetAsync(
            $"/api/companies/{companyId}/notifications/my");
        Assert.Equal(HttpStatusCode.OK, notifResp.StatusCode);

        var payload = await notifResp.Content.ReadFromJsonAsync<NotificationListPayload>();
        Assert.True(payload!.UnreadCount >= 1);
        Assert.Contains(payload.Items, n => !n.IsRead);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ExpiryE2EAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ExpiryE2EAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
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

    private static async Task UploadDocAsync(
        HttpClient client, Guid companyId, Guid docTypeId, Guid employeeId, DateOnly expiryDate)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46;

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Expiry Test Doc"),                     "Title");
        content.Add(new StringContent(docTypeId.ToString()),                  "DocumentTypeId");
        content.Add(new StringContent(expiryDate.ToString("yyyy-MM-dd")),     "ExpiryDate");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        var resp = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/documents", content);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record NotifPayload(int ExpiringSoonCount, int ExpiredCount);
    private sealed record DocTypePayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Title, string Source, string Priority, Guid? AssignedEmployeeId);
    private sealed record NotificationListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);
    private sealed record NotifItem(Guid Id, bool IsRead, string Type);
}
