using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetApplication;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetApplicationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Application_With_Candidate_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(application.Id, result.Value!.Id);
        Assert.Equal("Emma", result.Value.CandidateFirstName);
        Assert.Equal("Clarke", result.Value.CandidateLastName);
        Assert.Equal("emma.clarke@example.com", result.Value.CandidateEmail);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Belongs_To_Different_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var otherVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.AddRange(vacancy, otherVacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = otherVacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_StageHistory_For_Freshly_Created_Application()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.StageHistory);
    }

    [Fact]
    public async Task HandleAsync_Returns_StageHistory_Ordered_Oldest_First_After_Transitions()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);

        var changedBy = Guid.NewGuid();
        var laterEntry = ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(), companyId, application.Id, ApplicationStatus.Screening, ApplicationStatus.InterviewScheduled,
            changedBy, "Scheduled first round.", Now.AddDays(2));
        var earlierEntry = ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(), companyId, application.Id, ApplicationStatus.Applied, ApplicationStatus.Screening,
            changedBy, "Passed CV screen.", Now.AddDays(1));
        db.ApplicationStageHistoryEntries.AddRange(laterEntry, earlierEntry);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [earlierEntry.Id, laterEntry.Id],
            result.Value!.StageHistory.Select(h => h.Id));
        Assert.Equal(ApplicationStatus.Applied, result.Value.StageHistory[0].PreviousStage);
        Assert.Equal(ApplicationStatus.Screening, result.Value.StageHistory[0].NewStage);
        Assert.Equal(changedBy, result.Value.StageHistory[0].ChangedByUserId);
        Assert.Equal("Passed CV screen.", result.Value.StageHistory[0].Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Source_And_Recruiter_AgencyName_When_Source_Is_ExternalRecruiter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now, ApplicationSource.ExternalRecruiter, recruiter.Id);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.ExternalRecruiter, result.Value!.Source);
        Assert.Equal(recruiter.Id, result.Value.SourceExternalRecruiterId);
        Assert.Equal("Acme Recruiting", result.Value.SourceExternalRecruiterAgencyName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Source_Fields_When_Source_Was_Never_Set()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Source);
        Assert.Null(result.Value.SourceExternalRecruiterId);
        Assert.Null(result.Value.SourceExternalRecruiterAgencyName);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
