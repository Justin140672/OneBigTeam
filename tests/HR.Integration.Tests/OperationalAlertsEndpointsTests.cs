using System.Net;
using System.Net.Http.Json;

using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up B: platform-admin Operational Alerts endpoints (list / detail / resolve). Same
/// "platform:admin" policy + allow-list gate pattern as GetAuditLogEndpointTests /
/// ListCustomersEndpointTests — anonymous is 401, authenticated-but-not-allow-listed is 403.
/// FastEndpoints request-validation failures surface as 422 (UnprocessableEntity), matching the
/// rest of the suite.
/// </summary>
[Collection("Integration")]
public class OperationalAlertsEndpointsTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";
    private const string BaseUrl = "/api/notifications/admin/operational-alerts";

    private readonly ApiWebApplicationFactory _factory;

    public OperationalAlertsEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return client;
    }

    private static RaiseAdministrativeAlertCommand Command(
        Guid companyId,
        string dedupKey,
        AdministrativeAlertCategory category = AdministrativeAlertCategory.IntegrationDelivery,
        AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning,
        DateTimeOffset? occurredAt = null) =>
        new(
            companyId,
            severity,
            category,
            "Something needs attention",
            "Sensitive detail: missing-file-payslip.pdf",
            occurredAt ?? DateTimeOffset.UtcNow,
            dedupKey,
            "OrganisationDataExport",
            Guid.NewGuid(),
            "Investigate",
            null,
            3);

    private async Task<Guid> SeedAlertAsync(RaiseAdministrativeAlertCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), command, command.OccurredAt);
        db.AdministrativeAlerts.Add(alert);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    private async Task ResetCompanyAlertsAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alertIds = await db.AdministrativeAlerts
            .Where(a => a.CompanyId == companyId).Select(a => a.Id).ToListAsync();
        await db.OperationalAlertEmailDeliveries
            .Where(d => alertIds.Contains(d.AlertId)).ExecuteDeleteAsync();
        await db.AdministrativeAlerts.Where(a => a.CompanyId == companyId).ExecuteDeleteAsync();
    }

    // ── Authorization ──────────────────────────────────────────────────────

    [Fact]
    public async Task List_Anonymous_Is_Unauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(BaseUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Anonymous_Is_Unauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_Anonymous_Is_Unauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/resolve", new { ResolutionNote = "Resolved now" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task All_Endpoints_Forbid_An_Authenticated_Non_Platform_Admin_Caller()
    {
        using var client = ClientFor(Guid.NewGuid(), $"not-admin-{Guid.NewGuid():N}@example.com");

        var list = await client.GetAsync(BaseUrl);
        var get = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");
        var resolve = await client.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/resolve", new { ResolutionNote = "Resolved now" });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resolve.StatusCode);
    }

    // ── LIST ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Returns_Seeded_Alerts_Without_Detail_And_Paginated()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        await SeedAlertAsync(Command(companyId, $"key-a-{Guid.NewGuid():N}"));
        await SeedAlertAsync(Command(companyId, $"key-b-{Guid.NewGuid():N}"));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var response = await client.GetAsync($"{BaseUrl}?companyId={companyId}&page=1&pageSize=25");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
        Assert.Equal(1, payload.Page);
        Assert.Equal(25, payload.PageSize);
        Assert.Equal(2, payload.Items.Count);
        Assert.All(payload.Items, i => Assert.Equal(companyId, i.CompanyId));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("missing-file-payslip.pdf", raw);
        Assert.DoesNotContain("Sensitive detail", raw);
    }

    [Fact]
    public async Task List_Filters_By_Company_Category_And_Status()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        await ResetCompanyAlertsAsync(otherCompanyId);

        var openReport = await SeedAlertAsync(Command(companyId, $"rpt-{Guid.NewGuid():N}", AdministrativeAlertCategory.ReportGeneration));
        await SeedAlertAsync(Command(companyId, $"intg-{Guid.NewGuid():N}", AdministrativeAlertCategory.IntegrationDelivery));
        var toResolve = await SeedAlertAsync(Command(companyId, $"res-{Guid.NewGuid():N}", AdministrativeAlertCategory.ReportGeneration));
        await SeedAlertAsync(Command(otherCompanyId, $"other-{Guid.NewGuid():N}", AdministrativeAlertCategory.ReportGeneration));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        (await client.PostAsJsonAsync($"{BaseUrl}/{toResolve}/resolve", new { ResolutionNote = "Handled offline" }))
            .EnsureSuccessStatusCode();

        // company + category filter
        var byCategory = await client.GetFromJsonAsync<ListPayload>(
            $"{BaseUrl}?companyId={companyId}&category=reportgeneration");
        Assert.NotNull(byCategory);
        Assert.Equal(2, byCategory!.TotalCount);
        Assert.All(byCategory.Items, i => Assert.Equal("ReportGeneration", i.Category));

        // status=open excludes the resolved one
        var open = await client.GetFromJsonAsync<ListPayload>(
            $"{BaseUrl}?companyId={companyId}&category=reportgeneration&status=open");
        Assert.NotNull(open);
        Assert.Equal(new[] { openReport }, open!.Items.Select(i => i.Id).ToArray());

        // status=resolved returns only the resolved one
        var resolved = await client.GetFromJsonAsync<ListPayload>(
            $"{BaseUrl}?companyId={companyId}&status=resolved");
        Assert.NotNull(resolved);
        Assert.Equal(new[] { toResolve }, resolved!.Items.Select(i => i.Id).ToArray());
    }

    [Theory]
    [InlineData("?category=not-real")]
    [InlineData("?status=closed")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    public async Task List_Rejects_Invalid_Query_Parameters(string query)
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var response = await client.GetAsync($"{BaseUrl}{query}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GET details ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Returns_Full_Detail_For_A_Known_Alert()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var dedupKey = $"detail-{Guid.NewGuid():N}";
        var alertId = await SeedAlertAsync(Command(companyId, dedupKey));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var payload = await client.GetFromJsonAsync<DetailPayload>($"{BaseUrl}/{alertId}");

        Assert.NotNull(payload);
        Assert.Equal(alertId, payload!.Id);
        Assert.Equal(dedupKey, payload.DedupKey);
        Assert.False(string.IsNullOrEmpty(payload.Detail));
        Assert.Contains("missing-file-payslip.pdf", payload.Detail!);
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_Unknown_Id()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var response = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── RESOLVE ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Sets_Resolved_Status_And_Caller_Attribution()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var alertId = await SeedAlertAsync(Command(companyId, $"resolve-{Guid.NewGuid():N}"));
        var callerUserId = Guid.NewGuid();

        using var client = ClientFor(callerUserId, AllowListedEmail);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/{alertId}/resolve", new { ResolutionNote = "Root cause fixed in storage config" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResolvePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Resolved", payload!.Status);
        Assert.Equal(callerUserId, payload.ResolvedByUserId);
        Assert.NotNull(payload.ResolvedAt);
        Assert.True(payload.ResolvedAt >= before);

        var detail = await client.GetFromJsonAsync<DetailPayload>($"{BaseUrl}/{alertId}");
        Assert.Equal("Resolved", detail!.Status);
        Assert.Equal(callerUserId, detail.ResolvedByUserId);
    }

    [Fact]
    public async Task Resolve_Unknown_Id_Returns_NotFound()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/resolve", new { ResolutionNote = "Resolving a ghost" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_With_Too_Short_Note_Is_Rejected()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var alertId = await SeedAlertAsync(Command(companyId, $"short-{Guid.NewGuid():N}"));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var response = await client.PostAsJsonAsync($"{BaseUrl}/{alertId}/resolve", new { ResolutionNote = "no" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Resolving_An_Already_Resolved_Alert_Returns_Conflict()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var alertId = await SeedAlertAsync(Command(companyId, $"conflict-{Guid.NewGuid():N}"));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        (await client.PostAsJsonAsync($"{BaseUrl}/{alertId}/resolve", new { ResolutionNote = "First resolution" }))
            .EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"{BaseUrl}/{alertId}/resolve", new { ResolutionNote = "Second resolution attempt" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task After_Resolution_An_Identical_Raise_Opens_A_Brand_New_Alert()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var dedupKey = $"recur-{Guid.NewGuid():N}";
        var firstId = await SeedAlertAsync(Command(companyId, dedupKey));

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        (await client.PostAsJsonAsync($"{BaseUrl}/{firstId}/resolve", new { ResolutionNote = "Closed the first one" }))
            .EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAdministrativeAlertWriter>();
            await writer.RaiseAsync(Command(companyId, dedupKey));
        }

        var open = await client.GetFromJsonAsync<ListPayload>($"{BaseUrl}?companyId={companyId}&status=open");
        Assert.NotNull(open);
        var openItem = Assert.Single(open!.Items);
        Assert.NotEqual(firstId, openItem.Id);
        Assert.Equal(1, openItem.OccurrenceCount);
    }

    private sealed record ListPayload(List<ListItem> Items, int TotalCount, int Page, int PageSize);

    private sealed record ListItem(
        Guid Id, Guid CompanyId, string Category, string Severity, string Status,
        string Summary, int OccurrenceCount);

    private sealed record DetailPayload(
        Guid Id, Guid CompanyId, string Status, string? Detail, string DedupKey,
        Guid? ResolvedByUserId, DateTimeOffset? ResolvedAt);

    private sealed record ResolvePayload(Guid Id, string Status, DateTimeOffset? ResolvedAt, Guid? ResolvedByUserId);
}
