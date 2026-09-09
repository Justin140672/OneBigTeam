using System.Text;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up I: attempt archives left in storage by a failed or superseded export build (upload
/// succeeded, completion never persisted) must be swept up and the export marked cleaned. Proven here
/// against the real database, the real <see cref="IOrganisationDataExportJobStore"/> and the real
/// (local file system) <see cref="IOrganisationDataExportStorage"/>.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportArtefactCleanupTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportArtefactCleanupTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Orphan_Attempt_Archives_For_A_Failed_Export_Are_Removed_And_The_Row_Is_Marked_Cleaned()
    {
        var companyId = Guid.NewGuid();
        try
        {
            var exportId = await SeedFailedExportAsync(companyId);

            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();
            var storage = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportStorage>();

            // Two attempts uploaded their archives; neither completion persisted.
            await UploadOrphanAsync(storage, companyId, exportId);
            await UploadOrphanAsync(storage, companyId, exportId);

            var keys = await storage.ListAttemptKeysAsync(companyId, exportId, CancellationToken.None);
            Assert.Equal(2, keys.Count);

            // Cleanup: the export has no published StorageKey, so every attempt archive is an orphan.
            foreach (var key in keys)
                await storage.DeleteAsync(key, CancellationToken.None);
            await store.MarkAttemptFilesCleanedAsync(exportId, CancellationToken.None);

            Assert.Empty(await storage.ListAttemptKeysAsync(companyId, exportId, CancellationToken.None));
            await AssertRowAsync(exportId, row =>
            {
                Assert.Equal(OrganisationDataExport.StatusFailed, row.Status);
                Assert.NotNull(row.AttemptFilesCleanedAt);
                Assert.Equal("boom", row.FailureReason);
            });
        }
        finally
        {
            await ClearExports(companyId);
        }
    }

    private static async Task UploadOrphanAsync(IOrganisationDataExportStorage storage, Guid companyId, Guid exportId)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("ZIP"));
        await storage.UploadAsync(companyId, exportId, Guid.NewGuid(), content, CancellationToken.None);
    }

    private async Task<Guid> SeedFailedExportAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var export = OrganisationDataExport.Create(companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-30));
        export.BeginAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-25));
        export.MarkFailed("boom", DateTimeOffset.UtcNow.AddMinutes(-20));
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
