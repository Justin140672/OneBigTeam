using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 6: the <c>isInternal</c> filter on RecruitmentReportReader's
/// GetByVacancyAsync / GetByRecruiterAsync (IRecruitmentPipelineReader) and
/// GetVacancyPerformanceAsync (IVacancyPerformanceReader). GetSummaryAsync's filter is covered in
/// RecruitmentReportReaderGetSummaryAsyncTests. Application.Source == Internal is the only internal
/// indicator — a hired external candidate (Candidate.EmployeeId set) is counted as external.
/// </summary>
public class RecruitmentReportReaderInternalFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Seed(Guid CompanyId, Guid VacancyId, Guid RecruiterId);

    private static async Task<Seed> SeedAsync(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Platform Engineer", null, Guid.NewGuid(), Now, recruiterId);
        vacancy.Open(Now, new DateOnly(2026, 1, 1));
        db.Vacancies.Add(vacancy);

        var (_, a1) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.Offer.Id, Guid.NewGuid(), Now);
        InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, Guid.NewGuid(), Now, "Tom", "Baker");
        var (_, e1) = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.Interview.Id, ApplicationSource.Direct, Now);
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, null, Now, "Noah", "Patel");
        var (_, e3) = InternalApplicationTestData.AddHiredExternal(db, companyId, vacancy.Id, stages.Hired.Id, ApplicationSource.Direct, Guid.NewGuid(), Now);

        db.Interviews.AddRange(
            Interview.Create(Guid.NewGuid(), companyId, a1.Id, Guid.NewGuid(), Now.AddDays(1), 30, null, Now),
            Interview.Create(Guid.NewGuid(), companyId, e1.Id, Guid.NewGuid(), Now.AddDays(1), 30, null, Now),
            Interview.Create(Guid.NewGuid(), companyId, e1.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now),
            Interview.Create(Guid.NewGuid(), companyId, e3.Id, Guid.NewGuid(), Now.AddDays(1), 30, null, Now));

        db.ApplicationStageHistoryEntries.AddRange(
            ApplicationStageHistoryEntry.Create(Guid.NewGuid(), companyId, a1.Id, stages.Interview.Id, stages.Offer.Id, null, null, Now.AddDays(3)),
            ApplicationStageHistoryEntry.Create(Guid.NewGuid(), companyId, e3.Id, stages.Interview.Id, stages.Offer.Id, null, null, Now.AddDays(3)),
            ApplicationStageHistoryEntry.Create(Guid.NewGuid(), companyId, e3.Id, stages.Offer.Id, stages.Hired.Id, null, null, Now.AddDays(5)));

        await db.SaveChangesAsync();
        return new Seed(companyId, vacancy.Id, recruiterId);
    }

    private static RecruitmentReportReader Reader(RecruitmentDbContext db) => new(db, new FakePositionProfileReader());


    [Theory]
    [InlineData(null, 5, 4, 2, 1)]
    [InlineData(true, 2, 1, 1, 0)]
    [InlineData(false, 3, 3, 1, 1)]
    public async Task GetByVacancyAsync_Applies_IsInternal_Filter(bool? isInternal, int candidates, int interviews, int offers, int hires)
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db);

        var rows = await Reader(db).GetByVacancyAsync(seed.CompanyId, null, null, isInternal, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(seed.VacancyId, row.VacancyId);
        Assert.Equal(candidates, row.Candidates);
        Assert.Equal(interviews, row.Interviews);
        Assert.Equal(offers, row.Offers);
        Assert.Equal(hires, row.Hires);
    }

    [Fact]
    public async Task GetByVacancyAsync_IsInternal_True_Omits_Vacancy_With_Only_External_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "External Only", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        InternalApplicationTestData.AddHiredExternal(db, companyId, vacancy.Id, stages.Hired.Id, ApplicationSource.Direct, Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        var internalRows = await Reader(db).GetByVacancyAsync(companyId, null, null, true, CancellationToken.None);
        var externalRows = await Reader(db).GetByVacancyAsync(companyId, null, null, false, CancellationToken.None);

        Assert.Empty(internalRows);
        Assert.Equal(1, Assert.Single(externalRows).Candidates);
    }


    [Theory]
    [InlineData(null, 5, 4, 2, 1)]
    [InlineData(true, 2, 1, 1, 0)]
    [InlineData(false, 3, 3, 1, 1)]
    public async Task GetByRecruiterAsync_Applies_IsInternal_Filter(bool? isInternal, int candidates, int interviews, int offers, int hires)
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db);

        var rows = await Reader(db).GetByRecruiterAsync(seed.CompanyId, null, null, isInternal, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(seed.RecruiterId, row.RecruiterId);
        Assert.Equal(1, row.Vacancies);
        Assert.Equal(candidates, row.Candidates);
        Assert.Equal(interviews, row.Interviews);
        Assert.Equal(offers, row.Offers);
        Assert.Equal(hires, row.Hires);
    }


    [Theory]
    [InlineData(null, 5, 4, 2)]
    [InlineData(true, 2, 1, 1)]
    [InlineData(false, 3, 3, 1)]
    public async Task GetVacancyPerformanceAsync_Applies_IsInternal_Filter(bool? isInternal, int candidates, int interviews, int offers)
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db);

        var items = await Reader(db).GetVacancyPerformanceAsync(seed.CompanyId, null, null, isInternal, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(seed.VacancyId, item.VacancyId);
        Assert.Equal(candidates, item.CandidateCount);
        Assert.Equal(interviews, item.InterviewCount);
        Assert.Equal(offers, item.OfferCount);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task GetVacancyPerformanceAsync_HireDate_Comes_Only_From_Hired_External_Application(bool? isInternal, bool expectHireDate)
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db);

        var item = Assert.Single(await Reader(db).GetVacancyPerformanceAsync(seed.CompanyId, null, null, isInternal, CancellationToken.None));

        if (expectHireDate)
            Assert.Equal(DateOnly.FromDateTime(Now.AddDays(5).UtcDateTime), item.HireDate);
        else
            Assert.Null(item.HireDate);
    }

    [Fact]
    public async Task GetVacancyPerformanceAsync_IsInternal_True_Keeps_Vacancy_With_Zero_Counts_When_No_Internal_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "External Only", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, null, Now);
        await db.SaveChangesAsync();

        var item = Assert.Single(await Reader(db).GetVacancyPerformanceAsync(companyId, null, null, true, CancellationToken.None));

        Assert.Equal(vacancy.Id, item.VacancyId);
        Assert.Equal(0, item.CandidateCount);
        Assert.Equal(0, item.InterviewCount);
        Assert.Equal(0, item.OfferCount);
        Assert.Null(item.HireDate);
    }


    [Fact]
    public async Task GetByVacancyAsync_IsInternal_Filter_Combines_With_Date_Range()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Platform Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, Guid.NewGuid(), Now);
        InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, Guid.NewGuid(), new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "Tom", "Baker");
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, ApplicationSource.Direct, Now);
        await db.SaveChangesAsync();

        var rows = await Reader(db).GetByVacancyAsync(companyId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), true, CancellationToken.None);

        Assert.Equal(1, Assert.Single(rows).Candidates);
    }
}
