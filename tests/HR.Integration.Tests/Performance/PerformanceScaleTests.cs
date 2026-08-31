using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Xunit.Abstractions;

namespace HR.Integration.Tests.Performance;

/// <summary>
/// NFR-02: repeatable performance / scale tests. Each operation is measured at 50 / 500 / 2000
/// employees (the product scale range) against the product target
/// (specifications/product-specifications/31-non-functional-requirements.md), using p95 latency and
/// a CI multiplier (see <see cref="PerformanceMeasurement"/>). Every measured iteration also
/// captures EF command count so an N+1 regression at scale fails the run rather than merely slowing
/// it.
///
/// These tests are in their own <c>Category=Performance</c> trait and their own xUnit collection so
/// the normal PR integration gate never runs them; they run in the dedicated
/// <c>perf-nightly.yml</c> workflow (or on demand).
/// </summary>
[Trait("Category", "Performance")]
[Collection("Performance")]
public sealed class PerformanceScaleTests
{
    // Product targets (ms). See NFR-02 performance table.
    private const double PageLoadTargetMs = 2000;
    private const double DashboardTargetMs = 2000;
    private const double SearchTargetMs = 500;
    private const double CrudTargetMs = 1000;
    private const double SmallReportTargetMs = 10_000;

    // Absolute per-request command ceilings, asserted against the MINIMUM commands-per-request seen
    // across the measured iterations (the minimum is the cleanest observation of one request's own
    // cost — background drain from async domain-event handlers can only ever add commands on top,
    // never remove them). Deliberately generous vs. the observed floor, but constant — far below N.
    // Because the same ceiling is asserted at 50 / 500 / 2000, an N+1 over employees/leave/tasks
    // would push the 2000-employee run into the hundreds/thousands and fail it even though the
    // 50-employee run passed: the three scale points ARE the "flat, not linear" check. Observed
    // floors (see docs/performance-testing.md): list ~21, search ~17, report ~14, CRUD ~14,
    // dashboard ~93 (a fixed provider fan-out, flat across all three scales — see the NFR-02
    // follow-up note).
    private const int ListCommandCeiling = 45;
    private const int DashboardCommandCeiling = 150;
    private const int SearchCommandCeiling = 45;
    private const int CrudCommandCeiling = 35;
    private const int ReportCommandCeiling = 35;

    private static readonly ConcurrentDictionary<int, Task<PerformanceDataSeeder.SeededCompany>> DatasetCache = new();

    private readonly PerfApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public PerformanceScaleTests(PerfApiWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public static TheoryData<string, int> Scales() => new()
    {
        { "small", 50 },
        { "medium", 500 },
        { "large", 2000 },
    };

    private Task<PerformanceDataSeeder.SeededCompany> GetDatasetAsync(int scale) =>
        DatasetCache.GetOrAdd(scale, s => PerformanceDataSeeder.SeedAsync(_factory, s));

    private async Task<HttpClient> HrAdminClientFor(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public async Task Employee_List_Page_Load(string scaleLabel, int scale)
    {
        var data = await GetDatasetAsync(scale);
        using var client = await HrAdminClientFor(data.CompanyId);
        var url = $"/api/companies/{data.CompanyId}/employees?pageNumber=1&pageSize=25";

        var result = await new PerformanceMeasurement(_output).MeasureAsync(
            "employee-list-page", scaleLabel, scale, PageLoadTargetMs,
            () => AssertOk(client, url));

        Assert.True(result.WithinBudget, result.ToString());
        Assert.True(
            result.MinCommandCount <= ListCommandCeiling,
            $"possible N+1: {result.MinCommandCount} commands/request at scale {scale} (ceiling {ListCommandCeiling})");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public async Task Manager_Dashboard_Summary_Load(string scaleLabel, int scale)
    {
        var data = await GetDatasetAsync(scale);
        using var client = await HrAdminClientFor(data.CompanyId);
        var url = $"/api/companies/{data.CompanyId}/dashboards/manager/summary";

        var result = await new PerformanceMeasurement(_output).MeasureAsync(
            "manager-dashboard-summary", scaleLabel, scale, DashboardTargetMs,
            () => AssertOk(client, url));

        Assert.True(result.WithinBudget, result.ToString());
        Assert.True(
            result.MinCommandCount <= DashboardCommandCeiling,
            $"possible N+1: {result.MinCommandCount} commands/request at scale {scale} (ceiling {DashboardCommandCeiling})");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public async Task Employee_Search(string scaleLabel, int scale)
    {
        var data = await GetDatasetAsync(scale);
        using var client = await HrAdminClientFor(data.CompanyId);
        // Matches exactly one employee at every scale (LastName == "Employee00042").
        var url = $"/api/companies/{data.CompanyId}/employees?search=Employee00042";

        var result = await new PerformanceMeasurement(_output).MeasureAsync(
            "employee-search", scaleLabel, scale, SearchTargetMs,
            () => AssertOk(client, url));

        Assert.True(result.WithinBudget, result.ToString());
        Assert.True(
            result.MinCommandCount <= SearchCommandCeiling,
            $"possible N+1: {result.MinCommandCount} commands/request at scale {scale} (ceiling {SearchCommandCeiling})");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public async Task Standard_Crud_Create_Department(string scaleLabel, int scale)
    {
        var data = await GetDatasetAsync(scale);
        using var client = await HrAdminClientFor(data.CompanyId);
        var url = $"/api/companies/{data.CompanyId}/departments";

        var result = await new PerformanceMeasurement(_output).MeasureAsync(
            "create-department", scaleLabel, scale, CrudTargetMs,
            async () =>
            {
                var response = await client.PostAsJsonAsync(url, new
                {
                    companyId = data.CompanyId,
                    name = $"Perf-Dept-{Guid.NewGuid():N}",
                });
                Assert.True(
                    response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                    $"unexpected {(int)response.StatusCode} from {url}");
            });

        Assert.True(result.WithinBudget, result.ToString());
        Assert.True(
            result.MinCommandCount <= CrudCommandCeiling,
            $"possible N+1: {result.MinCommandCount} commands/request at scale {scale} (ceiling {CrudCommandCeiling})");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public async Task Small_Synchronous_Report_Headcount_Summary(string scaleLabel, int scale)
    {
        var data = await GetDatasetAsync(scale);
        using var client = await HrAdminClientFor(data.CompanyId);
        var url = $"/api/companies/{data.CompanyId}/reporting/hr-headcount-summary";

        var result = await new PerformanceMeasurement(_output).MeasureAsync(
            "hr-headcount-summary-report", scaleLabel, scale, SmallReportTargetMs,
            () => AssertOk(client, url));

        Assert.True(result.WithinBudget, result.ToString());
        Assert.True(
            result.MinCommandCount <= ReportCommandCeiling,
            $"possible N+1: {result.MinCommandCount} commands/request at scale {scale} (ceiling {ReportCommandCeiling})");
    }

    private static async Task AssertOk(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
