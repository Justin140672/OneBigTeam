using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// REP-05: exercises the Sickness report's bounded-result behaviour at real data volumes exceeding
/// ReportLimits.DisplayRowLimit (20,000) for the live Get* endpoint. GetSicknessReportHandler groups
/// by employee by default, so distinct employees (one sickness record each) are seeded — one record
/// per employee — to exceed the cap via group count, not raw record count. Records are seeded via a
/// single bulk AddRange + SaveChangesAsync to keep this within a reasonable test runtime.
/// </summary>
[Collection("Integration")]
public class SicknessReportVolumeEndpointTests
{
    private const int DisplayRowLimit = 20_000;

    private readonly ApiWebApplicationFactory _factory;

    public SicknessReportVolumeEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task SeedRecordsAsync(Guid companyId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        var now = DateTimeOffset.UtcNow;

        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, $"Vol-{Guid.NewGuid():N}", 1, now);
        db.SicknessCategories.Add(category);

        var records = Enumerable.Range(0, count)
            .Select(i => SicknessRecord.Create(
                Guid.NewGuid(), companyId, Guid.NewGuid(), category.Id,
                new DateOnly(2026, 1, 1), SicknessDayPart.FullDay,
                new DateOnly(2026, 1, 2), SicknessDayPart.FullDay,
                totalDays: 2m, notes: null, evidenceStatus: SicknessEvidenceStatus.NotRequired, now: now.AddSeconds(i)))
            .ToList();
        db.SicknessRecords.AddRange(records);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_SicknessReport_Above_DisplayRowLimit_Reports_Truncated_And_Full_Total()
    {
        var companyId = Guid.NewGuid();
        const int overLimitBy = 500;
        await SeedRecordsAsync(companyId, DisplayRowLimit + overLimitBy);
        using var client = await HrAdminClientFor(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/sickness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();

        Assert.NotNull(payload);
        Assert.True(payload!.IsTruncated);
        Assert.Equal(DisplayRowLimit + overLimitBy, payload.TotalCount);
        Assert.Equal(DisplayRowLimit, payload.Items.Count);
    }

    [Fact]
    public async Task Get_SicknessReport_Returns_Deterministic_Order_Across_Repeated_Calls()
    {
        // Proves SicknessReportReader's `OrderBy(r => r.Id)` produces a stable, repeatable row
        // order end-to-end — each employee here has exactly one record, so group order equals
        // record order.
        var companyId = Guid.NewGuid();
        await SeedRecordsAsync(companyId, 200);
        using var client = await HrAdminClientFor(companyId);

        var first = await client.GetFromJsonAsync<ReportPayload>($"/api/companies/{companyId}/reporting/sickness");
        var second = await client.GetFromJsonAsync<ReportPayload>($"/api/companies/{companyId}/reporting/sickness");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(200, first!.Items.Count);
        Assert.Equal(
            first.Items.Select(i => i.GroupKey),
            second!.Items.Select(i => i.GroupKey));
    }

    [Fact]
    public async Task Export_SicknessReport_Above_DisplayRowLimit_Caps_Csv_Rows()
    {
        // ExportSicknessReportHandler delegates to GetSicknessReportHandler, so the export is
        // bounded by DisplayRowLimit (20,000) here too, not ExportRowLimit (50,000). The export
        // endpoint streams a CSV file rather than a JSON DTO, so this asserts the row cap
        // indirectly via CSV line count.
        var companyId = Guid.NewGuid();
        await SeedRecordsAsync(companyId, DisplayRowLimit + 500);
        using var client = await HrAdminClientFor(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/sickness/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var lineCount = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.Equal(DisplayRowLimit + 1, lineCount);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items, int TotalCount, bool IsTruncated);

    private sealed record ReportItemPayload(
        string GroupKey,
        string GroupLabel,
        int AbsenceCount,
        decimal DaysAbsent,
        int BradfordScore);
}
