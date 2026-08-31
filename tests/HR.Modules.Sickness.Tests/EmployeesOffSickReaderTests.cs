using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// DSH-05: <see cref="EmployeesOffSickReader"/> — an active (open) sickness record covers
/// <c>onDate</c> when its start date is on or before that date. A record captured ahead of the
/// absence actually starting, or one that has been closed, does not count.
/// </summary>
public class EmployeesOffSickReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OnDate = new(2026, 7, 1);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static void SeedRecord(
        SicknessDbContext db, Guid companyId, Guid employeeId, DateOnly startDate, DateOnly? endDate = null)
    {
        db.SicknessRecords.Add(SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), startDate, SicknessDayPart.FullDay,
            endDate, endDate is null ? null : SicknessDayPart.FullDay, null, null,
            SicknessEvidenceStatus.NotRequired, Now));
    }

    private static Task<IReadOnlySet<Guid>> Read(SicknessDbContext db, Guid companyId, params Guid[] ids) =>
        new EmployeesOffSickReader(db).GetOffSickEmployeeIdsAsync(companyId, ids, OnDate, CancellationToken.None);

    [Fact]
    public async Task Active_Record_Starting_Before_OnDate_Is_Included()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, OnDate.AddDays(-3));
        await db.SaveChangesAsync();

        Assert.Contains(employeeId, await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Active_Record_Starting_Exactly_On_OnDate_Is_Included()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, OnDate);
        await db.SaveChangesAsync();

        Assert.Contains(employeeId, await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Active_Record_Starting_The_Day_After_OnDate_Is_Excluded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, OnDate.AddDays(1));
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Closed_Record_Is_Excluded_Even_When_It_Covers_OnDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, OnDate.AddDays(-5), endDate: OnDate.AddDays(-1));
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Is_Scoped_By_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyB, employeeId, OnDate.AddDays(-1));
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyA, employeeId));
    }

    [Fact]
    public async Task Returns_Only_Requested_Ids()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var requested = Guid.NewGuid();
        var other = Guid.NewGuid();
        SeedRecord(db, companyId, requested, OnDate.AddDays(-1));
        SeedRecord(db, companyId, other, OnDate.AddDays(-1));
        await db.SaveChangesAsync();

        Assert.Equal(new[] { requested }, await Read(db, companyId, requested));
    }

    [Fact]
    public async Task Deduplicates_Multiple_Active_Records_For_The_Same_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, OnDate.AddDays(-1));
        SeedRecord(db, companyId, employeeId, OnDate.AddDays(-10));
        await db.SaveChangesAsync();

        Assert.Single(await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Empty_Id_List_Short_Circuits_To_Empty()
    {
        await using var db = BuildContext();
        Assert.Empty(await Read(db, Guid.NewGuid()));
    }
}
