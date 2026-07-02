using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class RecordSicknessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    // Mon–Fri, 7.5h/day
    private static readonly WorkingPattern DefaultPattern = WorkingPattern.Default;

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static RecordSicknessHandler BuildHandler(
        SicknessDbContext db,
        WorkingPattern? pattern = null,
        bool excludePublicHolidays = false,
        IReadOnlyCollection<DateOnly>? publicHolidays = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(pattern ?? DefaultPattern),
            new FakeCompanySicknessSettingsReader(excludePublicHolidays),
            new FakePublicHolidayReader(publicHolidays));

    [Fact]
    public async Task HandleAsync_Creates_SicknessRecord_With_No_EndDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay,
            Notes = "Feeling unwell"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.Equal(SicknessStatus.Active, result.Value.Status);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(SicknessDayPart.FullDay, result.Value.StartDayPart);
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result.Value.EvidenceStatus);
        Assert.Equal("Feeling unwell", result.Value.Notes);

        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(employeeId, saved.EmployeeId);
        Assert.Null(saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_TotalDays_Is_Null_When_No_EndDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Null(saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_For_FullDay_Single_Day()
    {
        // 2026-07-01 is a Wednesday (working day)
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 1),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(1m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_For_HalfDay()
    {
        // 2026-07-01 is a Wednesday
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.HalfDayAM,
            EndDate = new DateOnly(2026, 7, 1),
            EndDayPart = SicknessDayPart.HalfDayAM
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(0.5m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_Across_Multiple_Working_Days()
    {
        // 2026-07-01 (Wed) to 2026-07-03 (Fri) = 3 working days
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(3m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Weekend_Days_From_TotalDays()
    {
        // 2026-07-01 (Wed) to 2026-07-06 (Mon) = 4 working days (Wed, Thu, Fri, Mon)
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 6),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(4m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holidays_When_Setting_Is_Enabled()
    {
        // 2026-07-01 (Wed) to 2026-07-03 (Fri) = 3 working days, but 2026-07-02 is a public holiday
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, excludePublicHolidays: true, publicHolidays: publicHolidays)
            .HandleAsync(new RecordSicknessRequest
            {
                CompanyId = companyId,
                EmployeeId = Guid.NewGuid(),
                CategoryId = categoryId,
                StartDate = new DateOnly(2026, 7, 1),
                StartDayPart = SicknessDayPart.FullDay,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(2m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Exclude_Public_Holidays_When_Setting_Is_Disabled()
    {
        // Setting disabled: public holidays still count
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, excludePublicHolidays: false, publicHolidays: publicHolidays)
            .HandleAsync(new RecordSicknessRequest
            {
                CompanyId = companyId,
                EmployeeId = Guid.NewGuid(),
                CategoryId = categoryId,
                StartDate = new DateOnly(2026, 7, 1),
                StartDayPart = SicknessDayPart.FullDay,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(3m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Respects_Custom_Working_Pattern()
    {
        // 4-day week (Mon–Thu), 8h/day
        var pattern = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
            8m);

        // 2026-07-01 (Wed) to 2026-07-03 (Fri) — Fri is not a working day
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, pattern: pattern).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(2m, saved.TotalDays); // Wed + Thu only
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.HalfDayAM
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expected = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        Assert.Equal(expected, result.Value!.CreatedAt);
        Assert.Equal(expected, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var categoryId = await SeedCategory(db, Guid.NewGuid()); // different company

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Null_Notes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.HalfDayPM,
            Notes = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Notes);
    }
}
