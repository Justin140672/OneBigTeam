using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CreateLeaveType;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class CreateLeaveTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_LeaveType()
    {
        await using var db = BuildContext();
        var handler = new CreateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateLeaveTypeRequest
        {
            CompanyId = companyId,
            Name = "Annual Leave",
            Code = "annual",
            DefaultEntitlementDays = 25,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Annual Leave", result.Value.Name);
        Assert.Equal("ANNUAL", result.Value.Code);
        Assert.Equal(25, result.Value.DefaultEntitlementDays);
        Assert.Equal("Monthly", result.Value.AccrualMethod);
        Assert.Equal("Standard", result.Value.Behaviour);
        Assert.True(result.Value.IsActive);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.Equal("ANNUAL", saved.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Code_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        db.LeaveTypes.Add(LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));
        await db.SaveChangesAsync();

        var handler = new CreateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CreateLeaveTypeRequest
        {
            CompanyId = companyId,
            Name = "Annual Leave 2",
            Code = "annual",
            DefaultEntitlementDays = 20,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Code_In_Different_Companies()
    {
        await using var db = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.LeaveTypes.Add(LeaveType.Create(Guid.NewGuid(), companyA, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));
        await db.SaveChangesAsync();

        var handler = new CreateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CreateLeaveTypeRequest
        {
            CompanyId = companyB,
            Name = "Annual Leave",
            Code = "ANNUAL",
            DefaultEntitlementDays = 25,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
