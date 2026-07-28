using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetRecruitmentStageUsageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Stage_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_InUse_When_No_Applications_On_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(companyId, stages.Offer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InUse);
        Assert.Equal(0, result.Value.ActiveVacancyCount);
        Assert.Empty(result.Value.VacancyLabels);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Applications_On_Closed_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(companyId, stages.Offer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InUse);
        Assert.Equal(0, result.Value.ActiveVacancyCount);
    }

    [Fact]
    public async Task HandleAsync_Counts_Application_On_Draft_Vacancy() =>
        await AssertCountsApplicationOnActiveVacancyAsync(VacancyStatus.Draft);

    [Fact]
    public async Task HandleAsync_Counts_Application_On_Open_Vacancy() =>
        await AssertCountsApplicationOnActiveVacancyAsync(VacancyStatus.Open);

    [Fact]
    public async Task HandleAsync_Counts_Application_On_OnHold_Vacancy() =>
        await AssertCountsApplicationOnActiveVacancyAsync(VacancyStatus.OnHold);

    // Not a [Theory] — VacancyStatus is internal to HR.Modules.Recruitment and cannot be used as
    // a public test-method parameter (xUnit discovery requires public parameter types), so each
    // status is exercised via its own thin [Fact] wrapper above instead.
    private async Task AssertCountsApplicationOnActiveVacancyAsync(VacancyStatus status)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        if (status is VacancyStatus.Open or VacancyStatus.OnHold)
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        if (status == VacancyStatus.OnHold)
            vacancy.Hold(Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(companyId, stages.Offer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.InUse);
        Assert.Equal(1, result.Value.ActiveVacancyCount);
        Assert.Single(result.Value.VacancyLabels);
        Assert.Equal("Backend Engineer", result.Value.VacancyLabels[0]);
    }

    [Fact]
    public async Task HandleAsync_Counts_Multiple_Vacancies_And_Caps_Labels_At_Five()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);

        for (var i = 0; i < 7; i++)
        {
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), $"Role {i}", null, Guid.NewGuid(), Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
            db.Vacancies.Add(vacancy);
            db.Applications.Add(application);
        }
        await db.SaveChangesAsync();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(companyId, stages.Offer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.InUse);
        Assert.Equal(7, result.Value.ActiveVacancyCount);
        Assert.Equal(5, result.Value.VacancyLabels.Count);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Vacancy_Id_Label_When_AdvertTitle_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetRecruitmentStageUsageHandler(db).HandleAsync(
            new GetRecruitmentStageUsageRequest(companyId, stages.Offer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.VacancyLabels);
        Assert.Equal($"Vacancy {vacancy.Id.ToString()[..8]}", result.Value.VacancyLabels[0]);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
