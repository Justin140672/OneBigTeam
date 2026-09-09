using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up D: an organisation data export is owned by exactly one worker at a time via a renewable
/// lease. These tests prove, against the real database and the real
/// <see cref="IOrganisationDataExportJobStore"/>, that a healthy long-running build keeps ownership,
/// and that a superseded worker can neither fail nor publish the export once a replacement has taken
/// over.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportOwnershipTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportOwnershipTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthy_Build_Exceeding_The_Lease_Duration_Keeps_Ownership()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var exportId = await SeedPendingAsync(companyId);
            var tokenA = Guid.NewGuid();

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            Assert.True(await store.BeginAttemptAsync(exportId, tokenA, CancellationToken.None));

            // Simulate a build that runs well past the 15-minute lease window: the worker heartbeats
            // repeatedly and each renewal must succeed.
            for (var i = 0; i < 5; i++)
                Assert.True(await store.RenewLeaseAsync(exportId, tokenA, CancellationToken.None));

            // While the lease is live the recovery sweep must never see the row as recoverable.
            var now = DateTimeOffset.UtcNow;
            var recoverable = await store.GetRecoverableAsync(now, now, CancellationToken.None);
            Assert.DoesNotContain(exportId, recoverable.Select(r => r.Id));
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task A_Superseded_Worker_Cannot_Mark_The_Export_Failed()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var tokenA = Guid.NewGuid();
            var tokenB = Guid.NewGuid();
            var exportId = await SeedInProgressWithExpiredLeaseAsync(companyId, tokenA);

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            Assert.True(await store.BeginAttemptAsync(exportId, tokenB, CancellationToken.None));

            Assert.False(await store.MarkFailedAsync(exportId, tokenA, "worker A gave up", CancellationToken.None));

            await AssertRowAsync(exportId, row =>
            {
                Assert.Equal(OrganisationDataExport.StatusInProgress, row.Status);
                Assert.Equal(tokenB, row.LeaseOwnerToken);
                Assert.Null(row.FailureReason);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Ownership_Expires_During_Upload_Prevents_Publish()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var tokenA = Guid.NewGuid();
            var tokenB = Guid.NewGuid();
            var exportId = await SeedInProgressWithExpiredLeaseAsync(companyId, tokenA);

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            Assert.True(await store.BeginAttemptAsync(exportId, tokenB, CancellationToken.None));

            Assert.False(await store.MarkCompletedAsync(
                exportId, tokenA, $"organisation-exports/{companyId}/{exportId}/{tokenA}.zip", 4096, CancellationToken.None));

            await AssertRowAsync(exportId, row =>
            {
                Assert.NotEqual(OrganisationDataExport.StatusCompleted, row.Status);
                Assert.Null(row.StorageKey);
                Assert.Equal(tokenB, row.LeaseOwnerToken);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Worker_That_Loses_Ownership_Then_Resumes_Does_Not_Overwrite_A_Completed_Export()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var tokenA = Guid.NewGuid();
            var tokenB = Guid.NewGuid();
            var exportId = await SeedInProgressWithExpiredLeaseAsync(companyId, tokenA);
            var keyB = $"organisation-exports/{companyId}/{exportId}/{tokenB}.zip";

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            Assert.True(await store.BeginAttemptAsync(exportId, tokenB, CancellationToken.None));

            // Replacement worker B finishes the job.
            Assert.True(await store.MarkCompletedAsync(exportId, tokenB, keyB, 8192, CancellationToken.None));

            // Original worker A wakes up and tries to carry on.
            Assert.False(await store.RenewLeaseAsync(exportId, tokenA, CancellationToken.None));
            Assert.False(await store.MarkCompletedAsync(
                exportId, tokenA, $"organisation-exports/{companyId}/{exportId}/{tokenA}.zip", 4096, CancellationToken.None));

            await AssertRowAsync(exportId, row =>
            {
                Assert.Equal(OrganisationDataExport.StatusCompleted, row.Status);
                Assert.Equal(keyB, row.StorageKey);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    private async Task<Guid> SeedPendingAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-1));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        return export.Id;
    }

    /// <summary>
    /// Seed an export already claimed by <paramref name="ownerToken"/> whose 15-minute lease is long
    /// expired (claimed 40 minutes ago), so a replacement worker can immediately take over.
    /// </summary>
    private async Task<Guid> SeedInProgressWithExpiredLeaseAsync(Guid companyId, Guid ownerToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-60));
        export.BeginAttempt(ownerToken, DateTimeOffset.UtcNow.AddMinutes(-40));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        return export.Id;
    }

    private async Task AssertRowAsync(Guid exportId, Action<OrganisationDataExport> assert)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var row = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == exportId);
        assert(row);
    }

    private async Task ClearExports(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await db.OrganisationDataExports.Where(e => e.CompanyId == companyId).ExecuteDeleteAsync();
    }
}
