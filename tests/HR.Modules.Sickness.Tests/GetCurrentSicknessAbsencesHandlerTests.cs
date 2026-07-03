using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetCurrentSicknessAbsences;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetCurrentSicknessAbsencesHandlerTests
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
    public async Task HandleAsync_Returns_Active_Records_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var handler = new GetCurrentSicknessAbsencesHandler(db);
        var result = await handler.HandleAsync(new GetCurrentSicknessAbsencesRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(record.Id, item.RecordId);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.Equal(StartDate, item.StartDate);
        Assert.Equal("Pending", item.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Closed_Records()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var closed = SicknessRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), categoryId, StartDate, SicknessDayPart.FullDay,
            StartDate.AddDays(2), SicknessDayPart.FullDay, 2m, null, SicknessEvidenceStatus.NotRequired, Now);
        db.SicknessRecords.Add(closed);
        await db.SaveChangesAsync();

        var handler = new GetCurrentSicknessAbsencesHandler(db);
        var result = await handler.HandleAsync(new GetCurrentSicknessAbsencesRequest(companyId), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Records_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCategoryId = await SeedCategory(db, otherCompanyId);

        var record = SicknessRecord.Create(
            Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), otherCategoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, Now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var handler = new GetCurrentSicknessAbsencesHandler(db);
        var result = await handler.HandleAsync(new GetCurrentSicknessAbsencesRequest(companyId), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
