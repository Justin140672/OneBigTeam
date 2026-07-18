using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class OfferCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Offers_Interviewed_Application()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Offered, result.Value!.Status);
        Assert.Equal(vacancy.PositionProfileId, result.Value.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_PositionProfile_EmploymentDefaults_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var leavePolicyId = Guid.NewGuid();
        var employmentDefaults = new Dictionary<Guid, PositionProfileEmploymentDefaults>
        {
            [positionProfileId] = new(
                positionProfileId,
                "Senior Software Engineer",
                SalaryMin: 60000m,
                SalaryMax: 80000m,
                SalaryType: "Annual",
                WorkingDaysOverride: WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
                HoursPerDayOverride: 7.5m,
                ProbationMonthsOverride: 3,
                DefaultLeavePolicyId: leavePolicyId,
                LocationId: Guid.NewGuid(),
                LocationName: "London"),
        };

        var result = await handler(db, new FakePositionProfileReader(employmentDefaults: employmentDefaults)).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        Assert.Equal("Senior Software Engineer", result.Value.PositionProfileTitle);
        Assert.Equal(60000m, result.Value.SalaryMin);
        Assert.Equal(80000m, result.Value.SalaryMax);
        Assert.Equal("Annual", result.Value.SalaryType);
        Assert.Equal(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
            result.Value.WorkingDaysOverride);
        Assert.Equal(7.5m, result.Value.HoursPerDayOverride);
        Assert.Equal(3, result.Value.ProbationMonthsOverride);
        Assert.Equal(leavePolicyId, result.Value.DefaultLeavePolicyId);
        Assert.Equal("London", result.Value.LocationName);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Null_EmploymentDefaults_When_PositionProfile_Not_Resolvable()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        // No employmentDefaults dictionary supplied — the linked profile cannot be resolved. Offering
        // still succeeds; only the informational fields are null.
        var result = await handler(db, new FakePositionProfileReader()).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PositionProfileTitle);
        Assert.Null(result.Value.SalaryMin);
        Assert.Null(result.Value.SalaryMax);
        Assert.Null(result.Value.SalaryType);
        Assert.Null(result.Value.WorkingDaysOverride);
        Assert.Null(result.Value.HoursPerDayOverride);
        Assert.Null(result.Value.ProbationMonthsOverride);
        Assert.Null(result.Value.DefaultLeavePolicyId);
        Assert.Null(result.Value.LocationName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Nair", "priya.nair@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancyId, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Not_Interviewed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static OfferCandidateHandler handler(RecruitmentDbContext db, FakePositionProfileReader? positionProfileReader = null) =>
        new(db, new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
