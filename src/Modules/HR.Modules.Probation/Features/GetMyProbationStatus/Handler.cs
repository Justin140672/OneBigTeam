using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetMyProbationStatus;

internal sealed class GetMyProbationStatusHandler(ProbationDbContext dbContext)
{
    public async Task<GetMyProbationStatusResponse> HandleAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var record = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
            return new GetMyProbationStatusResponse(false, null, null, null, null, null, null);

        return new GetMyProbationStatusResponse(
            true,
            record.Id,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString(),
            record.DecisionDate,
            record.OutcomeNotes);
    }
}
