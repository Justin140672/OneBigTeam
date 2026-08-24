using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class GetMissingFitNotesHandler(SicknessDbContext dbContext)
{
    public async Task<GetMissingFitNotesResponse> HandleAsync(
        GetMissingFitNotesRequest request,
        IReadOnlySet<Guid>? authorizedEmployeeIds,
        CancellationToken cancellationToken)
    {
        // authorizedEmployeeIds is null for HR Administrators (company-wide, unrestricted).
        // For managers it is their full reporting hierarchy — resolved server-side by the
        // endpoint via SicknessResourceAuthorizer, never trusted from the client (SICK-02).
        if (authorizedEmployeeIds is not null && authorizedEmployeeIds.Count == 0)
            return new GetMissingFitNotesResponse([]);

        var items = await (
            from evidenceRequest in dbContext.SicknessEvidenceRequests
            join record in dbContext.SicknessRecords on evidenceRequest.SicknessRecordId equals record.Id
            where evidenceRequest.CompanyId == request.CompanyId
               && (evidenceRequest.Status == SicknessEvidenceRequestStatus.Pending
                   || evidenceRequest.Status == SicknessEvidenceRequestStatus.Overdue)
               && (authorizedEmployeeIds == null || authorizedEmployeeIds.Contains(record.EmployeeId))
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
