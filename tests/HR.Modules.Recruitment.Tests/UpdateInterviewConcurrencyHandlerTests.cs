using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 2 (optimistic concurrency rollout): Interview.Version coverage for UpdateInterview. See
// UpdateCandidateConcurrencyHandlerTests for the two-context InMemory approach. UpdateInterview
// publishes no audit/integration events, so the stale path only asserts nothing was committed.
public class UpdateInterviewConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, "Remote", Now);
        seed.Vacancies.Add(vacancy);
        seed.Candidates.Add(candidate);
        seed.Applications.Add(application);
        seed.Interviews.Add(interview);
        await seed.SaveChangesAsync();
        return (dbName, companyId, vacancy.Id, application.Id, interview.Id);
    }

    private static UpdateInterviewRequest Request(
        Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId, int? expectedVersion, int duration)
        => new()
        {
            CompanyId = companyId,
            VacancyId = vacancyId,
            ApplicationId = applicationId,
            InterviewId = interviewId,
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt = Now.AddDays(4),
            DurationMinutes = duration,
            Location = "Office - Room 3",
            ExpectedVersion = expectedVersion,
        };

    private static UpdateInterviewHandler Handler(RecruitmentDbContext db)
        => new(db, new FakeClock(FixedUtcNow));

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, vacancyId, applicationId, interviewId) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db)
            .HandleAsync(Request(companyId, vacancyId, applicationId, interviewId, expectedVersion: 1, duration: 45), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Interviews.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, vacancyId, applicationId, interviewId) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db)
            .HandleAsync(Request(companyId, vacancyId, applicationId, interviewId, expectedVersion: null, duration: 75), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Interviews.SingleAsync();
        Assert.Equal(30, saved.DurationMinutes);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId, vacancyId, applicationId, interviewId) = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(dbName));
        await ctxA.Interviews.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB)
                .HandleAsync(Request(companyId, vacancyId, applicationId, interviewId, expectedVersion: 1, duration: 90), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await Handler(ctxA)
            .HandleAsync(Request(companyId, vacancyId, applicationId, interviewId, expectedVersion: 1, duration: 15), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Interviews.SingleAsync();
        Assert.Equal(90, saved.DurationMinutes);
        Assert.Equal(2, saved.Version);
    }
}
