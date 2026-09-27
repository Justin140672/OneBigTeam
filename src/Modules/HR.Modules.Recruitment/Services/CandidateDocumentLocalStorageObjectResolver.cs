using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Dev-only local delivery re-check for the <see cref="LocalStorageBuckets.CandidateDocuments"/> bucket.
/// A key is servable only while a CandidateDocument row still owns it and its malware scan is Clean,
/// the same record/scan predicate as DownloadCandidateDocumentHandler (which made the tenant and
/// permission decision before the URL was signed). Pending, Scanning, Infected and Failed documents,
/// and orphaned files with no row, are never served, even if the physical file exists.
/// </summary>
internal sealed class CandidateDocumentLocalStorageObjectResolver(RecruitmentDbContext db) : ILocalStorageObjectResolver
{
    public string Bucket => LocalStorageBuckets.CandidateDocuments;

    public Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken) =>
        db.CandidateDocuments.AsNoTracking()
            .AnyAsync(cd => cd.StorageKey == storageKey && cd.ScanStatus == CandidateDocumentScanStatus.Clean, cancellationToken);
}
