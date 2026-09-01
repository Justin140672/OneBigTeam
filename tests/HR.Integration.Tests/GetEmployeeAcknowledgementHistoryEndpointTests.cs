using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers GET /api/companies/{companyId}/employees/{employeeId}/acknowledgement-history
/// (GetEmployeeAcknowledgementHistory): the role:employee endpoint policy, the endpoint's
/// tenant-match check, and its "self OR shared-document:manage" branch — an employee may read
/// their own history; anyone else needs the HR management policy.
/// </summary>
[Collection("Integration")]
public class GetEmployeeAcknowledgementHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetEmployeeAcknowledgementHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/acknowledgement-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Tenant_Does_Not_Match_Route_Company()
    {
        var companyId        = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        var userId           = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientAs(differentCompany, userId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{userId}/acknowledgement-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Empty_History_For_Employee_Viewing_Their_Own_Record()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{userId}/acknowledgement-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Manager_Viewing_Another_Employees_History()
    {
        var companyId  = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        // Employee role satisfies the endpoint's role:employee policy; Manager does not carry
        // shared-document:manage, so the "not self" branch must Forbid.
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Employee);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);
        using var client = await ClientAs(companyId, managerId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/acknowledgement-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_History_For_HrAdministrator_Viewing_Another_Employee_After_An_Acknowledgement()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.Employee);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var docId = await UploadAsync(hrClient, companyId, categoryId, "Remote Working Policy");
        await PublishDirectlyAsync(companyId, docId);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var ack = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/acknowledge", new { Confirmed = true });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        var response = await hrClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/acknowledgement-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.Contains(payload!.Items, i => i.DocumentTitle == "Remote Working Policy" && i.VersionNumber == 1);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<Guid> UploadAsync(HttpClient client, Guid companyId, Guid categoryId, string title)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent(categoryId.ToString()), "CategoryId" },
        };
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy.pdf");

        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents", form);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == payload!.Id);
        doc.MarkScanClean(DateTimeOffset.UtcNow);
        var version = await db.SharedCompanyDocumentVersions
            .Where(v => v.SharedCompanyDocumentId == payload!.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();
        version.MarkScanClean(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return payload!.Id;
    }

    private async Task PublishDirectlyAsync(Guid companyId, Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == documentId && d.CompanyId == companyId);
        doc.Publish(Guid.NewGuid(), DateTimeOffset.UtcNow);
        doc.SetAcknowledgementSettings(
            true, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
    private sealed record HistoryPayload(IReadOnlyList<HistoryItem> Items);
    private sealed record HistoryItem(string DocumentTitle, int VersionNumber, DateTimeOffset AcknowledgedAt);
}
