using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class ListDocumentRequestsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("cd000001-0000-0000-0000-000000000001");

    public ListDocumentRequestsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client  = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/document-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Non_Manager_Requests_Another_Employee()
    {
        var companyId   = Guid.NewGuid();
        var employeeId  = Guid.NewGuid();
        var callerId    = Guid.NewGuid(); // different employee

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader,   callerId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_With_Empty_List_When_No_Requests()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var client = await EmployeeClient(companyId, employeeId);
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Requests_For_Employee()
    {
        var (companyId, employeeId, _, _) = await SetupAsync(count: 2);

        using var client = await EmployeeClient(companyId, employeeId);
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item =>
        {
            Assert.NotEqual(Guid.Empty,      item.Id);
            Assert.NotEmpty(item.DocumentTypeName);
            Assert.Equal("Requested", item.Status);
        });
    }

    [Fact]
    public async Task Returns_Uploaded_Request_With_Uploaded_Status()
    {
        var (companyId, employeeId, requestId, _) = await SetupAsync(count: 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var req = await db.DocumentRequests.FindAsync(requestId);
        req!.MarkUploaded(employeeId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        using var client = await EmployeeClient(companyId, employeeId);
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests");

        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.Single(payload!.Items);
        Assert.Equal("Uploaded", payload.Items[0].Status);
    }

    [Fact]
    public async Task Manager_Can_List_Another_Employees_Requests()
    {
        var (companyId, employeeId, _, _) = await SetupAsync(count: 1);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader,   AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/document-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.Single(payload!.Items);
    }

    [Fact]
    public async Task Does_Not_Return_Other_Employees_Requests()
    {
        var (companyId, employeeId, _, _) = await SetupAsync(count: 1);
        var otherEmployee = Guid.NewGuid();

        using var client = await EmployeeClient(companyId, otherEmployee);
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{otherEmployee}/document-requests");

        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.Empty(payload!.Items);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid FirstRequestId, IReadOnlyList<Guid> AllRequestIds)>
        SetupAsync(int count = 1)
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var requestIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var docType = DocumentType.Create(Guid.NewGuid(), companyId, $"Document Type {i + 1}", null, DateTimeOffset.UtcNow);
            db.DocumentTypes.Add(docType);
            await db.SaveChangesAsync();

            var request = DocumentRequest.Create(
                Guid.NewGuid(), companyId, employeeId, docType.Id,
                positionProfileRequiredDocumentId: null, dueDate: null,
                isMandatory: false, notes: null,
                requestedByEmployeeId: null, DateTimeOffset.UtcNow);
            db.DocumentRequests.Add(request);
            await db.SaveChangesAsync();

            requestIds.Add(request.Id);
        }

        return (companyId, employeeId, requestIds[0], requestIds);
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader,   employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return client;
    }

    private sealed record Payload(IReadOnlyList<RequestItem> Items);
    private sealed record RequestItem(Guid Id, string DocumentTypeName, DateOnly? DueDate, string Status);
}
