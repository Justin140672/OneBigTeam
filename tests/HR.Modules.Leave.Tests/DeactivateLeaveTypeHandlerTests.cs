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

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

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
        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Current_Employee_Has_LeaveBalance()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var employeeId = Guid.NewGuid();

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, entity.Id, Guid.NewGuid(), 2026, 25, new DateOnly(2026, 1, 1), now);
        db.LeaveBalances.Add(balance);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader([employeeId]), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("Annual Leave", result.Error.Message);
        Assert.Contains("1 active employee", result.Error.Message);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_LeaveType_When_LeaveBalance_Belongs_To_NonCurrent_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var terminatedEmployeeId = Guid.NewGuid();

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, terminatedEmployeeId, entity.Id, Guid.NewGuid(), 2026, 25, new DateOnly(2026, 1, 1), now);
        db.LeaveBalances.Add(balance);
        await db.SaveChangesAsync();

        // FakeCurrentEmployeeReader returns an empty list by default, simulating that
        // terminatedEmployeeId is not among the current (non-terminated) employees.
        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

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
    public async Task HandleAsync_Returns_Conflict_When_LeaveType_Is_System()
    {
        // Item 50: a system leave type (e.g. the platform-provisioned Annual Leave) can never be
        // deactivated, regardless of whether it's currently assigned to any employees.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now, isSystem: true);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("system leave type", result.Error.Message);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_NonSystem_LeaveType_Unaffected_By_IsSystem_Restriction()
    {
        // Confirms the IsSystem guard is opt-in: an ordinary (non-system) leave type can still be
        // deactivated exactly as before.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Compassionate Leave", "COMPASSIONATE", 5,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, isSystem: false);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

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
    public async Task HandleAsync_Returns_Conflict_When_Already_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        entity.Deactivate(now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveTypeDeactivatedAuditEvent()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var actorId = Guid.NewGuid();

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Compassionate Leave", "COMPASSIONATE", 5, AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = new DeactivateLeaveTypeHandler(db, new FakeClock(FixedUtcNow), new FakeCurrentEmployeeReader(), auditPublisher);

        var result = await handler.HandleAsync(new DeactivateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id,
            ActorEmployeeId = actorId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveTypeDeactivatedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(entity.Id, auditEvent.LeaveTypeId);
        Assert.Equal("Compassionate Leave", auditEvent.Name);
        Assert.Equal(actorId, auditEvent.ActorEmployeeIdValue);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
