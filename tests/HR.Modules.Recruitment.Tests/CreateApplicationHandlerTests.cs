using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateApplicationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_Application_On_First_Active_NonTerminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value!.CurrentStageId);
        Assert.Equal(vacancy.Id, result.Value.VacancyId);
        Assert.Equal(candidate.Id, result.Value.CandidateId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Seeds_Default_Stages_When_Company_Has_None_Yet()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = Guid.NewGuid(), CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Applied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Source_Direct_And_No_Recruiter_Id()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.Direct,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.Direct, result.Value!.Source);
        Assert.Null(result.Value.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Source_ExternalRecruiter_And_Valid_Recruiter_Id()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.ExternalRecruiter, result.Value!.Source);
        Assert.Equal(recruiter.Id, result.Value.SourceExternalRecruiterId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(recruiter.Id, saved.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Source_ExternalRecruiter_But_Recruiter_Id_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), otherCompanyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static CreateApplicationHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new Infrastructure.FakeAuditPublisher(), new RecruitmentStageSeeder(db));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
