using System.Net;
using System.Text;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 3: organisation data exports must be recoverable and complete. This class proves the
/// real-database / real-DI wiring the Reporting unit tests cannot: the filtered unique index that
/// blocks a second active export, and the recovery sweep re-enqueuing a build job for an export that
/// was saved but (apparently) never queued.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportRecoveryTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportRecoveryTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_Is_Rejected_With_Conflict_When_An_InProgress_Export_Already_Exists()
    {
        await ClearExports(AcmeCompanyId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var inProgress = OrganisationDataExport.Create(AcmeCompanyId, CompanyAdmin, "Company Admin", DateTimeOffset.UtcNow.AddMinutes(-2));
        inProgress.BeginAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2));
        db.OrganisationDataExports.Add(inProgress);
        await db.SaveChangesAsync();

        using var client = await CompanyAdminClient();
        var response = await client.PostAsync($"/api/companies/{AcmeCompanyId}/reporting/data-exports", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await ClearExports(AcmeCompanyId);
    }

    [Fact]
    public async Task A_Second_Active_Export_Row_Cannot_Be_Persisted_For_The_Same_Company()
    {
        var companyId = Guid.NewGuid();
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                db.OrganisationDataExports.Add(
                    OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-1)));
                await db.SaveChangesAsync();
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                db.OrganisationDataExports.Add(
                    OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Recovery_Sweep_Re_Enqueues_A_Build_Job_For_A_Pending_Export_That_Was_Never_Queued()
    {
        var companyId = Guid.NewGuid();
        Guid exportId;
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-10));
                db.OrganisationDataExports.Add(export);
                await db.SaveChangesAsync();
                exportId = export.Id;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var job = scope.ServiceProvider.GetRequiredService<RecoverStalledOrganisationDataExportsJob>();
                await job.ExecuteAsync(CancellationToken.None);
            }

            var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
                _factory.Services.GetRequiredService<Hangfire.IBackgroundJobClient>());
            Assert.Contains(backgroundJobClient.CreatedJobs, j =>
                j.Type == typeof(OrganisationDataExportBuildJob)
                && j.Args.ElementAtOrDefault(0) is Guid g && g == exportId);
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Two_Workers_Race_The_Same_Pending_Row_Only_The_First_BeginAttempt_Wins()
    {
        var companyId = Guid.NewGuid();
        try
        {
            Guid exportId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-1));
                db.OrganisationDataExports.Add(export);
                await db.SaveChangesAsync();
                exportId = export.Id;
            }

            bool first, second;
            using (var scope = _factory.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();
                first = await store.BeginAttemptAsync(exportId, Guid.NewGuid(), CancellationToken.None);
            }
            using (var scope = _factory.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();
                second = await store.BeginAttemptAsync(exportId, Guid.NewGuid(), CancellationToken.None);
            }

            Assert.True(first);
            Assert.False(second);
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Recovery_Sweep_Resets_And_Re_Enqueues_An_InProgress_Export_Whose_Lease_Has_Expired()
    {
        var companyId = Guid.NewGuid();
        Guid exportId;
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-60));
                // claim 40 minutes ago: the 15-minute lease is long expired
                export.BeginAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-40));
                db.OrganisationDataExports.Add(export);
                await db.SaveChangesAsync();
                exportId = export.Id;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var job = scope.ServiceProvider.GetRequiredService<RecoverStalledOrganisationDataExportsJob>();
                await job.ExecuteAsync(CancellationToken.None);
            }

            var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
                _factory.Services.GetRequiredService<Hangfire.IBackgroundJobClient>());
            Assert.Contains(backgroundJobClient.CreatedJobs, j =>
                j.Type == typeof(OrganisationDataExportBuildJob)
                && j.Args.ElementAtOrDefault(0) is Guid g && g == exportId);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var reloaded = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == exportId);
                Assert.Equal(OrganisationDataExport.StatusPending, reloaded.Status);
                Assert.Null(reloaded.LeaseOwnerToken);
                Assert.Null(reloaded.LeaseExpiresAt);
            }
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    [Fact]
    public async Task Recovery_Sweep_Leaves_A_Healthy_InProgress_Export_With_A_Live_Lease_Untouched()
    {
        var companyId = Guid.NewGuid();
        Guid exportId;
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-60));
                // just claimed: lease is live for another 15 minutes
                export.BeginAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow);
                db.OrganisationDataExports.Add(export);
                await db.SaveChangesAsync();
                exportId = export.Id;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var job = scope.ServiceProvider.GetRequiredService<RecoverStalledOrganisationDataExportsJob>();
                await job.ExecuteAsync(CancellationToken.None);
            }

            var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
                _factory.Services.GetRequiredService<Hangfire.IBackgroundJobClient>());
            Assert.DoesNotContain(backgroundJobClient.CreatedJobs, j =>
                j.Type == typeof(OrganisationDataExportBuildJob)
                && j.Args.ElementAtOrDefault(0) is Guid g && g == exportId);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var reloaded = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == exportId);
                Assert.Equal(OrganisationDataExport.StatusInProgress, reloaded.Status);
                Assert.NotNull(reloaded.LeaseOwnerToken);
            }
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    private async Task ClearExports(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await db.OrganisationDataExports.Where(e => e.CompanyId == companyId).ExecuteDeleteAsync();
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
}
