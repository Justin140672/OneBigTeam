using System.Net;
using System.Net.Http.Headers;
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
public class UploadRequestedDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("cc000001-0000-0000-0000-000000000001");

    public UploadRequestedDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Upload_Fulfils_DocumentRequest_And_Creates_EmployeeDocument()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = await EmployeeClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}/upload",
            BuildPdfUpload("My Passport"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var req = await db.DocumentRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(DocumentRequestStatus.Uploaded, req.Status);
        Assert.NotNull(req.CompletedAt);

        Assert.Single(await db.EmployeeDocuments.Where(ed => ed.EmployeeId == employeeId).ToListAsync());
    }

    [Fact]
    public async Task Upload_Completes_The_Associated_Upload_Task()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = await EmployeeClient(companyId, employeeId);

        await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}/upload",
            BuildPdfUpload("My Passport"));

        using var scope = _factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var task = await tasks.TaskItems
            .Where(t => t.SourceEntityId == requestId)
            .SingleOrDefaultAsync();

        Assert.NotNull(task);
        Assert.Equal("Completed", task.Status.ToString());
    }

    [Fact]
    public async Task Upload_Returns_NotFound_For_Unknown_Request()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{Guid.NewGuid()}/upload",
            BuildPdfUpload("Passport"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Conflict_When_Request_Already_Uploaded()
    {
        var (companyId, employeeId, requestId) = await SetupAsync();
        using var client = await EmployeeClient(companyId, employeeId);
        var url = $"/api/companies/{companyId}/employees/{employeeId}/document-requests/{requestId}/upload";

        await client.PostAsync(url, BuildPdfUpload("First Upload"));
        var second = await client.PostAsync(url, BuildPdfUpload("Second Upload"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Forbidden_When_Employee_Uploads_Against_Another_Employees_Request()
    {
        var (companyId, ownerEmployeeId, requestId) = await SetupAsync();
        var otherEmployeeId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId, otherEmployeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{ownerEmployeeId}/document-requests/{requestId}/upload",
            BuildPdfUpload("Passport"));

        // Non-manager uploading to a different employee's route → Forbidden at the endpoint level
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/document-requests/{Guid.NewGuid()}/upload",
            BuildPdfUpload("Passport"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid RequestId)> SetupAsync()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

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

    private async Task<HttpClient> EmployeeClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(string title)
    {
        var magic   = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var content = new byte[magic.Length + 1020];
        magic.CopyTo(content, 0);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "passport.pdf");

        return form;
    }
}
