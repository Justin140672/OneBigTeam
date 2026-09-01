using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Story 2: organisation data export endpoints, all gated by "role:company-administrator" plus a
/// caller-tenant check (mirrors PurgeEligibleArchivedEmployeeDocumentsEndpointTests).
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportEndpointsTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportEndpointsTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/companies/{AcmeCompanyId}/reporting/data-exports", EmptyJson());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_Returns_Forbidden_For_Other_Tenant()
    {
        using var client = await CompanyAdminClient();
        var response = await client.PostAsync($"/api/companies/{OtherCompanyId}/reporting/data-exports", EmptyJson());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_Creates_Pending_Export_And_Rejects_Duplicate_With_Conflict()
    {
        await ClearExports();
        using var client = await CompanyAdminClient();

        var first = await client.PostAsync($"/api/companies/{AcmeCompanyId}/reporting/data-exports", EmptyJson());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var payload = await first.Content.ReadFromJsonAsync<RequestPayload>();
        Assert.NotEqual(Guid.Empty, payload!.ExportId);
        Assert.Equal("Pending", payload.Status);

        var second = await client.PostAsync($"/api/companies/{AcmeCompanyId}/reporting/data-exports", EmptyJson());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await ClearExports();
    }

    [Fact]
    public async Task GetLatest_And_List_Return_Seeded_History()
    {
        await ClearExports();
        var completedId = await SeedCompletedExportAsync();

        using var client = await CompanyAdminClient();

        var latest = await client.GetFromJsonAsync<LatestPayload>(
            $"/api/companies/{AcmeCompanyId}/reporting/data-exports/latest");
        Assert.Equal(completedId, latest!.ExportId);
        Assert.Equal("Completed", latest.Status);
        Assert.True(latest.Downloadable);

        var list = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{AcmeCompanyId}/reporting/data-exports");
        Assert.Contains(list!.Exports, e => e.ExportId == completedId);

        await ClearExports();
    }

    [Fact]
    public async Task Download_Streams_Zip_For_Completed_Export_And_404_For_Unknown()
    {
        await ClearExports();
        var completedId = await SeedCompletedExportAsync();

        using var client = await CompanyAdminClient();

        var ok = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/reporting/data-exports/{completedId}/download");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/zip", ok.Content.Headers.ContentType!.MediaType);
        var bytes = await ok.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        var missing = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/reporting/data-exports/{Guid.NewGuid()}/download");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await ClearExports();
    }

    [Fact]
    public async Task Download_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/reporting/data-exports/{Guid.NewGuid()}/download");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedCompletedExportAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportStorage>();

        var export = OrganisationDataExport.Create(AcmeCompanyId, CompanyAdmin, "Company Admin", DateTimeOffset.UtcNow.AddMinutes(-10));
        export.MarkInProgress(DateTimeOffset.UtcNow.AddMinutes(-9));

        using var content = new MemoryStream("PK fake-zip"u8.ToArray());
        var key = await storage.UploadAsync(AcmeCompanyId, export.Id, content, CancellationToken.None);
        export.MarkCompleted(key, content.Length, DateTimeOffset.UtcNow.AddMinutes(-8));

        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        return export.Id;
    }

    private async Task ClearExports()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await db.OrganisationDataExports
            .Where(e => e.CompanyId == AcmeCompanyId)
            .ExecuteDeleteAsync();
    }

    private async Task<HttpClient> CompanyAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.CompanyAdministrator, AcmeCompanyId);
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private sealed record RequestPayload(Guid ExportId, string Status);
    private sealed record LatestPayload(Guid? ExportId, string? Status, bool Downloadable);
    private sealed record ListPayload(List<ListItem> Exports);
    private sealed record ListItem(Guid ExportId, string Status);
}
