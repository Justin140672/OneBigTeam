using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetTeamSicknessToday;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetTeamSicknessTodayHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Records_For_Direct_Reports_Only()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var nonReportId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var reportRecord = SicknessRecord.Create(
            Guid.NewGuid(), companyId, reportId, categoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, Now);
        var nonReportRecord = SicknessRecord.Create(
            Guid.NewGuid(), companyId, nonReportId, categoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, Now);
        db.SicknessRecords.AddRange(reportRecord, nonReportRecord);
        await db.SaveChangesAsync();

        var handler = new GetTeamSicknessTodayHandler(db, new FakeDirectReportsReader(reportId));
        var result = await handler.HandleAsync(
            new GetTeamSicknessTodayRequest(companyId, managerId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(reportId, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Manager_Has_No_Direct_Reports()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var handler = new GetTeamSicknessTodayHandler(db, new FakeDirectReportsReader());
        var result = await handler.HandleAsync(
            new GetTeamSicknessTodayRequest(companyId, managerId), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
