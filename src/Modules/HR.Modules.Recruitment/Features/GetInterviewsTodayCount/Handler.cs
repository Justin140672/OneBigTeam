using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInterviewsTodayCount;

internal sealed class GetInterviewsTodayCountHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<GetInterviewsTodayCountResponse>> HandleAsync(
        GetInterviewsTodayCountRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1);

        var count = await db.Interviews
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId
                     && i.ScheduledAt >= startOfDay
                     && i.ScheduledAt < endOfDay
                     && i.Outcome != InterviewOutcome.Cancelled)
            .CountAsync(cancellationToken);

        return Result.Success(new GetInterviewsTodayCountResponse(count));
    }
}
