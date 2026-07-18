using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetUpcomingInterviews;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetUpcomingInterviewsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Only_Future_Pending_Interviews()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);

        var futurePending = Interview.Create(
            Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, "Room 1", Now);
        var pastPending = Interview.Create(
            Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(-2), 30, "Room 2", Now);
        var futureCancelled = Interview.Create(
            Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(3), 30, "Room 3", Now);
        futureCancelled.Cancel(Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.AddRange(futurePending, pastPending, futureCancelled);
        await db.SaveChangesAsync();

        var handler = new GetUpcomingInterviewsHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetUpcomingInterviewsRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(futurePending.Id, item.InterviewId);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_ScheduledAt_Ascending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);

        var later = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(5), 30, null, Now);
        var soonest = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(1), 30, null, Now);
        var middle = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(3), 30, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.AddRange(later, soonest, middle);
        await db.SaveChangesAsync();

        var handler = new GetUpcomingInterviewsHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetUpcomingInterviewsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(
            [soonest.Id, middle.Id, later.Id],
            result.Items.Select(i => i.InterviewId).ToArray());
    }

    [Fact]
    public async Task HandleAsync_Projects_Candidate_And_Vacancy_Names()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(1), 45, "Zoom", Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var handler = new GetUpcomingInterviewsHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetUpcomingInterviewsRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Olivia Grant", item.CandidateName);
        Assert.Equal("Product Designer", item.VacancyTitle);
        Assert.Equal(candidate.Id, item.CandidateId);
        Assert.Equal(vacancy.Id, item.VacancyId);
        Assert.Equal(application.Id, item.ApplicationId);
        Assert.Equal("Zoom", item.Location);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Interviews_For_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Noah", "Patel", "noah.patel@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), otherCompanyId, application.Id, Guid.NewGuid(), Now.AddDays(1), 30, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var handler = new GetUpcomingInterviewsHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetUpcomingInterviewsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Caps_Results_At_Fifteen()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Ava", "Bell", "ava.bell@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        for (var i = 1; i <= 20; i++)
            db.Interviews.Add(Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(i), 30, null, Now));
        await db.SaveChangesAsync();

        var handler = new GetUpcomingInterviewsHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetUpcomingInterviewsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(15, result.Items.Count);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
