using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

internal sealed class GetEmployeeDocumentVersionHistoryHandler(DocumentsDbContext db)
{
    public async Task<Result<GetEmployeeDocumentVersionHistoryResponse>> HandleAsync(
        GetEmployeeDocumentVersionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d in db.Documents.AsNoTracking() on ed.DocumentId equals d.Id
            where ed.CompanyId == request.CompanyId && ed.EmployeeId == request.EmployeeId
            select new
            {
                ed.Id,
                ed.PreviousVersionId,
                ed.IsLatestVersion,
                ed.AddedBy,
                ed.CreatedAt,
                ed.IssueDate,
                ed.ExpiryDate,
                ed.IsArchived,
                d.FileName,
                d.FileSize,
            }
        ).ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(r => r.Id);

        if (!byId.TryGetValue(request.EmployeeDocumentId, out var anchor))
            return Result.Failure<GetEmployeeDocumentVersionHistoryResponse>(
                Error.NotFound("Employee document was not found."));

        var current = anchor;
        while (current.PreviousVersionId is Guid previousId && byId.TryGetValue(previousId, out var previous))
            current = previous;

        var chain = new List<EmployeeDocumentVersionHistoryItem>();
        var node = current;
        while (true)
        {
            chain.Add(new EmployeeDocumentVersionHistoryItem(
                node.Id,
                node.PreviousVersionId,
                node.IsLatestVersion,
                node.FileName,
                node.FileSize,
                node.AddedBy,
                node.CreatedAt,
                node.IssueDate,
                node.ExpiryDate,
                node.IsArchived));

            var next = rows.FirstOrDefault(r => r.PreviousVersionId == node.Id);
            if (next is null)
                break;

            node = next;
        }

        chain.Reverse();

        return Result.Success(new GetEmployeeDocumentVersionHistoryResponse(chain));
    }
}
