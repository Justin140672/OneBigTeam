using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetExternalRecruiterUsageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_InUse_When_No_Vacancies_Assigned()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(companyId, recruiter.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InUse);
        Assert.Equal(0, result.Value.ActiveVacancyCount);
        Assert.Empty(result.Value.VacancyLabels);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Closed_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(companyId, recruiter.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InUse);
        Assert.Equal(0, result.Value.ActiveVacancyCount);
    }

    [Fact]
    public async Task HandleAsync_Counts_Vacancy_Assigned_To_Recruiter_When_Draft() =>
        await AssertCountsVacancyAssignedToRecruiterAsync(VacancyStatus.Draft);

    [Fact]
    public async Task HandleAsync_Counts_Vacancy_Assigned_To_Recruiter_When_Open() =>
        await AssertCountsVacancyAssignedToRecruiterAsync(VacancyStatus.Open);

    [Fact]
    public async Task HandleAsync_Counts_Vacancy_Assigned_To_Recruiter_When_OnHold() =>
        await AssertCountsVacancyAssignedToRecruiterAsync(VacancyStatus.OnHold);

    // Not a [Theory] — VacancyStatus is internal to HR.Modules.Recruitment and cannot be used as
    // a public test-method parameter (xUnit discovery requires public parameter types), so each
    // status is exercised via its own thin [Fact] wrapper above instead.
    private async Task AssertCountsVacancyAssignedToRecruiterAsync(VacancyStatus status)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        if (status is VacancyStatus.Open or VacancyStatus.OnHold)
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        if (status == VacancyStatus.OnHold)
            vacancy.Hold(Now);
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(companyId, recruiter.Id),
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
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);

        for (var i = 0; i < 7; i++)
        {
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), $"Role {i}", null, Guid.NewGuid(), Now, recruiter.Id);
            db.Vacancies.Add(vacancy);
        }
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(companyId, recruiter.Id),
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
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), null, null, Guid.NewGuid(), Now, recruiter.Id);
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetExternalRecruiterUsageHandler(db).HandleAsync(
            new GetExternalRecruiterUsageRequest(companyId, recruiter.Id),
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
