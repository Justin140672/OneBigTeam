using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// REP-05: exercises the Asset Assignment report's bounded-result behaviour at real data volumes
/// exceeding both ReportLimits.DisplayRowLimit (20,000, for the live Get* endpoint) and confirms
/// the paired Export* endpoint — which delegates to the same Get handler rather than using
/// ReportLimits.ExportRowLimit — caps at the same 20,000-row bound. Asset assignments are seeded
/// via a single bulk AddRange + SaveChangesAsync (not one row per HTTP call) to keep this within a
/// reasonable test runtime; a single shared Asset row is reused across every assignment since
/// AssetAssignment.AssetId carries no DB-level FK constraint requiring a distinct asset per row.
/// </summary>
[Collection("Integration")]
public class AssetAssignmentReportVolumeEndpointTests
{
    private const int DisplayRowLimit = 20_000;

    private readonly ApiWebApplicationFactory _factory;

    public AssetAssignmentReportVolumeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> HrAdminClientFor(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task SeedAssignmentsAsync(Guid companyId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var now = DateTimeOffset.UtcNow;

        var asset = Asset.Create(
            Guid.NewGuid(), companyId, $"VOL-{Guid.NewGuid():N}", Guid.NewGuid(),
            "Volume Test Laptop", "Acme", "Model X", "SN-VOL", null, null, now);
        db.Assets.Add(asset);

        var assignments = Enumerable.Range(0, count)
            .Select(i => AssetAssignment.Create(
                Guid.NewGuid(), companyId, asset.Id, Guid.NewGuid(), Guid.NewGuid(),
                notes: null, now: now.AddSeconds(i)))
            .ToList();
        db.AssetAssignments.AddRange(assignments);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_AssetAssignmentReport_Above_DisplayRowLimit_Reports_Truncated_And_Full_Total()
    {
        var companyId = Guid.NewGuid();
        const int overLimitBy = 500;
        await SeedAssignmentsAsync(companyId, DisplayRowLimit + overLimitBy);
        using var client = await HrAdminClientFor(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/asset-assignment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();

        Assert.NotNull(payload);
        Assert.True(payload!.IsTruncated);
        Assert.Equal(DisplayRowLimit + overLimitBy, payload.TotalAssignments);
        Assert.Equal(DisplayRowLimit, payload.Items.Count);
    }

    [Fact]
    public async Task Get_AssetAssignmentReport_Returns_Deterministic_Order_Across_Repeated_Calls()
    {
        // Proves AssetAssignmentReportReader's `orderby aa.Id` produces a stable, repeatable row
        // order end-to-end (not just at the unit-test/fake level).
        var companyId = Guid.NewGuid();
        await SeedAssignmentsAsync(companyId, 200);
        using var client = await HrAdminClientFor(companyId);

        var first = await client.GetFromJsonAsync<ReportPayload>($"/api/companies/{companyId}/reporting/asset-assignment");
        var second = await client.GetFromJsonAsync<ReportPayload>($"/api/companies/{companyId}/reporting/asset-assignment");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(200, first!.Items.Count);
        Assert.Equal(
            first.Items.Select(i => (i.EmployeeId, i.AssignedDate)),
            second!.Items.Select(i => (i.EmployeeId, i.AssignedDate)));
    }

    [Fact]
    public async Task Export_AssetAssignmentReport_Above_DisplayRowLimit_Caps_Csv_Rows()
    {
        // ExportAssetAssignmentReportHandler delegates to GetAssetAssignmentReportHandler rather
        // than applying ReportLimits.ExportRowLimit itself (see ticket note on this report pair),
        // so the export is bounded by DisplayRowLimit (20,000), not ExportRowLimit (50,000). The
        // export endpoint streams a CSV file rather than a JSON DTO, so IsTruncated/TotalCount
        // aren't observable over HTTP here — this asserts the row cap indirectly via CSV line count.
        var companyId = Guid.NewGuid();
        await SeedAssignmentsAsync(companyId, DisplayRowLimit + 500);
        using var client = await HrAdminClientFor(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/asset-assignment/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var lineCount = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // +1 for the CSV header row.
        Assert.Equal(DisplayRowLimit + 1, lineCount);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items, int TotalAssignments, bool IsTruncated);

    private sealed record ReportItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        string AssetName,
        string? SerialNumber,
        DateTimeOffset AssignedDate,
        string ReturnStatus);
}
