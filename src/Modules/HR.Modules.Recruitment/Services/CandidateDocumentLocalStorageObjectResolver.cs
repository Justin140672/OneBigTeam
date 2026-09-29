using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class CandidateDocumentLocalStorageObjectResolver(RecruitmentDbContext db) : ILocalStorageObjectResolver
{
    public string Bucket => LocalStorageBuckets.CandidateDocuments;

    public Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken) =>
        db.CandidateDocuments.AsNoTracking()
            .AnyAsync(cd => cd.StorageKey == storageKey && cd.ScanStatus == CandidateDocumentScanStatus.Clean, cancellationToken);
}
