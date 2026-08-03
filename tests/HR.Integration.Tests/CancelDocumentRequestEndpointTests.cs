using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CancelDocumentRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("cb000001-0000-0000-0000-000000000001");

    public CancelDocumentRequestEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_NoContent_When_Cancelled_Successfully()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Sets_DocumentRequest_Status_To_Cancelled()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var req = await db.DocumentRequests.SingleAsync(r => r.Id == requestId);

        Assert.Equal(DocumentRequestStatus.Cancelled, req.Status);
        Assert.NotNull(req.CompletedAt);
    }

    [Fact]
    public async Task Cancels_Associated_Upload_Task()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = ManagerClient(companyId);

        await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}");

        using var scope = _factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = await tasks.TaskItems.SingleAsync(t => t.SourceEntityId == requestId);

        Assert.Equal("Cancelled", task.Status.ToString());
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Request()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = ManagerClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Already_Uploaded()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();

        // Upload the document first
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var req = await db.DocumentRequests.SingleAsync(r => r.Id == requestId);
        req.MarkUploaded(employeeId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        using var client = ManagerClient(companyId);
        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/document-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/document-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Caller_Is_Not_A_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/document-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid RequestId)> SetupAsync()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            positionProfileRequiredDocumentId: null, dueDate: null,
            isMandatory: false, notes: null,
            requestedByEmployeeId: null, DateTimeOffset.UtcNow);
        db.DocumentRequests.Add(request);

        await db.SaveChangesAsync();

        var taskCreator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        await taskCreator.CreateAsync(
            companyId,
            createdBy:          AdminUser,
            title:              "Upload Passport",
            description:        "Please upload a copy of your Passport.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Document,
            actionType:         TaskActionType.Upload,
            dueDate:            null,
            assignedEmployeeId: employeeId,
            assignedUserId:     null,
            sourceEntityId:     request.Id,
            CancellationToken.None);

        return (companyId, employeeId, request.Id);
    }

    private HttpClient ManagerClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }
}
