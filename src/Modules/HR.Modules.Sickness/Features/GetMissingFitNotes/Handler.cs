using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class GetMissingFitNotesHandler(SicknessDbContext dbContext, IOpenTaskBySourceEntityReader taskReader)
{
    public async Task<GetMissingFitNotesResponse> HandleAsync(
        GetMissingFitNotesRequest request,
        IReadOnlySet<Guid>? authorizedEmployeeIds,
        CancellationToken cancellationToken)
    {
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

        if (items.Count == 0)
            return new GetMissingFitNotesResponse(items);

        var taskIds = await taskReader.GetOpenTaskIdsAsync(
            request.CompanyId, items.Select(i => i.RequestId), cancellationToken, TaskActionType.Upload);

        var withTasks = items
            .Select(i => i with { TaskId = taskIds.TryGetValue(i.RequestId, out var tid) ? tid : null })
            .ToList();

        return new GetMissingFitNotesResponse(withTasks);
    }
}
