using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RequestAdditionalEmployeeDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("da000001-0000-0000-0000-000000000001");

    public RequestAdditionalEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ── Happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_Created_With_DocumentRequest_When_Valid()
    {
        var (companyId, employeeId, docTypeId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests",
            new { documentTypeId = docTypeId, dueDate = "2026-09-01" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RequestPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.DocumentRequestId);
        Assert.Equal(companyId,  payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(docTypeId,  payload.DocumentTypeId);
        Assert.Equal("Requested", payload.Status);
    }

    [Fact]
    public async Task Creates_DocumentRequest_In_Database()
    {
        var (companyId, employeeId, docTypeId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests",
            new { documentTypeId = docTypeId });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var req = await db.DocumentRequests
            .SingleAsync(r => r.EmployeeId == employeeId && r.DocumentTypeId == docTypeId);

        Assert.Equal(companyId, req.CompanyId);
        Assert.Equal("Requested", req.Status.ToString());
        Assert.NotNull(req.RequestedByEmployeeId);
    }

    [Fact]
    public async Task Creates_Upload_Task_Assigned_To_Employee()
    {
        var (companyId, employeeId, docTypeId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests",
            new { documentTypeId = docTypeId });
        var payload = await response.Content.ReadFromJsonAsync<RequestPayload>();

        using var scope = _factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var task = await tasks.TaskItems
            .SingleAsync(t => t.SourceEntityId == payload!.DocumentRequestId);

        Assert.Equal(employeeId,  task.AssignedEmployeeId);
        Assert.Equal("Document",  task.Source.ToString());
        Assert.Equal("Upload",    task.ActionType.ToString());
    }

    [Fact]
    public async Task DueDate_Is_Stored_On_Request_And_Task()
    {
        var (companyId, employeeId, docTypeId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests",
            new { documentTypeId = docTypeId, dueDate = "2026-10-15" });

        using var scope = _factory.Services.CreateScope();
        var docsDb = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var req = await docsDb.DocumentRequests
            .SingleAsync(r => r.EmployeeId == employeeId && r.DocumentTypeId == docTypeId);
        Assert.Equal(new DateOnly(2026, 10, 15), req.DueDate);

        var task = await tasksDb.TaskItems
            .SingleAsync(t => t.SourceEntityId == req.Id);
        Assert.Equal(new DateOnly(2026, 10, 15), task.DueDate);
    }

    // ── Authorization ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/document-requests",
            new { documentTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/document-requests",
            new { documentTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Caller_Is_Not_A_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/document-requests",
            new { documentTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Error cases ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/document-requests",
            new { documentTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Request_Already_Exists_For_Same_Employee_And_Type()
    {
        var (companyId, employeeId, docTypeId) = await SetupAsync();
        using var client = ManagerClient(companyId);
        var url = $"/api/companies/{companyId}/employees/{employeeId}/document-requests";

        var first = await client.PostAsJsonAsync(url, new { documentTypeId = docTypeId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(url, new { documentTypeId = docTypeId });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid DocTypeId)> SetupAsync()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var client = ManagerClient(companyId);

        var typeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { companyId, name = $"Passport {Guid.NewGuid():N}" });
        typeResp.EnsureSuccessStatusCode();
        var docTypeId = (await typeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (companyId, employeeId, docTypeId);
    }

    private HttpClient ManagerClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record RequestPayload(
        Guid DocumentRequestId, Guid CompanyId, Guid EmployeeId,
        Guid DocumentTypeId, string DocumentTypeName, DateOnly? DueDate,
        string Status, DateTimeOffset CreatedAt);

    private sealed record IdPayload(Guid Id);
}
