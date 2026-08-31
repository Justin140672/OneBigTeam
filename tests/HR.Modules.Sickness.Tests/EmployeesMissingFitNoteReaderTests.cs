using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// DSH-05: <see cref="EmployeesMissingFitNoteReader"/> — an employee "is missing a fit note" when
/// they have a sickness evidence request in Pending or Overdue status (joined to the sickness
/// record for the employee id). Fulfilled / Cancelled requests do not count.
/// </summary>
public class EmployeesMissingFitNoteReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly DueDate = new(2026, 7, 9);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedRecord(SicknessDbContext db, Guid companyId, Guid employeeId)
    {
        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);
        return record.Id;
    }

    private static SicknessEvidenceRequest NewRequest(Guid companyId, Guid recordId) =>
        SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordId, Guid.Empty, DueDate, null, Now);

    private static Task<IReadOnlySet<Guid>> Read(SicknessDbContext db, Guid companyId, params Guid[] ids) =>
        new EmployeesMissingFitNoteReader(db).GetEmployeeIdsMissingFitNotesAsync(companyId, ids, CancellationToken.None);

    [Fact]
    public async Task Pending_Request_Is_Included_With_EmployeeId_From_Joined_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = SeedRecord(db, companyId, employeeId);
        db.SicknessEvidenceRequests.Add(NewRequest(companyId, recordId));
        await db.SaveChangesAsync();

        Assert.Equal(new[] { employeeId }, await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Overdue_Request_Is_Included()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = SeedRecord(db, companyId, employeeId);
        var request = NewRequest(companyId, recordId);
        request.MarkOverdue(Now);
        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();

        Assert.Contains(employeeId, await Read(db, companyId, employeeId));
    }

    [Fact]
    public async Task Fulfilled_And_Cancelled_Requests_Are_Excluded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var fulfilledEmployee = Guid.NewGuid();
        var cancelledEmployee = Guid.NewGuid();
        var fulfilledRecord = SeedRecord(db, companyId, fulfilledEmployee);
        var cancelledRecord = SeedRecord(db, companyId, cancelledEmployee);

        var fulfilled = NewRequest(companyId, fulfilledRecord);
        fulfilled.Fulfil(Now);
        var cancelled = NewRequest(companyId, cancelledRecord);
        cancelled.Cancel(Now);
        db.SicknessEvidenceRequests.AddRange(fulfilled, cancelled);
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyId, fulfilledEmployee, cancelledEmployee));
    }

    [Fact]
    public async Task Is_Scoped_By_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = SeedRecord(db, companyB, employeeId);
        db.SicknessEvidenceRequests.Add(NewRequest(companyB, recordId));
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
        db.SicknessEvidenceRequests.Add(NewRequest(companyId, SeedRecord(db, companyId, requested)));
        db.SicknessEvidenceRequests.Add(NewRequest(companyId, SeedRecord(db, companyId, other)));
        await db.SaveChangesAsync();

        Assert.Equal(new[] { requested }, await Read(db, companyId, requested));
    }

    [Fact]
    public async Task Deduplicates_Multiple_Pending_Requests_For_The_Same_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = SeedRecord(db, companyId, employeeId);
        db.SicknessEvidenceRequests.Add(NewRequest(companyId, recordId));
        db.SicknessEvidenceRequests.Add(NewRequest(companyId, recordId));
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
