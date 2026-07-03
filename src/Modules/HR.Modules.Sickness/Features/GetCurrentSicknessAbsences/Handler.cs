using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetCurrentSicknessAbsences;

internal sealed class GetCurrentSicknessAbsencesHandler(SicknessDbContext dbContext)
{
    public async Task<GetCurrentSicknessAbsencesResponse> HandleAsync(
        GetCurrentSicknessAbsencesRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.Status == SicknessStatus.Active)
            .OrderBy(r => r.StartDate)
            .Select(r => new CurrentSicknessAbsenceItem(
                r.Id,
                r.EmployeeId,
                r.CategoryId,
                r.StartDate,
                r.EvidenceStatus.ToString()))
            .ToListAsync(cancellationToken);

        return new GetCurrentSicknessAbsencesResponse(items);
    }
}
