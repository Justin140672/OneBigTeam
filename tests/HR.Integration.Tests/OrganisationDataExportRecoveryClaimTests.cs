using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up H: recovery of a stalled organisation data export must claim ownership atomically before
/// deleting any files, so a resurrected original worker (or a second concurrent recovery sweep) can
/// never race it. Proven here against the real database and the real
/// <see cref="IOrganisationDataExportJobStore"/> / optimistic-concurrency token.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportRecoveryClaimTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportRecoveryClaimTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Recovery_Claim_Locks_Out_A_Resurrected_Original_Worker_And_A_Second_Sweep()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var tokenA = Guid.NewGuid();
            var exportId = await SeedInProgressWithExpiredLeaseAsync(companyId, tokenA);

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            var recovery = Guid.NewGuid();
            Assert.True(await store.ClaimForRecoveryAsync(exportId, recovery, CancellationToken.None));

            // Original worker A wakes up: it no longer owns the lease.
            Assert.False(await store.RenewLeaseAsync(exportId, tokenA, CancellationToken.None));
            Assert.False(await store.MarkCompletedAsync(
                exportId, tokenA, $"organisation-exports/{companyId}/{exportId}/{tokenA}.zip", 10, CancellationToken.None));

            // A second recovery sweep sees a now-live lease and abandons.
            Assert.False(await store.ClaimForRecoveryAsync(exportId, Guid.NewGuid(), CancellationToken.None));

            // The claiming sweep may reset it for another attempt.
            Assert.True(await store.ResetForRetryAsync(exportId, recovery, CancellationToken.None));

            await AssertRowAsync(exportId, row =>
            {
                Assert.Equal(OrganisationDataExport.StatusPending, row.Status);
                Assert.Null(row.LeaseOwnerToken);
                Assert.Null(row.LeaseExpiresAt);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task A_Resurrected_Worker_That_Renews_First_Blocks_The_Recovery_Claim()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var tokenA = Guid.NewGuid();
            var exportId = await SeedInProgressWithExpiredLeaseAsync(companyId, tokenA);

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();

            // Worker A comes back to life just before the sweep and renews its lease.
            Assert.True(await store.RenewLeaseAsync(exportId, tokenA, CancellationToken.None));

            Assert.False(await store.ClaimForRecoveryAsync(exportId, Guid.NewGuid(), CancellationToken.None));

            await AssertRowAsync(exportId, row =>
            {
                Assert.Equal(OrganisationDataExport.StatusInProgress, row.Status);
                Assert.Equal(tokenA, row.LeaseOwnerToken);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

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
