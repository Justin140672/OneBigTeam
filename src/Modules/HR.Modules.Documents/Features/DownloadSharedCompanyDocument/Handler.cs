using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocument;

internal sealed class DownloadSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IEmployeeAudienceReader audienceReader)
{
    public async Task<Result<Uri>> HandleAsync(
        DownloadSharedCompanyDocumentRequest request,
        Guid callerEmployeeId,
        bool callerCanManage,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<Uri>(Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        // HR (shared-document:manage) can always download, including Draft/Archived versions —
        // everyone else needs the document Published and within their audience, same rule as
        // GetPublishedSharedCompanyDocument.
        if (!callerCanManage)
        {
            if (document.Status != SharedCompanyDocumentStatus.Published)
                return Result.Failure<Uri>(Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

            var (myDepartmentId, myLocationId) = await audienceReader.GetEmployeeAudienceAsync(
                request.CompanyId, callerEmployeeId, cancellationToken);

            var inAudience =
                (document.AudienceDepartmentId is null && document.AudienceLocationId is null) ||
                (document.AudienceDepartmentId is not null && document.AudienceDepartmentId == myDepartmentId) ||
                (document.AudienceLocationId is not null && document.AudienceLocationId == myLocationId);

            if (!inAudience)
                return Result.Failure<Uri>(Error.NotFound($"Shared document '{request.DocumentId}' was not found."));
        }

        var url = await storage.GetDownloadUrlAsync(document.CurrentFileReference, cancellationToken);
        return Result.Success(url);
    }
}
