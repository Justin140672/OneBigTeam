using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetRecruitmentKanban;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetRecruitmentKanbanHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Columns_For_Each_Active_Stage_In_DisplayOrder_Even_When_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value!.Columns.Count);
        Assert.Equal(
            [stages.ApplicationReceived.Id, stages.CvReview.Id, stages.Interview.Id, stages.Offer.Id, stages.Hired.Id, stages.Rejected.Id],
            result.Value.Columns.Select(c => c.StageId));
        Assert.All(result.Value.Columns, c =>
        {
            Assert.Equal(0, c.Count);
            Assert.Empty(c.Applicants);
        });
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Stages_From_Columns()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        stages.Offer.SetActiveStatus(false, Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value!.Columns, c => c.StageId == stages.Offer.Id);
    }

    [Fact]
    public async Task HandleAsync_Groups_Applicants_Into_Correct_Stage_Column()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate1 = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var candidate2 = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate1.Id, stages.ApplicationReceived.Id, null, Now);
        var cvReview = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate2.Id, stages.CvReview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidate1, candidate2);
        db.Applications.AddRange(applied, cvReview);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var appliedColumn = result.Value!.Columns.Single(c => c.StageId == stages.ApplicationReceived.Id);
        var cvReviewColumn = result.Value.Columns.Single(c => c.StageId == stages.CvReview.Id);

        Assert.Equal(1, appliedColumn.Count);
        Assert.Equal(applied.Id, appliedColumn.Applicants.Single().ApplicationId);

        Assert.Equal(1, cvReviewColumn.Count);
        Assert.Equal(cvReview.Id, cvReviewColumn.Applicants.Single().ApplicationId);
    }

    [Fact]
    public async Task HandleAsync_Flags_Withdrawn_Applications_Without_A_Separate_Column()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value!.Columns.Count);
        var appliedColumn = result.Value.Columns.Single(c => c.StageId == stages.ApplicationReceived.Id);
        var applicant = Assert.Single(appliedColumn.Applicants);
        Assert.True(applicant.IsWithdrawn);
    }

    [Fact]
    public async Task HandleAsync_Column_Count_Matches_Applicants_Count()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        for (var i = 0; i < 3; i++)
        {
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "First" + i, "Last" + i, $"candidate{i}@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now.AddMinutes(i));
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
        }
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var appliedColumn = result.Value!.Columns.Single(c => c.StageId == stages.ApplicationReceived.Id);
        Assert.Equal(3, appliedColumn.Count);
        Assert.Equal(appliedColumn.Applicants.Count, appliedColumn.Count);
    }

    [Fact]
    public async Task HandleAsync_Orders_Applicants_Within_A_Column_By_AppliedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidateLater = Candidate.Create(Guid.NewGuid(), companyId, "Later", "Applicant", "later@example.com", null, null, Now);
        var candidateEarlier = Candidate.Create(Guid.NewGuid(), companyId, "Earlier", "Applicant", "earlier@example.com", null, null, Now);
        var applicationLater = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateLater.Id, stages.ApplicationReceived.Id, null, Now.AddDays(2));
        var applicationEarlier = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateEarlier.Id, stages.ApplicationReceived.Id, null, Now.AddDays(1));
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidateLater, candidateEarlier);
        db.Applications.AddRange(applicationLater, applicationEarlier);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var appliedColumn = result.Value!.Columns.Single(c => c.StageId == stages.ApplicationReceived.Id);
        Assert.Equal(
            [applicationEarlier.Id, applicationLater.Id],
            appliedColumn.Applicants.Select(a => a.ApplicationId));
    }

    [Fact]
    public async Task HandleAsync_Propagates_AssignedRecruiterId_And_VacancyTitle_Onto_Each_Applicant()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now, recruiterId);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = result.Value!.Columns.Single(c => c.StageId == stages.ApplicationReceived.Id).Applicants.Single();
        Assert.Equal(recruiterId, summary.AssignedRecruiterId);
        Assert.Equal("Senior Software Engineer", summary.VacancyTitle);
        Assert.Equal("Senior Software Engineer", result.Value.VacancyTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = otherCompanyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static GetRecruitmentKanbanHandler handler(RecruitmentDbContext db, IPositionProfileReader? positionProfileReader = null) =>
        new(db, positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
