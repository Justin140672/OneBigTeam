using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetMissingFitNotes;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetMissingFitNotesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly DueDate = new(2026, 7, 9);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<(Guid recordId, Guid employeeId)> SeedRecordAsync(SicknessDbContext db, Guid companyId)
    {
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, "Cold", 1, Now));

        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return (record.Id, employeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Pending_Requests_With_EmployeeId_From_Joined_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var (recordId, employeeId) = await SeedRecordAsync(db, companyId);

        var request = SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordId, Guid.Empty, DueDate, null, Now);
        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new GetMissingFitNotesHandler(db);
        var result = await handler.HandleAsync(new GetMissingFitNotesRequest(companyId), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(request.Id, item.RequestId);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(recordId, item.SicknessRecordId);
        Assert.Equal(DueDate, item.DueDate);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task HandleAsync_Includes_Overdue_Requests()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var (recordId, _) = await SeedRecordAsync(db, companyId);

        var request = SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordId, Guid.Empty, DueDate, null, Now);
        request.MarkOverdue(Now);
        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new GetMissingFitNotesHandler(db);
        var result = await handler.HandleAsync(new GetMissingFitNotesRequest(companyId), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Overdue", item.Status);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Fulfilled_And_Cancelled_Requests()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var (recordId1, _) = await SeedRecordAsync(db, companyId);
        var (recordId2, _) = await SeedRecordAsync(db, companyId);

        var fulfilled = SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordId1, Guid.Empty, DueDate, null, Now);
        fulfilled.Fulfil(Now);
        var cancelled = SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordId2, Guid.Empty, DueDate, null, Now);
        cancelled.Cancel(Now);
        db.SicknessEvidenceRequests.AddRange(fulfilled, cancelled);
        await db.SaveChangesAsync();

        var handler = new GetMissingFitNotesHandler(db);
        var result = await handler.HandleAsync(new GetMissingFitNotesRequest(companyId), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Requests_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var (recordId, _) = await SeedRecordAsync(db, otherCompanyId);

        var request = SicknessEvidenceRequest.Create(Guid.NewGuid(), otherCompanyId, recordId, Guid.Empty, DueDate, null, Now);
        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new GetMissingFitNotesHandler(db);
        var result = await handler.HandleAsync(new GetMissingFitNotesRequest(companyId), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
