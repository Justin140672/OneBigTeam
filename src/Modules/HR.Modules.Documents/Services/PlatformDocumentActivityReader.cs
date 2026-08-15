using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Platform-wide counterpart to <see cref="DocumentStorageReader"/> — sums FileSize across the same
/// five document-bearing tables but without a company_id filter, and groups upload timestamps by UTC
/// date for the Admin Portal Application Metrics dashboard (Platform Monitoring epic). Skips
/// SharedCompanyDocumentVersions in the daily-upload grouping to avoid double counting revisions of
/// the same logical document.
/// </summary>
internal sealed class PlatformDocumentActivityReader(DocumentsDbContext dbContext) : IPlatformDocumentActivityReader
{
    public async Task<PlatformDocumentActivity> GetPlatformActivityAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var documents = await dbContext.Documents
            .AsNoTracking()
            .Select(d => new { d.FileSize, d.CreatedAt })
            .ToListAsync(cancellationToken);

        var sharedDocuments = await dbContext.SharedCompanyDocuments
            .AsNoTracking()
            .Select(d => new { d.FileSize, d.CreatedAt })
            .ToListAsync(cancellationToken);

        var sharedDocumentVersions = await dbContext.SharedCompanyDocumentVersions
            .AsNoTracking()
            .Select(v => v.FileSize)
            .ToListAsync(cancellationToken);

        var profilePhotos = await dbContext.EmployeeProfilePhotos
            .AsNoTracking()
            .Select(p => new { p.FileSize, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var pendingProfilePhotos = await dbContext.PendingProfilePhotos
            .AsNoTracking()
            .Select(p => new { p.FileSize, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var totalStorageBytes = documents.Sum(d => d.FileSize)
            + sharedDocuments.Sum(d => d.FileSize)
            + sharedDocumentVersions.Sum(v => v)
            + profilePhotos.Sum(p => p.FileSize)
            + pendingProfilePhotos.Sum(p => p.FileSize);

        var uploadDates = documents.Select(d => d.CreatedAt)
            .Concat(sharedDocuments.Select(d => d.CreatedAt))
            .Concat(profilePhotos.Select(p => p.CreatedAt))
            .Concat(pendingProfilePhotos.Select(p => p.CreatedAt))
            .Select(createdAt => DateOnly.FromDateTime(createdAt.UtcDateTime.Date))
            .Where(date => date >= fromDate && date <= toDate)
            .GroupBy(date => date)
            .Select(g => new DailyUploadCount(g.Key, g.Count()))
            .ToList();

        return new PlatformDocumentActivity(totalStorageBytes, uploadDates);
    }
}
