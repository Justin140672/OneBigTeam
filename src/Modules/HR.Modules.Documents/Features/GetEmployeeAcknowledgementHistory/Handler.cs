using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;

internal sealed class GetEmployeeAcknowledgementHistoryHandler(DocumentsDbContext db)
{
    public async Task<Result<GetEmployeeAcknowledgementHistoryResponse>> HandleAsync(
        GetEmployeeAcknowledgementHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var items = await db.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId && a.EmployeeId == request.EmployeeId)
            .Join(
                db.SharedCompanyDocuments.AsNoTracking(),
                a => a.SharedCompanyDocumentId,
                d => d.Id,
                (a, d) => new { d.Title, a.VersionNumber, a.AcknowledgedAt })
            .OrderByDescending(i => i.AcknowledgedAt)
            .Select(i => new GetEmployeeAcknowledgementHistoryItem(
                i.Title,
                i.VersionNumber,
                i.AcknowledgedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetEmployeeAcknowledgementHistoryResponse(items));
    }
}
