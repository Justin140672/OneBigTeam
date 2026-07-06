using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetInterviewsTodayCount;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetInterviewsTodayCountHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Counts_Interviews_Scheduled_Today()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var todayMorning = new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);
        var todayEvening = new DateTimeOffset(2026, 7, 6, 16, 0, 0, TimeSpan.Zero);
        var interviewA = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), todayMorning, 30, null, Now);
        var interviewB = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), todayEvening, 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.AddRange(interviewA, interviewB);
        await db.SaveChangesAsync();

        var result = await new GetInterviewsTodayCountHandler(db, new FakeClock(FixedUtcNow)).HandleAsync(
            new GetInterviewsTodayCountRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Interviews_Scheduled_On_Other_Days()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var yesterday = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
        var tomorrow = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.AddRange(
            Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), yesterday, 30, null, Now),
            Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), tomorrow, 30, null, Now));
        await db.SaveChangesAsync();

        var result = await new GetInterviewsTodayCountHandler(db, new FakeClock(FixedUtcNow)).HandleAsync(
            new GetInterviewsTodayCountRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Cancelled_Interviews_Scheduled_Today()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", "noah.patel@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var todayAfternoon = new DateTimeOffset(2026, 7, 6, 14, 0, 0, TimeSpan.Zero);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), todayAfternoon, 30, null, Now);
        interview.Cancel(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var result = await new GetInterviewsTodayCountHandler(db, new FakeClock(FixedUtcNow)).HandleAsync(
            new GetInterviewsTodayCountRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Interviews_For_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, null, "Product Designer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, null, Now);
        var todayAfternoon = new DateTimeOffset(2026, 7, 6, 14, 0, 0, TimeSpan.Zero);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(Interview.Create(Guid.NewGuid(), otherCompanyId, application.Id, Guid.NewGuid(), todayAfternoon, 30, null, Now));
        await db.SaveChangesAsync();

        var result = await new GetInterviewsTodayCountHandler(db, new FakeClock(FixedUtcNow)).HandleAsync(
            new GetInterviewsTodayCountRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Value!.Count);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
