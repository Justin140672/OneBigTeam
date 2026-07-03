using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class GetMissingFitNotesHandler(SicknessDbContext dbContext)
{
    public async Task<GetMissingFitNotesResponse> HandleAsync(
        GetMissingFitNotesRequest request,
        CancellationToken cancellationToken)
    {
        var items = await (
            from evidenceRequest in dbContext.SicknessEvidenceRequests
            join record in dbContext.SicknessRecords on evidenceRequest.SicknessRecordId equals record.Id
            where evidenceRequest.CompanyId == request.CompanyId
               && (evidenceRequest.Status == SicknessEvidenceRequestStatus.Pending
                   || evidenceRequest.Status == SicknessEvidenceRequestStatus.Overdue)
            orderby evidenceRequest.DueDate
            select new MissingFitNoteItem(
                evidenceRequest.Id,
                record.EmployeeId,
                evidenceRequest.SicknessRecordId,
                evidenceRequest.DueDate,
                evidenceRequest.Status.ToString()))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new GetMissingFitNotesResponse(items);
    }
}
