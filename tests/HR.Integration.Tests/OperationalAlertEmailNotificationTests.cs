using Hangfire;

using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up C: the internal-operations email side-effect of opening a new ReportGeneration
/// administrative alert. Exercises the real Postgres partial unique (company_id, dedup_key) index —
/// the concurrent-creation case cannot be reproduced against the EF Core InMemory provider used by
/// the module unit tests. Job execution itself is a no-op here (ApiWebApplicationFactory swaps in
/// <see cref="FakeBackgroundJobClient"/>); this test only asserts on what gets enqueued and the
/// delivery rows written in the same transaction as the alert.
/// </summary>
[Collection("Integration")]
public class OperationalAlertEmailNotificationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public OperationalAlertEmailNotificationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FakeBackgroundJobClient Jobs =>
        (FakeBackgroundJobClient)_factory.Services.GetRequiredService<IBackgroundJobClient>();

    private static RaiseAdministrativeAlertCommand ReportCommand(
        Guid companyId,
        string dedupKey,
        AdministrativeAlertReason? reason = AdministrativeAlertReason.MissingDocumentExport) =>
        new(
            companyId,
            AdministrativeAlertSeverity.Warning,
            AdministrativeAlertCategory.ReportGeneration,
            "Organisation data export completed with missing files",
            "Missing file: secret-payslip.pdf",
            DateTimeOffset.UtcNow,
            dedupKey,
            "OrganisationDataExport",
            Guid.NewGuid(),
            "Investigate storage",
            null,
            2,
            reason);

    private async Task RaiseAsync(RaiseAdministrativeAlertCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAdministrativeAlertWriter>();
        await writer.RaiseAsync(command);
    }

    private async Task<(int Alerts, int Deliveries, int EnqueuedJobs)> SnapshotAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alertIds = await db.AdministrativeAlerts
            .Where(a => a.CompanyId == companyId).Select(a => a.Id).ToListAsync();
        var deliveries = await db.OperationalAlertEmailDeliveries
            .CountAsync(d => alertIds.Contains(d.AlertId));

        var alertIdSet = alertIds.ToHashSet();
        var enqueued = Jobs.CreatedJobs
            .Where(j => j.Type == typeof(HR.Modules.Notifications.Jobs.SendOperationalAlertEmailJob))
            .Count(j => j.Args.Count > 0 && j.Args[0] is Guid g && alertIdSet.Contains(g));

        return (alertIds.Count, deliveries, enqueued);
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

    [Fact]
    public async Task First_Raise_Of_A_ReportGeneration_Alert_Writes_One_Delivery_And_Enqueues_One_Job()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);

        await RaiseAsync(ReportCommand(companyId, $"rpt-{Guid.NewGuid():N}"));

        var snapshot = await SnapshotAsync(companyId);
        Assert.Equal((1, 1, 1), (snapshot.Alerts, snapshot.Deliveries, snapshot.EnqueuedJobs));
    }

    [Fact]
    public async Task Recurrence_While_Open_Does_Not_Add_A_Delivery_Or_A_Second_Job()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var dedupKey = $"rpt-{Guid.NewGuid():N}";

        await RaiseAsync(ReportCommand(companyId, dedupKey));
        await RaiseAsync(ReportCommand(companyId, dedupKey));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var alert = await db.AdministrativeAlerts.SingleAsync(a => a.CompanyId == companyId);
            Assert.Equal(2, alert.OccurrenceCount);
        }

        var snapshot = await SnapshotAsync(companyId);
        Assert.Equal((1, 1, 1), (snapshot.Alerts, snapshot.Deliveries, snapshot.EnqueuedJobs));
    }

    [Fact]
    public async Task Raise_After_Resolution_Opens_A_New_Alert_With_A_Second_Delivery_And_Second_Job()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var dedupKey = $"rpt-{Guid.NewGuid():N}";

        await RaiseAsync(ReportCommand(companyId, dedupKey));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var alert = await db.AdministrativeAlerts.SingleAsync(a => a.CompanyId == companyId);
            alert.Resolve(Guid.NewGuid(), "Handled", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await RaiseAsync(ReportCommand(companyId, dedupKey));

        var snapshot = await SnapshotAsync(companyId);
        Assert.Equal((2, 2, 2), (snapshot.Alerts, snapshot.Deliveries, snapshot.EnqueuedJobs));
    }

    [Fact]
    public async Task ReportGeneration_Alert_Without_A_Reason_Writes_The_Alert_But_No_Delivery_And_No_Job()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);

        await RaiseAsync(ReportCommand(companyId, $"rpt-{Guid.NewGuid():N}", reason: null));

        var snapshot = await SnapshotAsync(companyId);
        Assert.Equal((1, 0, 0), (snapshot.Alerts, snapshot.Deliveries, snapshot.EnqueuedJobs));
    }

    [Fact]
    public async Task Concurrent_Creation_For_The_Same_New_DedupKey_Produces_Exactly_One_Alert_Delivery_And_Job()
    {
        var companyId = Guid.NewGuid();
        await ResetCompanyAlertsAsync(companyId);
        var dedupKey = $"rpt-concurrent-{Guid.NewGuid():N}";

        await Task.WhenAll(
            RaiseAsync(ReportCommand(companyId, dedupKey)),
            RaiseAsync(ReportCommand(companyId, dedupKey)));

        var snapshot = await SnapshotAsync(companyId);
        Assert.Equal((1, 1, 1), (snapshot.Alerts, snapshot.Deliveries, snapshot.EnqueuedJobs));
    }
}
