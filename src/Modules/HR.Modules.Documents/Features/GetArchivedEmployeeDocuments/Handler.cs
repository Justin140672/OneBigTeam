using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;

// DOC-04: HR-only view of archived (soft-deleted) employee documents — the mirror of
// ListEmployeeDocuments but scoped to IsArchived == true instead of excluding it.
internal sealed class GetArchivedEmployeeDocumentsHandler(DocumentsDbContext db)
{
    public async Task<Result<GetArchivedEmployeeDocumentsResponse>> HandleAsync(
        GetArchivedEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId  == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
               && ed.IsArchived
            orderby ed.ArchivedAt descending
            select new ArchivedEmployeeDocumentListItem(
                ed.Id,
                d.Title,
                dt.Name,
                ed.ArchivedByUserId!.Value,
                ed.ArchivedAt!.Value,
                ed.ArchiveReason,
                ed.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetArchivedEmployeeDocumentsResponse(items));
    }
}
