using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetExternalRecruiterActivitySummaryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetExternalRecruiterActivitySummaryHandler(db).HandleAsync(
            new GetExternalRecruiterActivitySummaryRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Splits_Current_And_Previous_Vacancies_By_Vacancy_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var currentVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        var previousVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Frontend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        previousVacancy.Open(Now, new DateOnly(2026, 1, 1));
        previousVacancy.Close(Now, new DateOnly(2026, 2, 1));
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.AddRange(currentVacancy, previousVacancy);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterActivitySummaryHandler(db).HandleAsync(
            new GetExternalRecruiterActivitySummaryRequest(companyId, recruiter.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.CurrentVacancies);
        Assert.Equal(currentVacancy.Id, result.Value.CurrentVacancies[0].VacancyId);
        Assert.Single(result.Value.PreviousVacancies);
        Assert.Equal(previousVacancy.Id, result.Value.PreviousVacancies[0].VacancyId);
    }

    [Fact]
    public async Task HandleAsync_Reassigned_Away_Before_Closing_Vacancy_Does_Not_Appear_In_Current_Or_Previous()
    {
        // Ticket #81 behaviour change documented in GetExternalRecruiterActivitySummaryHandler: unlike
        // the old VacancyRecruiterAssignment history model, there is no longer any record of a recruiter
        // that was once assigned but was reassigned/cleared before the vacancy reached a terminal
        // status. Such a vacancy simply vanishes from this recruiter's summary entirely.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var replacementRecruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Beta Talent", null, null, null, null, null, Now);

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        vacancy.Open(Now, new DateOnly(2026, 1, 1));

        // Reassigned to a different recruiter before the vacancy closes.
        vacancy.AssignRecruiter(replacementRecruiter.Id, Now);
        vacancy.Close(Now, new DateOnly(2026, 2, 1));

        db.ExternalRecruiters.AddRange(recruiter, replacementRecruiter);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterActivitySummaryHandler(db).HandleAsync(
            new GetExternalRecruiterActivitySummaryRequest(companyId, recruiter.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.CurrentVacancies);
        Assert.Empty(result.Value.PreviousVacancies);

        // Meanwhile the replacement recruiter (the one still assigned at closing time) does pick it
        // up as a previous vacancy, confirming the vacancy itself wasn't dropped, only the original
        // recruiter's historical link to it.
        var replacementResult = await new GetExternalRecruiterActivitySummaryHandler(db).HandleAsync(
            new GetExternalRecruiterActivitySummaryRequest(companyId, replacementRecruiter.Id),
            CancellationToken.None);

        Assert.True(replacementResult.IsSuccess);
        Assert.Empty(replacementResult.Value!.CurrentVacancies);
        Assert.Single(replacementResult.Value.PreviousVacancies);
        Assert.Equal(vacancy.Id, replacementResult.Value.PreviousVacancies[0].VacancyId);
    }

    [Fact]
    public async Task HandleAsync_Counts_Candidates_Introduced_And_Hired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var otherRecruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Beta Talent", null, null, null, null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate1 = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma@example.com", null, null, Now);
        var candidate2 = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam@example.com", null, null, Now);
        var candidate3 = Candidate.Create(Guid.NewGuid(), companyId, "Nina", "Patel", "nina@example.com", null, null, Now);

        var introducedApplication = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate1.Id, stages.ApplicationReceived.Id, null, Now, ApplicationSource.ExternalRecruiter, recruiter.Id);
        var hiredApplication = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate2.Id, stages.Hired.Id, null, Now, ApplicationSource.ExternalRecruiter, recruiter.Id);
        var otherRecruiterApplication = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate3.Id, stages.ApplicationReceived.Id, null, Now, ApplicationSource.ExternalRecruiter, otherRecruiter.Id);

        db.ExternalRecruiters.AddRange(recruiter, otherRecruiter);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidate1, candidate2, candidate3);
        db.Applications.AddRange(introducedApplication, hiredApplication, otherRecruiterApplication);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterActivitySummaryHandler(db).HandleAsync(
            new GetExternalRecruiterActivitySummaryRequest(companyId, recruiter.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CandidatesIntroducedCount);
        Assert.Equal(1, result.Value.CandidatesHiredCount);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
