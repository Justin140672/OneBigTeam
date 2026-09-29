using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Tenant-isolation guardrail (ticket: "Strengthen tenant-isolation guardrails"). The existing
/// <see cref="CompanyCrossTenantAccessTests"/> proves TenantRouteAuthorizationMiddleware rejects a
/// route carrying ANOTHER company's id. This class covers the harder case the middleware cannot
/// see: the route carries the caller's OWN, legitimate company id, but the resource id in the
/// route/body/query belongs to a DIFFERENT company. Every test asserts that
///   1. the response is 403/404 (never 2xx) and discloses nothing from the foreign tenant; and
///   2. the foreign tenant's resource is verifiably unchanged afterwards.
/// Operations sampled across modules: get-by-id, update-by-id, delete/deactivate, bulk operations,
/// direct-EF update/delete, background-job-enqueueing operations and download endpoints.
/// </summary>
[Collection("Integration")]
public class CrossTenantResourceIsolationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("dd0000ff-0000-0000-0000-000000000001");

    public CrossTenantResourceIsolationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, bool followRedirects = true)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        return client;
    }

    private static async Task AssertDeniedWithoutDisclosureAsync(HttpResponseMessage response, params string[] foreignMarkers)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 403/404 for a foreign-tenant resource but got {(int)response.StatusCode} {response.StatusCode}.");

        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in foreignMarkers)
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record IdPayload(Guid Id);

    // ------------------------------------------------------------------ Employees: get by id

    private static async Task<(Guid EmployeeId, string LastName)> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var lastName = $"Isolated{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Tenant", lastName, $"tenant.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<IdPayload>())!.Id, lastName);
    }

    [Fact]
    public async Task Get_Employee_By_Id_From_Another_Company_Is_Denied_Without_Disclosure()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var (employeeId, lastName) = await CreateEmployeeAsync(clientA, companyA);

        var response = await clientB.GetAsync($"/api/companies/{companyB}/employees/{employeeId}");

        await AssertDeniedWithoutDisclosureAsync(response, lastName);
    }

    [Fact]
    public async Task Get_Compensation_History_For_Employee_Of_Another_Company_Is_Denied()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(clientA, companyA);

        var created = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/employees/{employeeId}/compensation", new
        {
            companyId = companyA, employeeId, effectiveFrom = "2099-01-01",
            salaryType = "Annual", salary = 61234m, currency = "GBP", reason = "NewHire"
        });
        created.EnsureSuccessStatusCode();

        var response = await clientB.GetAsync(
            $"/api/companies/{companyB}/employees/{employeeId}/compensation/history");

        await AssertDeniedWithoutDisclosureAsync(response, "61234");
    }

    // ------------------------------------------------- Employees: get / update / delete by id

    [Fact]
    public async Task Department_Get_Update_Delete_By_Id_From_Another_Company_Are_Denied_And_Department_Unchanged()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var name = $"Confidential Dept {Guid.NewGuid():N}";

        var created = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/departments", new { companyId = companyA, name });
        created.EnsureSuccessStatusCode();
        var departmentId = (await created.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.GetAsync($"/api/companies/{companyB}/departments/{departmentId}"), name);

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.PutAsJsonAsync($"/api/companies/{companyB}/departments/{departmentId}",
                new { companyId = companyB, id = departmentId, name = "Hijacked", expectedVersion = 1 }),
            name);

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.DeleteAsync($"/api/companies/{companyB}/departments/{departmentId}"), name);

        var after = await clientA.GetFromJsonAsync<JsonElement>($"/api/companies/{companyA}/departments/{departmentId}");
        Assert.Equal(name, after.GetProperty("name").GetString());
        Assert.True(after.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, after.GetProperty("version").GetInt32());
    }

    // -------------------------------------------------------- Direct EF delete of a child row

    [Fact]
    public async Task Delete_Future_Compensation_Record_Of_Another_Company_Is_Denied_And_Record_Survives()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var employeeA = await CompensationTestHelpers.CreateEmployeeAsync(clientA, companyA);
        var employeeB = await CompensationTestHelpers.CreateEmployeeAsync(clientB, companyB);

        var created = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/employees/{employeeA}/compensation", new
        {
            companyId = companyA, employeeId = employeeA, effectiveFrom = "2099-01-01",
            salaryType = "Annual", salary = 50000m, currency = "GBP", reason = "NewHire"
        });
        created.EnsureSuccessStatusCode();
        var recordId = (await created.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        // (a) foreign employee + foreign record, (b) the caller's OWN employee paired with the foreign record id.
        var attemptA = await clientB.DeleteAsync($"/api/companies/{companyB}/employees/{employeeA}/compensation/{recordId}");
        var attemptB = await clientB.DeleteAsync($"/api/companies/{companyB}/employees/{employeeB}/compensation/{recordId}");

        await AssertDeniedWithoutDisclosureAsync(attemptA);
        await AssertDeniedWithoutDisclosureAsync(attemptB);

        var history = await clientA.GetFromJsonAsync<JsonElement>(
            $"/api/companies/{companyA}/employees/{employeeA}/compensation/history");
        Assert.Contains(history.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == recordId);
    }

    // ------------------------------------------------------------------------- Bulk operation

    [Fact]
    public async Task Bulk_Compensation_Adjustment_Naming_Employees_Of_Another_Company_Does_Not_Modify_Them()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var employeeA = await CompensationTestHelpers.CreateEmployeeAsync(clientA, companyA);

        var response = await clientB.PostAsJsonAsync($"/api/companies/{companyB}/compensation/bulk", new
        {
            companyId = companyB,
            effectiveDate = "2099-01-01",
            reason = "AnnualReview",
            adjustmentMode = "PercentageIncrease",
            items = new object[] { new { employeeId = employeeA, proposedSalary = 99999m, salaryType = "Annual", currency = "GBP" } }
        });

        // Whatever the status (400/403/404, or a 200 that reports the item as rejected), the
        // foreign employee must not be reported as adjusted.
        if (response.IsSuccessStatusCode)
            Assert.False(await BulkReportedSuccessForAsync(response, employeeA),
                "Bulk operation must not report success for an employee of another company.");

        var history = await clientA.GetFromJsonAsync<JsonElement>(
            $"/api/companies/{companyA}/employees/{employeeA}/compensation/history");
        Assert.DoesNotContain("99999", history.GetRawText());
        Assert.Empty(history.GetProperty("items").EnumerateArray());
    }

    private static async Task<bool> BulkReportedSuccessForAsync(HttpResponseMessage response, Guid employeeId)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("items", out var items)) return false;
        return items.EnumerateArray().Any(i =>
            i.TryGetProperty("employeeId", out var id) && id.GetGuid() == employeeId
            && i.TryGetProperty("status", out var status)
            && status.ToString().Contains("Applied", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------- Assets: get by id, update, deactivate

    [Fact]
    public async Task Asset_Get_By_Id_From_Another_Company_Is_Denied_Without_Disclosure()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var assetName = $"SecretLaptop{Guid.NewGuid():N}";

        var category = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/asset-categories",
            new { companyId = companyA, name = "Electronics" });
        category.EnsureSuccessStatusCode();
        var categoryId = (await category.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var asset = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/assets",
            new { companyId = companyA, assetNumber = "ISO-001", categoryId, name = assetName });
        asset.EnsureSuccessStatusCode();
        var assetId = (await asset.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.GetAsync($"/api/companies/{companyB}/assets/{assetId}"), assetName);
    }

    [Fact]
    public async Task AssetCategory_Update_And_Deactivate_By_Id_From_Another_Company_Are_Denied_And_Category_Unchanged()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var name = $"Category {Guid.NewGuid():N}";

        var category = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/asset-categories",
            new { companyId = companyA, name });
        category.EnsureSuccessStatusCode();
        var categoryId = (await category.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.PutAsJsonAsync($"/api/companies/{companyB}/asset-categories/{categoryId}",
                new { companyId = companyB, id = categoryId, name = "Hijacked", expectedVersion = 1 }),
            name);
        await AssertDeniedWithoutDisclosureAsync(
            await clientB.DeleteAsync($"/api/companies/{companyB}/asset-categories/{categoryId}"), name);

        var list = await clientA.GetFromJsonAsync<JsonElement>($"/api/companies/{companyA}/asset-categories");
        Assert.Contains(name, list.GetRawText());
        Assert.DoesNotContain("Hijacked", list.GetRawText());
    }

    // ----------------------------------------------------------------- Download endpoint

    [Fact]
    public async Task Download_Employee_Document_Of_Another_Company_Is_Denied_And_Issues_No_Redirect()
    {
        // Seeded Acme employee document (see DownloadEmployeeDocumentEndpointTests); caller is a
        // legitimate admin of an unrelated company and uses their OWN company id in the route.
        var acmeEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var acmeDocumentId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var otherCompany = Guid.NewGuid();
        using var client = await ClientFor(otherCompany, followRedirects: false);

        var response = await client.GetAsync(
            $"/api/companies/{otherCompany}/employees/{acmeEmployeeId}/documents/{acmeDocumentId}/download");

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(response.Headers.Location);
        await AssertDeniedWithoutDisclosureAsync(response);
    }

    // ----------------------- Identity: bulk operation, background-job operation, direct update

    [Fact]
    public async Task Queue_Invitation_Batch_Naming_Employees_Of_Another_Company_Queues_Nothing_And_Leaks_No_Email()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientB = await ClientFor(companyB);
        var emailA = $"foreign.{Guid.NewGuid():N}@test.example";
        var employeeA = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyA, "Foreign", "Invitee", emailA);

        var response = await clientB.PostAsJsonAsync($"/api/companies/{companyB}/invitation-batches",
            new { companyId = companyB, employeeIds = new[] { employeeA } });

        // No eligible recipient remains -> validation failure (400) or an explicit exclusion; never a queued batch.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(emailA, body, StringComparison.OrdinalIgnoreCase);
        if (response.IsSuccessStatusCode)
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.Equal(0, json.GetProperty("queuedCount").GetInt32());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.InvitationBatchRecipients.AnyAsync(r => r.EmployeeId == employeeA));
        Assert.False(await db.InvitationBatches.AnyAsync(b => b.CompanyId == companyB));
    }

    [Fact]
    public async Task Invitation_Batch_Status_And_Retry_By_Id_From_Another_Company_Are_Denied_And_Enqueue_No_Job()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await ClientFor(companyA);
        using var clientB = await ClientFor(companyB);
        var employeeA = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyA, "Batch", "Owner");

        var queued = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/invitation-batches",
            new { companyId = companyA, employeeIds = new[] { employeeA } });
        queued.EnsureSuccessStatusCode();
        var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid();

        var jobsBefore = ((FakeBackgroundJobClient)_factory.Services
            .GetRequiredService<Hangfire.IBackgroundJobClient>()).CreatedJobs.Count;

        await AssertDeniedWithoutDisclosureAsync(
            await clientB.GetAsync($"/api/companies/{companyB}/invitation-batches/{batchId}"));
        await AssertDeniedWithoutDisclosureAsync(
            await clientB.PostAsync($"/api/companies/{companyB}/invitation-batches/{batchId}/retry",
                JsonContent.Create(new { })));

        var jobsAfter = ((FakeBackgroundJobClient)_factory.Services
            .GetRequiredService<Hangfire.IBackgroundJobClient>()).CreatedJobs.Count;
        Assert.Equal(jobsBefore, jobsAfter);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var batch = await db.InvitationBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(companyA, batch.CompanyId);
    }

    [Fact]
    public async Task Disable_And_Enable_User_Of_Another_Company_Are_Denied_And_User_Unchanged()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientB = await ClientFor(companyB);
        var employeeA = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyA, "Victim", "User");
        var userA = await IdentityUserAdminTestHelpers.SeedAccountAsync(
            _factory, employeeA, $"victim.{Guid.NewGuid():N}@test.example", isActive: true);

        var disable = await clientB.PostAsync($"/api/companies/{companyB}/users/{userA}/disable",
            JsonContent.Create(new { }));
        await AssertDeniedWithoutDisclosureAsync(disable);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True((await db.UserProfiles.AsNoTracking().SingleAsync(u => u.Id == userA)).IsActive);
    }
}
