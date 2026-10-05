using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInterviewStageSuggestion;

internal sealed class GetInterviewStageSuggestionHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetInterviewStageSuggestionResponse>> HandleAsync(
        GetInterviewStageSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var stages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var plan = InterviewStagePlanner.Plan(stages);

        var activeInterviewCount = stages.Count(s =>
            s.IsActive && !s.IsTerminal && s.Purpose == RecruitmentStagePurpose.Interview);

        return Result.Success(new GetInterviewStageSuggestionResponse(
            plan.SuggestedName,
            plan.DisplayOrder,
            activeInterviewCount,
            plan.StageToRename?.Name,
            plan.StageToRename is null ? null : InterviewStagePlanner.OrdinalName(1)));
    }
}
