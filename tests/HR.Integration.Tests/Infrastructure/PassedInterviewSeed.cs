using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class PassedInterviewSeed
{
    public static Interview Create(Guid companyId, Guid applicationId, Guid stageId, DateTimeOffset now)
    {
        var interview = Interview.Create(Guid.NewGuid(), companyId, applicationId, Guid.NewGuid(), now.AddDays(-1), 30, null, now.AddDays(-2), stageId);
        interview.RecordOutcome(InterviewOutcome.Passed, null, now.AddDays(-1));
        return interview;
    }

    public static async Task AddForAllInterviewStagesAsync(ApiWebApplicationFactory factory, Guid companyId, Guid applicationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var stageIds = await db.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal && s.Purpose == RecruitmentStagePurpose.Interview)
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var stageId in stageIds)
            db.Interviews.Add(Create(companyId, applicationId, stageId, now));

        await db.SaveChangesAsync();
    }
}
