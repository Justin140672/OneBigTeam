using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportJobStoreTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ReportingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task Lifecycle_Marks_InProgress_Then_Completed()
    {
        await using var db = BuildContext();
        var export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", new DateTimeOffset(Now));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();

        var store = new OrganisationDataExportJobStore(db, new FakeClock(Now));

        await store.MarkInProgressAsync(export.Id, CancellationToken.None);
        await store.MarkCompletedAsync(export.Id, "organisation-exports/x/y.zip", 99, CancellationToken.None);

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal("Completed", view!.Status);
        Assert.Equal("organisation-exports/x/y.zip", view.StorageKey);
    }

    [Fact]
    public async Task GetExpired_Returns_Only_Completed_Past_Expiry()
    {
        await using var db = BuildContext();
        var clock = new FakeClock(Now);
        var store = new OrganisationDataExportJobStore(db, clock);

        var fresh = OrganisationDataExport.Create(Guid.NewGuid(), null, null, new DateTimeOffset(Now.AddDays(-1)));
        fresh.MarkInProgress(new DateTimeOffset(Now.AddDays(-1)));
        fresh.MarkCompleted("k1", 1, new DateTimeOffset(Now.AddDays(-1))); // expires in 6 days

        var stale = OrganisationDataExport.Create(Guid.NewGuid(), null, null, new DateTimeOffset(Now.AddDays(-30)));
        stale.MarkInProgress(new DateTimeOffset(Now.AddDays(-30)));
        stale.MarkCompleted("k2", 1, new DateTimeOffset(Now.AddDays(-30))); // expired 23 days ago

        db.OrganisationDataExports.AddRange(fresh, stale);
        await db.SaveChangesAsync();

        var expired = await store.GetExpiredAsync(CancellationToken.None);

        Assert.Single(expired);
        Assert.Equal(stale.Id, expired[0].Id);

        await store.MarkExpiredAsync(stale.Id, CancellationToken.None);
        var reloaded = await store.GetAsync(stale.Id, CancellationToken.None);
        Assert.Equal("Expired", reloaded!.Status);
    }

    [Fact]
    public async Task StatusReader_HasActiveExport_True_For_Pending_Or_InProgress_Only()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reader = new OrganisationDataExportStatusReader(db);

        Assert.False(await reader.HasActiveExportAsync(companyId, CancellationToken.None));

        var pending = OrganisationDataExport.Create(companyId, null, null, new DateTimeOffset(Now));
        db.OrganisationDataExports.Add(pending);
        await db.SaveChangesAsync();
        Assert.True(await reader.HasActiveExportAsync(companyId, CancellationToken.None));

        pending.MarkInProgress(new DateTimeOffset(Now));
        pending.MarkCompleted("k", 1, new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        Assert.False(await reader.HasActiveExportAsync(companyId, CancellationToken.None));
    }
}
