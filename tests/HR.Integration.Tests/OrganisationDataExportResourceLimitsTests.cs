using System.IO.Compression;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 4: end-to-end resource-limit wiring. Drives a real build job through DI — exercising the
/// registered <see cref="OrganisationDataExportConcurrencyGate"/>, <see cref="OrganisationDataExportWorkspaceFactory"/>,
/// <see cref="OrganisationDataExportPackageBuilder"/> and the local export storage — and asserts the
/// export completes and its archive downloads as a valid ZIP. Uses a throwaway company so the export
/// dataset stays empty and the test is fast and deterministic.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportResourceLimitsTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000001");

    public OrganisationDataExportResourceLimitsTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Build_Job_Completes_Through_The_Real_Gate_Workspace_And_Storage_And_The_Archive_Is_A_Valid_Zip()
    {
        var companyId = Guid.NewGuid();
        Guid exportId;
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var export = OrganisationDataExport.Create(
                    companyId, CompanyAdmin, "Admin", DateTimeOffset.UtcNow.AddMinutes(-10));
                db.OrganisationDataExports.Add(export);
                await db.SaveChangesAsync();
                exportId = export.Id;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var job = scope.ServiceProvider.GetRequiredService<OrganisationDataExportBuildJob>();
                await job.RunAsync(exportId, companyId, CompanyAdmin, CancellationToken.None);
            }

            string storageKey;
            long fileSizeBytes;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var reloaded = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == exportId);
                Assert.Equal(OrganisationDataExport.StatusCompleted, reloaded.Status);
                Assert.NotNull(reloaded.StorageKey);
                Assert.NotNull(reloaded.FileSizeBytes);
                storageKey = reloaded.StorageKey!;
                fileSizeBytes = reloaded.FileSizeBytes!.Value;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportStorage>();
                await using var archive = await storage.OpenAsync(storageKey, CancellationToken.None);
                Assert.NotNull(archive);

                using var buffer = new MemoryStream();
                await archive!.CopyToAsync(buffer);
                var bytes = buffer.ToArray();
                Assert.Equal(fileSizeBytes, bytes.Length);

                // A malformed archive throws here.
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                Assert.NotNull(zip.Entries);
            }
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await db.OrganisationDataExports.Where(e => e.CompanyId == companyId).ExecuteDeleteAsync();
        }
    }
}
