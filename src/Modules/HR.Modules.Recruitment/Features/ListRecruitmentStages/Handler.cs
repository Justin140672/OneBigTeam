using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListRecruitmentStages;

internal sealed class ListRecruitmentStagesHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListRecruitmentStagesResponse>> HandleAsync(
        ListRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        var items = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new RecruitmentStageListItem(
                s.Id,
                s.Name,
                s.DisplayOrder,
                s.IsActive,
                s.IsTerminal,
                s.TerminalOutcome,
                s.Purpose,
                s.Version))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListRecruitmentStagesResponse(items));
    }
}
