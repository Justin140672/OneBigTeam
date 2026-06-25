using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetProbationRecord;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetProbationRecordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Record_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), "Some notes.", Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationRecordHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordRequest { CompanyId = companyId, Id = record.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(record.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(managerId, result.Value.ManagerEmployeeId);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal("Some notes.", result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Id()
    {
        await using var context = BuildContext();
        var handler = new GetProbationRecordHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var context = BuildContext();
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationRecordHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordRequest { CompanyId = Guid.NewGuid(), Id = record.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
