using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

/// <summary>
/// DOC-05: HR-only full version history for an employee document lineage — given ANY version's id
/// in the chain (not just the latest), returns every version (including the one passed in),
/// ordered newest-first. Mirrors GetArchivedEmployeeDocumentsHandler's shape/gating but scoped to a
/// single lineage rather than every archived document.
///
/// There is no separate "root document id"/"lineage id" column (see EmployeeDocument's DOC-05
/// comment) — the chain is walked in-memory via PreviousVersionId over the small set of
/// EmployeeDocument rows for this employee, which is simple and avoids inventing a second
/// lineage-tracking scheme not present in this entity's design.
/// </summary>
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

        // Walk back to the root of the lineage.
        var current = anchor;
        while (current.PreviousVersionId is Guid previousId && byId.TryGetValue(previousId, out var previous))
            current = previous;

        // Walk forward from the root, collecting every version in the chain.
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
