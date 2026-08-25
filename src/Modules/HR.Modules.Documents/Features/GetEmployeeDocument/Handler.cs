using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetEmployeeDocument;

internal sealed class GetEmployeeDocumentHandler(
    DocumentsDbContext db)
{
    public async Task<Result<GetEmployeeDocumentResponse>> HandleAsync(
        GetEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var row = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId      equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId   equals dt.Id
            where ed.Id         == request.EmployeeDocumentId
               && ed.CompanyId  == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
               && !ed.IsArchived
               && ed.IsLatestVersion
            select new { ed, d, dt }
        ).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<GetEmployeeDocumentResponse>(
                Error.NotFound("Employee document was not found."));

        return Result.Success(new GetEmployeeDocumentResponse(
            EmployeeDocumentId: row.ed.Id,
            DocumentId:         row.d.Id,
            CompanyId:          row.ed.CompanyId,
            EmployeeId:         row.ed.EmployeeId,
            Title:              row.d.Title,
            Description:        row.d.Description,
            FileName:           row.d.FileName,
            FileSize:           row.d.FileSize,
            ContentType:        row.d.ContentType,
            DocumentTypeId:     row.d.DocumentTypeId,
            DocumentTypeName:   row.dt.Name,
            Status:             row.d.Status,
            DocumentExpiryDate: row.d.ExpiryDate,
            UploadedBy:         row.d.UploadedBy,
            AddedBy:            row.ed.AddedBy,
            IssueDate:          row.ed.IssueDate,
            ExpiryDate:         row.ed.ExpiryDate,
            AcknowledgedAt:     row.ed.AcknowledgedAt,
            CreatedAt:          row.ed.CreatedAt));
    }
}
