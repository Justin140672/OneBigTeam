using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.DeactivateLeaveType;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class DeactivateLeaveTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_LeaveType()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        entity.Deactivate(now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
